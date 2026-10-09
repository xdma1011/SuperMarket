@echo off
rem ===================================================================
rem run\12-customer-app-tests.bat - customer app (Flutter) tests:
rem unit tests (cart, models) always; the live flow (branches, catalog,
rem OTP request, delivery order, order detail/list, coupon error) only
rem when the API is running on http://localhost:5000 against a TEST
rem database. Needs the Flutter SDK (default C:\Users\<you>\flutter).
rem ===================================================================
setlocal
chcp 65001 >nul
title Supermarket - Customer App Tests
if exist "%USERPROFILE%\flutter\bin\flutter.bat" set "PATH=%USERPROFILE%\flutter\bin;%PATH%"
where flutter >nul 2>&1
if errorlevel 1 (
    echo [FAILED] Flutter SDK not found. Install it to %USERPROFILE%\flutter or add it to PATH.
    pause >nul
    exit /b 1
)
cd /d "%~dp0..\SupermarketSystem.CustomerApp"
call flutter pub get
call flutter analyze
if errorlevel 1 goto :fail
set "API_FLAG=false"
powershell -NoProfile -Command "try { Invoke-WebRequest http://localhost:5000/api/v1/system/time-settings -UseBasicParsing -TimeoutSec 3 | Out-Null; exit 0 } catch { exit 1 }"
if not errorlevel 1 set "API_FLAG=true"
echo Live API tests: %API_FLAG%
call flutter test --dart-define=RUN_API_TESTS=%API_FLAG% --dart-define=API_BASE_URL=http://localhost:5000/api/v1
if errorlevel 1 goto :fail
echo.
echo [OK] Customer app tests passed.
pause >nul
exit /b 0
:fail
echo.
echo [FAILED] See messages above.
pause >nul
exit /b 1
