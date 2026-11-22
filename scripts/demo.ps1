param(
    [string]$BaseUrl = "http://localhost:8080",
    [string]$ImagePath = ""
)
$ErrorActionPreference = "Stop"
$email = "demo-$([Guid]::NewGuid().ToString('N'))@example.com"
$password = "Demo-" + [Guid]::NewGuid().ToString('N')
function Send-Json([string]$Method, [string]$Path, $Body, $Headers = @{}) {
    $json = $Body | ConvertTo-Json -Depth 10
    Invoke-RestMethod -Method $Method -Uri "$BaseUrl$Path" -Headers $Headers -ContentType "application/json; charset=utf-8" -Body ([Text.Encoding]::UTF8.GetBytes($json))
}
Send-Json "Post" "/api/auth/register" @{ email=$email; password=$password; displayName="Навчальна демонстрація" } | Out-Null
$tokens = Send-Json "Post" "/api/auth/login" @{ email=$email; password=$password }
$headers = @{ Authorization="Bearer $($tokens.accessToken)" }
Send-Json "Put" "/api/me/components/1" @{ reason="allergy"; note="Молоко" } $headers | Out-Null
Send-Json "Put" "/api/me/components/6" @{ reason="avoid"; note="Арахіс" } $headers | Out-Null
if ($ImagePath) {
    # HttpClient also works on Windows PowerShell 5.1 (which has no Invoke-RestMethod -Form).
    Add-Type -AssemblyName System.Net.Http
    $client = [System.Net.Http.HttpClient]::new()
    $client.Timeout = [TimeSpan]::FromSeconds(120)
    $client.DefaultRequestHeaders.Authorization = [System.Net.Http.Headers.AuthenticationHeaderValue]::new("Bearer", $tokens.accessToken)
    $form = [System.Net.Http.MultipartFormDataContent]::new()
    try {
        $file = [IO.File]::OpenRead((Resolve-Path $ImagePath).Path)
        $content = [System.Net.Http.StreamContent]::new($file)
        $form.Add($content, "image", [IO.Path]::GetFileName($ImagePath))
        $response = $client.PostAsync("$BaseUrl/api/analyses", $form).GetAwaiter().GetResult()
        $body = $response.Content.ReadAsStringAsync().GetAwaiter().GetResult()
        if (-not $response.IsSuccessStatusCode) { throw "HTTP $([int]$response.StatusCode): $body" }
        $result = $body | ConvertFrom-Json
    } finally { $form.Dispose(); $client.Dispose() }
} else {
    $result = Send-Json "Post" "/api/analyses/text" @{ text="Склад: молоко, цукор. Може містити арахіс."; productName="Навчальний приклад" } $headers
}
$result | ConvertTo-Json -Depth 20
Invoke-RestMethod -Uri "$BaseUrl/api/analyses" -Headers $headers | ConvertTo-Json -Depth 10
Write-Host "Готово. Тестовий користувач: $email"
