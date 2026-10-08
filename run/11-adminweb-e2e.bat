@echo off
rem ===================================================================
rem run\11-adminweb-e2e.bat - admin web in a real browser (Playwright):
rem purchase invoices (piece + carton + batch), supplier payments,
rem expenses, cash-closing "explain the difference", partner "stale"
rem badge, rejected sales, stocktake counting, employees - the numbers
rem on screen are compared with the API.
rem Uses the TEST database sprmrkt_ui_test, the API on port 5000 and the
rem admin web on 4200 (both must be free - close them first).
rem Uses the installed Google Chrome (no browser download).
rem Report: tools\AdminWebE2E\report\index.html
rem ===================================================================
setlocal
chcp 65001 >nul
title Supermarket - Admin Web E2E
cd /d "%~dp0.."
powershell -NoProfile -ExecutionPolicy Bypass -File "tools\AdminWebE2E\run-e2e.ps1"
if errorlevel 1 (
    echo.
    echo [FAILED] See messages above / tools\AdminWebE2E\report\index.html
    pause >nul
    exit /b 1
)
echo.
echo [OK] All admin web browser tests passed.
pause >nul
exit /b 0
