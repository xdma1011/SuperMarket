@echo off
rem ===================================================================
rem bootstrap-admin.bat - calls POST /api/v1/system/bootstrap-admin on
rem the running API to create the first admin user + main branch on a
rem completely empty database. Works ONCE only - the endpoint itself
rem refuses if any user already exists (see BootstrapAdminHandler.cs).
rem
rem Requires the API to already be running (run-api.bat in another
rem window first). This script does NOT start the API itself - it only
rem calls it, same as any other HTTP client would.
rem ===================================================================

setlocal enabledelayedexpansion
cd /d "%~dp0"

call "%~dp0scripts\_discover.bat"
if errorlevel 1 (
    echo [FAILED] Could not discover project structure - see message above.
    goto :end_fail
)

set "LAUNCH_SETTINGS=%API_PROJECT_DIR%Properties\launchSettings.json"
set "API_URL="
if exist "%LAUNCH_SETTINGS%" (
    for /f "usebackq delims=" %%L in (`findstr /c:"applicationUrl" "%LAUNCH_SETTINGS%"`) do set "RAW_URL=%%L"
    if defined RAW_URL (
        set "RAW_URL=!RAW_URL:"applicationUrl": "=!"
        set "RAW_URL=!RAW_URL:",=!"
        set "RAW_URL=!RAW_URL: =!"
        for %%u in (!RAW_URL:;= !) do (
            if not defined API_URL set "API_URL=%%u"
        )
    )
)

if not defined API_URL (
    echo [FAILED] Could not read applicationUrl from launchSettings.json.
    echo          Pass the API URL manually, e.g.: bootstrap-admin.bat http://localhost:5200
    if not "%~1"=="" set "API_URL=%~1"
)

if not "%~1"=="" set "API_URL=%~1"

echo === Calling %API_URL%/api/v1/system/bootstrap-admin ===
echo (يفترض إن الـAPI شغّالة أصلًا بنافذة تانية - run-api.bat)
echo.

powershell -NoProfile -Command ^
    "try {" ^
    "  $r = Invoke-RestMethod -Method Post -Uri '%API_URL%/api/v1/system/bootstrap-admin' -ContentType 'application/json';" ^
    "  Write-Host '=== تم إنشاء المستخدم الإداري الأول بنجاح ===' -ForegroundColor Green;" ^
    "  Write-Host ('اسم المستخدم : ' + $r.username);" ^
    "  Write-Host ('كلمة السر    : ' + $r.password);" ^
    "  Write-Host ('الفرع        : ' + $r.branchName);" ^
    "  Write-Host '';" ^
    "  Write-Host 'غيّر كلمة السر فورًا بعد أول دخول.' -ForegroundColor Yellow;" ^
    "} catch {" ^
    "  $resp = $_.Exception.Response;" ^
    "  if ($resp -and $resp.StatusCode.value__ -eq 409) {" ^
    "    Write-Host 'يوجد مستخدم مسجَّل أصلًا بالنظام - هذا السكربت يعمل مرة واحدة بس على قاعدة بيانات فاضية.' -ForegroundColor Yellow;" ^
    "  } else {" ^
    "    Write-Host ('[FAILED] تعذّر الاتصال بالـAPI على %API_URL% - تأكد إنها شغّالة (run-api.bat) بنافذة تانية.') -ForegroundColor Red;" ^
    "    Write-Host $_.Exception.Message -ForegroundColor Red;" ^
    "  }" ^
    "}"

echo.
echo Press any key to close this window . . .
pause >nul
exit /b 0

:end_fail
echo.
echo Press any key to close this window . . .
pause >nul
exit /b 1
