@echo off
rem ===================================================================
rem run\10-cashier-ui-automation.bat - drives the REAL cashier (WPF)
rem screens with Windows UI Automation: random sales (barcode, quantity,
rem carton, promotion), credit sales, held carts, "sync now", and the
rem API is stopped/started at random. At the end every sale the cashier
rem showed must exist on the server once, with the same total and paid.
rem Uses the TEST database sprmrkt_ui_test (created/migrated here) and a
rem temporary cashier data folder - never %LocalAppData%.
rem The cashier window opens on screen: don't use the mouse/keyboard on it
rem while it runs. Log: %TEMP%\spkt-ui-<time>\ui-log.txt
rem ===================================================================
setlocal
chcp 65001 >nul
title Supermarket - Cashier UI Automation
cd /d "%~dp0.."
set "UI_DB=sprmrkt_ui_test"
dotnet ef database update --project SupermarketSystem.Infrastructure --startup-project SupermarketSystem.API --connection "Server=localhost;Database=%UI_DB%;Trusted_Connection=True;TrustServerCertificate=True;"
if errorlevel 1 goto :fail
dotnet build SupermarketSystem.API -v q -nologo
if errorlevel 1 goto :fail
dotnet build SupermarketSystem.CashierApp -v q -nologo
if errorlevel 1 goto :fail
set "OPS="
set /p "OPS=Operations? [40]: "
if "%OPS%"=="" set "OPS=40"
powershell -NoProfile -ExecutionPolicy Bypass -File "tools\CashierUiAutomation\cashier-ui-loop.ps1" -Operations %OPS% -Database %UI_DB%
if errorlevel 1 goto :fail
echo.
echo [OK] Every sale on the cashier screen matches the server.
pause >nul
exit /b 0
:fail
echo.
echo [FAILED] See the messages above and ui-log.txt.
echo          "Application Control policy has blocked" = Windows Smart App Control, not a test failure.
pause >nul
exit /b 1
