# FoodScan — аналіз складу харчових продуктів

Навчальний серверний проєкт на **C# / ASP.NET Core (.NET 10)**. Приймає одну фотографію українського складу, розпізнає текст через Tesseract, виділяє інгредієнти через Gemini і зіставляє їх із персональним списком користувача.

## Що реалізовано

- Реєстрація, вхід, JWT access token, одноразове оновлення refresh token, вихід.
- Профіль: email та ім'я; редагування імені.
- Довідник із 30 демонстраційних компонентів, українськими синонімами та зв'язками груп.
- Персональний список: `allergy`, `avoid`, `track` та примітка.
- Одна фотографія JPEG/PNG до 8 MiB, до 20 млн пікселів; перевірка декодування, EXIF-орієнтація, видалення тимчасового фото.
- Локальний Tesseract (`ukr+eng`); англійська допомагає прочитати E-коди. Семантичний аналіз орієнтовано на українську.
- Gemini REST API: структурована відповідь, перевірка JSON, підтвердження інгредієнтів фрагментами OCR.
- Інгредієнти у складі та «можливі сліди» повертаються окремими значеннями `presence`.
- Історія, пагінація, перегляд, видалення, повторний аналіз виправленого тексту.
- Знімок персонального списку на момент аналізу. Старі результати не перераховуються при зміні налаштувань.
- PostgreSQL, автоматичне створення початкової схеми, SQL-експорт, OpenAPI, автоматизовані тести.

**Це прототип, не медичний інструмент.** `completed` означає завершення обробки, а не безпечність продукту. Навіть точна цитата не доводить правильність OCR або повноту вилучення інгредієнтів. Невідомі довіднику компоненти зберігаються й позначаються для ручної перевірки.

## Найпростіший запуск — Docker Desktop

Потрібен Docker Desktop з Linux containers. Локальний .NET і окреме встановлення Tesseract не потрібні.

1. Розпакуйте архів, відкрийте термінал у каталозі `FoodScan`.
2. Скопіюйте `.env.example` у `.env` (PowerShell: `Copy-Item .env.example .env`).
3. Заповніть `.env`:

```dotenv
POSTGRES_PASSWORD=YOUR_RANDOM_ALPHANUMERIC_PASSWORD
JWT_KEY=YOUR_RANDOM_SECRET_AT_LEAST_32_BYTES_LONG
GEMINI_API_KEY=YOUR_GOOGLE_AI_STUDIO_API_KEY
GEMINI_MODEL=MODEL_ID_AVAILABLE_ON_YOUR_FREE_TIER
```

Для `POSTGRES_PASSWORD` використайте випадкові літери/цифри без `;`, `$`, лапок і пробілів; пароль включається в рядок підключення.
JWT-секрет можна згенерувати в PowerShell:

```powershell
$bytes = New-Object byte[] 48
$rng = [System.Security.Cryptography.RandomNumberGenerator]::Create()
$rng.GetBytes($bytes)
[Convert]::ToBase64String($bytes)
$rng.Dispose()
```

Отримайте ключ у [Google AI Studio](https://aistudio.google.com/api-keys).
Перевірте доступну безкоштовну модель у своєму проєкті. Значення `GEMINI_MODEL` навмисно не зашито в код: моделі та безкоштовні квоти змінюються.
API-ключ потрібен для реального аналізу, але авторизація, профіль і довідник працюють без нього; аналіз поверне `503 llm_not_configured`.
Підписка ChatGPT Plus для цього не використовується.

4. Запустіть:

```powershell
docker compose up --build -d
docker compose logs -f api
```

5. Адреси:

- API: `http://localhost:8080`
- Перевірка процесу: `http://localhost:8080/health`
- Перевірка БД: `http://localhost:8080/health/ready`
- OpenAPI JSON: `http://localhost:8080/openapi/v1.json` (Development)

Це бекенд: на `/` немає вебінтерфейсу. Запити виконуйте через Postman, curl, `.http`-файл або PowerShell.
OpenAPI JSON можна імпортувати в Postman; точний формат завантаження файлу описано в `docs/API.md`.

Зупинка: `docker compose down`. Дані залишаться в Docker volume.
**`docker compose down -v` видаляє базу** — використовуйте лише для свідомого скидання навчальних даних.

## Швидка перевірка без фото

Після запуску виконайте в PowerShell:

```powershell
./scripts/demo.ps1
```

Скрипт створить нового тестового користувача, увійде, додасть молоко та арахіс до списку, проаналізує приклад українського складу, покаже результат і історію. Він викликає реальну LLM: потрібні налаштовані ключ і модель.
Щоб надіслати власне фото:

```powershell
./scripts/demo.ps1 -ImagePath "C:\Photos\ingredients.jpg"
```

Фотографуйте крупним планом саме склад: без відблисків, із прямими рядками тексту. Поточний режим OCR (`--psm 6`) розрахований на один блок тексту.

## Запуск C# локально / Visual Studio

Потрібні .NET 10 SDK, PostgreSQL 17 та Tesseract 5 з `ukr` і `eng`. Відкрийте `FoodScan.sln` у сумісній із .NET 10 IDE або працюйте через CLI.

Для PostgreSQL можна використати лише контейнер:

```powershell
docker compose up -d db
```

Збережіть локальні секрети (з кореня проєкту):

```powershell
dotnet user-secrets set "ConnectionStrings:Default" "Host=localhost;Database=foodscan;Username=foodscan;Password=YOUR_DB_PASSWORD" --project src/FoodScan.Api
dotnet user-secrets set "Jwt:Key" "YOUR_RANDOM_SECRET_AT_LEAST_32_BYTES_LONG" --project src/FoodScan.Api
dotnet user-secrets set "Gemini:ApiKey" "YOUR_API_KEY" --project src/FoodScan.Api
dotnet user-secrets set "Gemini:Model" "YOUR_MODEL_ID" --project src/FoodScan.Api
dotnet user-secrets set "Ocr:Executable" "C:\Program Files\Tesseract-OCR\tesseract.exe" --project src/FoodScan.Api
$env:ASPNETCORE_ENVIRONMENT = "Development"
dotnet run --project src/FoodScan.Api --urls http://localhost:8080
```

На Linux для Tesseract: `sudo apt-get install tesseract-ocr tesseract-ocr-ukr tesseract-ocr-eng`.
На Windows перевірте інсталяцію за [офіційною інструкцією Tesseract](https://tesseract-ocr.github.io/tessdoc/Installation.html) та наявність `ukr.traineddata`/`eng.traineddata` у `tessdata`.

## Тести

```powershell
dotnet test FoodScan.sln -c Release
```

Інтеграційні тести використовують реляційну SQLite in-memory та контрольовані заміни OCR/LLM. Справжні HTTP-маршрути, JWT, хешування паролів, валідація та EF Core працюють без замін.
Тести не потребують API-ключа, PostgreSQL або встановленого Tesseract. Перевірка декодування зображень виконується справжнім SkiaSharp.

Альтернативний запуск тих самих тестів без окремого процесу VSTest:

```powershell
dotnet run --project tests/FoodScan.Tests -c Release -- --selftest
```

Опис фактично виконаних перевірок: `docs/VALIDATION.md`.

## Як організовано код

| Каталог / файл | Призначення |
|---|---|
| `src/FoodScan.Api/Endpoints` | HTTP-контракти, перевірка власника даних, валідація |
| `Auth/TokenService.cs` | JWT, генерація та хешування refresh token |
| `Data/AppDbContext.cs` | Таблиці, ключі, індекси, початкові дані |
| `Data/Catalog.cs` | Компоненти, синоніми та зв'язки груп |
| `Services/TesseractOcr.cs` | Підготовка фото, окремий процес Tesseract з тайм-аутом |
| `Services/GeminiExtractor.cs` | Промпт, JSON-схема, виклик Gemini та перевірка відповіді |
| `Services/ComponentMatcher.cs` | Детерміноване зіставлення та перевірка цитат |
| `Services/AnalysisService.cs` | Обробка й збереження результату |
| `docs/API.md`, `requests.http` | Контракти та приклади всіх запитів |
| `docs/schema.sql` | Початкова схема PostgreSQL із seed-даними |

OCR та LLM замінюються через `IOcrService` і `IIngredientExtractor`.
Зовнішньому сервісу надсилається лише текст етикетки — без профілю, персонального списку та зображення.
Профіль і розпізнаний текст зберігаються у вашій БД. Фото після запиту видаляється.

## Межі першої версії

- Довідник демонстраційний, не повна база харчових алергенів. Вибір персональних компонентів — із довідника; додавання довільного компонента через API не реалізовано.
- Зіставлення використовує точні нормалізовані синоніми, не стемінг і не нечіткий пошук. Нові форми потрібно додавати в довідник; нерозпізнані форми потрапляють у warnings.
- `relatedGroup` означає зв'язок із групою. Це не оцінка індивідуальної реакції, концентрації або клінічного ризику; наприклад, лактоза та молочні білки — різні речовини.
- Модель може пропускати компоненти, хибно читати заперечення чи вкладений склад. Потрібна оцінка на власній вибірці фотографій. Реальні показники точності поки не виміряно.
- Обробка синхронна: тайм-аут OCR 30 с, LLM 60 с, до двох аналізів одночасно. При завантаженні повертається 429, автоматичної черги/повторів немає.
- Немає фронтенда, відновлення пароля, email-підтвердження, ролей адміністратора й редагування довідника через API.
- Logout відкликає refresh token. Уже виданий JWT чинний до 15 хвилин; клієнт повинен видалити обидва токени.
- Локальний Docker використовує HTTP, прив'язаний до `127.0.0.1`. Для зовнішнього розгортання потрібні HTTPS та окрема конфігурація хоста.
- Gemini може мати нульову/вичерпану квоту для окремих моделей або акаунтів. Відповідь 503 не замінюється вигаданим результатом.

## Зміни схеми БД

Для простоти навчального запуску використано `EnsureCreated`, без EF migrations. На порожній базі створюються таблиці й довідник. **Це не оновлює існуючу схему чи seed-дані.**
`docs/schema.sql` — альтернативне ручне створення порожньої БД, а не міграція; автоматичному запуску цей файл не потрібен.
Під час наступної зміни моделі можна скинути лише непотрібні тестові дані або перейти на EF migrations з окремим планом перенесення. Не змішуйте `EnsureCreated` та `Migrate` на вже створеній базі.

Генерація SQL з поточної моделі без підключення до PostgreSQL:

```powershell
dotnet run --project tests/FoodScan.Tests -c Release -- --schema docs/schema.sql
```

## Документація залежностей

- [ASP.NET Core](https://learn.microsoft.com/aspnet/core/)
- [Npgsql EF Core](https://www.npgsql.org/efcore/)
- [Tesseract](https://github.com/tesseract-ocr/tesseract)
- [SkiaSharp](https://github.com/mono/SkiaSharp)
- [Gemini generateContent](https://ai.google.dev/api/generate-content)
- [Gemini тарифи та квоти](https://ai.google.dev/gemini-api/docs/pricing)

Власного навчання нейромереж немає: OCR використовує готові мовні ваги, LLM — зовнішній API.
