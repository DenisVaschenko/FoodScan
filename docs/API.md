# Контракти API

Базова адреса: `http://localhost:8080`. JSON — camelCase, enum — рядки. Час — UTC ISO 8601.
На захищені маршрути передавайте `Authorization: Bearer ACCESS_TOKEN`.

| Метод | Шлях | Вхід | Успішна відповідь |
|---|---|---|---|
| POST | `/api/auth/register` | `email`, `password`, `displayName` | 201, профіль |
| POST | `/api/auth/login` | `email`, `password` | 200, токени |
| POST | `/api/auth/refresh` | `refreshToken` | 200, нова пара токенів |
| POST | `/api/auth/logout` | `refreshToken` | 204 |
| GET | `/api/me` | — | 200, профіль |
| PUT | `/api/me` | `displayName` | 200, профіль |
| GET | `/api/components?q=молоко` | необов'язковий пошук | 200, масив компонентів |
| GET | `/api/me/components` | — | 200, персональний список |
| PUT | `/api/me/components/{componentId}` | `reason`, необов'язковий `note` | 200, створений/оновлений запис |
| DELETE | `/api/me/components/{componentId}` | — | 204 (ідемпотентно) |
| POST | `/api/analyses` | multipart: `image`, необов'язковий `productName` | 201, деталі аналізу |
| POST | `/api/analyses/text` | `text`, необов'язковий `productName` | 201, деталі аналізу |
| POST | `/api/analyses/{id}/reanalyze` | виправлений `text`, необов'язковий `productName` | 201, **новий** аналіз |
| GET | `/api/analyses?page=1&pageSize=20` | пагінація | 200, `{page,pageSize,total,items}` |
| GET | `/api/analyses/{id}` | — | 200, деталі аналізу |
| DELETE | `/api/analyses/{id}` | — | 204 |
| GET | `/health` | — | 200, стан процесу |
| GET | `/health/ready` | — | 200/503, доступність БД |
| GET | `/openapi/v1.json` | — | OpenAPI, лише Development |

Чотири auth-маршрути та health/openapi не потребують access token. Для logout достатньо самого refresh token — це секрет, не передавайте його в URL.

## Авторизація

```json
{"email":"student@example.com","password":"LongPassword-123","displayName":"Студент"}
```

Пароль: 10–128 символів, ім'я: 1–80 символів, email: до 254 символів. Пароль зберігається як PBKDF2-хеш через ASP.NET Core PasswordHasher.

Вхід повертає:

```json
{"accessToken":"...","refreshToken":"...","expiresIn":900,"tokenType":"Bearer"}
```

Access token: 15 хвилин. Refresh-сесія: 14 днів від входу, у БД лише SHA-256-хеш випадкового токена.
Кожний успішний refresh замінює попередній токен. Паралельний refresh однієї сесії захищений optimistic concurrency.
Зберігайте **новий** refresh token; старий повторно не працюватиме.

## Персональний список

```json
{"reason":"allergy","note":"Уникати молочних компонентів"}
```

`reason`: `allergy` — алергія, `avoid` — бажання уникати, `track` — просте відстеження.
Примітка до 300 символів. Ідентифікатори беріть із `/api/components`, не зіставляйте за позицією в масиві.

## Фото

```bash
curl -X POST http://localhost:8080/api/analyses \
  -H "Authorization: Bearer ACCESS_TOKEN" \
  -F "image=@ingredients.jpg" \
  -F "productName=Печиво"
```

Рівно один файл `image`; JPEG/PNG до 8 MiB та 20 млн пікселів. Content-Type з boundary встановлює HTTP-клієнт, не задавайте його вручну.
Мінімальні ширина й висота — 30 пікселів. HEIC/WebP/PDF не підтримуються.

## Результат

Нижче ілюстрація структури; фактичний набір інгредієнтів залежить від етикетки та відповіді LLM.

```json
{
  "id":"11111111-1111-1111-1111-111111111111",
  "parentAnalysisId":null,
  "createdAt":"2026-11-16T12:00:00+00:00",
  "productName":"Приклад",
  "source":"text",
  "rawText":"Склад: молоко. Може містити арахіс.",
  "status":"completed",
  "model":"configured-model-id",
  "result":{
    "ingredients":[
      {"name":"молоко","evidence":"молоко","presence":"ingredient","componentId":1},
      {"name":"арахіс","evidence":"Може містити арахіс","presence":"mayContain","componentId":6}
    ],
    "matches":[
      {"componentId":1,"componentName":"Молоко","reason":"allergy","ingredientName":"молоко","evidence":"молоко","presence":"ingredient","matchKind":"direct"}
    ],
    "trackingSnapshot":[{"componentId":1,"name":"Молоко","reason":"allergy","note":""}],
    "warnings":[],
    "needsReview":false,
    "notice":"Збігів може бути не знайдено через помилки OCR або неповний довідник. Результат не гарантує безпечність продукту."
  }
}
```

- `presence=ingredient` — зазначено у складі; `mayContain` — можливі сліди/перехресне забруднення.
- `matchKind=direct` — точний синонім; `relatedGroup` — зв'язок компонента з відстежуваною групою.
- `componentId=null` — інгредієнт відсутній у довіднику; він не відкидається, але потрібна ручна перевірка.
- `status=needsReview` — є проблема повноти, невідомий компонент або сумнів у розпізнаванні.
- `completed` та порожній `matches` **не означають «безпечно»**.
- `source`: `photo`, `text`, `correctedText`.

Повторний аналіз створює новий запис із `parentAnalysisId` та поточним персональним списком. Старий запис залишається незмінним. Якщо старий запис видалено, його ідентифікатор у нащадку зберігається як історичне посилання; це не FK.

## Помилки

Бізнес-помилки повертаються як `application/problem+json` із `status`, `title` (код), `detail` та `traceId`. Стандартні 401/404/429 можуть не мати JSON-тіла.

| Статус | Приклади |
|---|---|
| 400 | неправильний JSON/enum, порожній текст, пошкоджений файл, два фото |
| 401 | неправильний пароль, прострочений JWT/refresh token |
| 404 | об'єкт не існує **або належить іншому користувачу** |
| 409 | email зайнято, конкурентне редагування персонального списку |
| 413 | завеликий файл/запит |
| 415 | потрібен multipart/form-data |
| 429 | ліміт запитів або два аналізи вже виконуються |
| 502 | помилка HTTP-провайдера або некоректний JSON від LLM |
| 503 | OCR/LLM не налаштовані, OCR не запустився, квоту LLM вичерпано |
| 504 | перевищено тайм-аут OCR/LLM |

Невдала спроба через недоступність провайдера не створює успішного запису в історії. Порожній OCR-текст створює `needsReview` без виклику LLM, щоб користувач міг подати виправлений текст.
