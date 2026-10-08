# بيشغّل اختبارات لوحة الإدارة (Playwright) من الصفر: قاعدة اختبار (Migrations) ← API على 5000 ← ng serve على 4200 ← الاختبارات ← بيطفّي الكل.
# القاعدة لازم اسمها فيه "test". الـAPI بياخد الاتصال من متغيّر بيئة (ما بنلمس appsettings).
param([string]$Database = "sprmrkt_ui_test", [string]$SqlServer = "localhost")
$ErrorActionPreference = 'Stop'
if ($Database -notmatch 'test') { throw "Database name must contain 'test'." }

$repo = (Resolve-Path (Join-Path $PSScriptRoot "..\..")).Path
$cs = "Server=$SqlServer;Database=$Database;Trusted_Connection=True;TrustServerCertificate=True;"
foreach ($port in 5000, 4200) {
    if (Get-NetTCPConnection -LocalPort $port -State Listen -ErrorAction SilentlyContinue) { throw "Port $port is busy - close the running API / admin web first." }
}

Push-Location $repo
try {
    Write-Host "=== Migrations on $Database ==="
    dotnet ef database update --project SupermarketSystem.Infrastructure --startup-project SupermarketSystem.API --connection $cs
    if ($LASTEXITCODE -ne 0) { throw "migrations failed" }
    dotnet build SupermarketSystem.API -v q -nologo
    if ($LASTEXITCODE -ne 0) { throw "API build failed" }

    $env:ConnectionStrings__DefaultConnection = $cs
    if (-not $env:Jwt__SigningKey) { $env:Jwt__SigningKey = "e2e-only-signing-key-" + [guid]::NewGuid().ToString("N") }
    $env:ASPNETCORE_URLS = "http://localhost:5000"
    $env:RateLimiting__Enabled = "false"
    $api = Start-Process dotnet -ArgumentList "SupermarketSystem.API.dll" -WorkingDirectory (Join-Path $repo "SupermarketSystem.API\bin\Debug\net10.0") -PassThru -WindowStyle Hidden
    $web = Start-Process cmd -ArgumentList "/c", "npx ng serve --port 4200" -WorkingDirectory (Join-Path $repo "SupermarketSystem.AdminWeb") -PassThru -WindowStyle Hidden

    Write-Host "=== Waiting for API and admin web ==="
    $deadline = (Get-Date).AddMinutes(4)
    do {
        Start-Sleep -Seconds 3
        $ok = $true
        try { Invoke-WebRequest "http://localhost:5000/api/v1/system/time-settings" -UseBasicParsing -TimeoutSec 3 | Out-Null } catch { $ok = $false }
        try { Invoke-WebRequest "http://localhost:4200/" -UseBasicParsing -TimeoutSec 3 | Out-Null } catch { $ok = $false }
    } while (-not $ok -and (Get-Date) -lt $deadline)
    if (-not $ok) { throw "API or admin web did not start" }

    # قاعدة فاضية: أدمن أول مرة (admin / 123). موجود أصلًا = 409 عادي.
    try { Invoke-RestMethod -Method Post "http://localhost:5000/api/v1/system/bootstrap-admin" | Out-Null } catch { }

    Push-Location (Join-Path $PSScriptRoot "")
    if (-not (Test-Path "node_modules")) { $env:PLAYWRIGHT_SKIP_BROWSER_DOWNLOAD = "1"; npm install --no-audit --no-fund }
    npx playwright test
    $code = $LASTEXITCODE
    Pop-Location
    exit $code
}
finally {
    if ($api -and -not $api.HasExited) { Stop-Process -Id $api.Id -Force }
    Get-CimInstance Win32_Process -Filter "Name='node.exe'" | Where-Object { $_.CommandLine -like '*ng*serve*--port 4200*' } | ForEach-Object { Stop-Process -Id $_.ProcessId -Force }
    Pop-Location
}
