@echo off
rem ===================================================================
rem run\2-admin-web.bat - runs the Angular admin panel (ng serve) and
rem opens the browser. Installs packages automatically on first run.
rem The panel talks to the API at http://localhost:5000 - start
rem 1-api.bat first (or use 0-api-and-web.bat).
rem No absolute path, no assumed folder/project name - see _find.bat.
rem The window stays open at the end (success or failure).
rem ===================================================================

setlocal enabledelayedexpansion
title Supermarket - Admin Web
call "%~dp0_find.bat"
if not defined ADMINWEB_DIR (
    echo [FAILED] Admin web project not found ^(no folder with angular.json^).
    goto :end_fail
)
where npm >nul 2>&1
if errorlevel 1 (
    echo [FAILED] Node.js / npm not found. Install Node.js LTS from https://nodejs.org
    goto :end_fail
)
cd /d "!ADMINWEB_DIR!"
if not exist "node_modules\" (
    echo === First run: installing packages ^(exact versions from package-lock.json^) ===
    if exist "package-lock.json" (
        call npm ci
    ) else (
        call npm install
    )
    if errorlevel 1 (
        echo [FAILED] Package install failed - see messages above.
        goto :end_fail
    )
)

echo === Admin web: !ADMINWEB_DIR! ===
echo === Opens http://localhost:4200 when ready - Ctrl+C stops it ===
echo.
call npx ng serve --open
if errorlevel 1 (
    echo [FAILED] ng serve exited with an error - see messages above.
    goto :end_fail
)
echo.
echo Press any key to close this window . . .
pause >nul
exit /b 0

:end_fail
echo.
echo Press any key to close this window . . .
pause >nul
exit /b 1
