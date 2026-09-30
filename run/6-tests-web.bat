@echo off
rem ===================================================================
rem run\6-tests-web.bat - runs the admin panel unit tests once
rem (Karma + Chrome, no watch mode). Installs packages on first run.
rem No absolute path, no assumed folder/project name - see _find.bat.
rem The window stays open at the end (success or failure).
rem ===================================================================

setlocal enabledelayedexpansion
title Supermarket - Web Tests
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

echo === Web tests: !ADMINWEB_DIR! ===
echo.
call npx ng test --watch=false
if errorlevel 1 (
    echo.
    echo [FAILED] Some web tests failed - see messages above.
    goto :end_fail
)
echo.
echo [OK] All web tests passed.
echo.
echo Press any key to close this window . . .
pause >nul
exit /b 0

:end_fail
echo.
echo Press any key to close this window . . .
pause >nul
exit /b 1
