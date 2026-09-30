@echo off
rem ===================================================================
rem run\4-customer-app.bat - runs the Flutter customer app on a
rem connected phone/emulator (Flutter asks which device if several).
rem Requires the Flutter SDK on PATH.
rem No absolute path, no assumed folder/project name - see _find.bat.
rem The window stays open at the end (success or failure).
rem ===================================================================

setlocal enabledelayedexpansion
title Supermarket - Customer App
call "%~dp0_find.bat"
if not defined CUSTOMER_DIR (
    echo [FAILED] Customer app not found ^(no folder with pubspec.yaml^).
    goto :end_fail
)
where flutter >nul 2>&1
if errorlevel 1 (
    echo [FAILED] Flutter SDK not found. Install it from https://docs.flutter.dev/get-started/install
    goto :end_fail
)
cd /d "!CUSTOMER_DIR!"
echo === Customer app: !CUSTOMER_DIR! ===
call flutter pub get
if errorlevel 1 (
    echo [FAILED] flutter pub get failed - see messages above.
    goto :end_fail
)
call flutter run
if errorlevel 1 (
    echo [FAILED] flutter run exited with an error - see messages above.
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
