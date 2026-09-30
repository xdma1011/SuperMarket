@echo off
rem ===================================================================
rem run\3-cashier.bat - runs the WPF cashier app (Windows only). It
rem reads the API address from appsettings.json next to the app
rem (ApiBaseUrl) - the API must be reachable for login/sync; selling
rem works offline after the first sync.
rem No absolute path, no assumed folder/project name - see _find.bat.
rem The window stays open at the end (success or failure).
rem ===================================================================

setlocal enabledelayedexpansion
title Supermarket - Cashier
call "%~dp0_find.bat"
if not defined CASHIER_PROJECT (
    echo [FAILED] Cashier project not found ^(no .csproj with UseWPF^).
    goto :end_fail
)
where dotnet >nul 2>&1
if errorlevel 1 (
    echo [FAILED] .NET SDK not found. Install .NET SDK 10 from https://dotnet.microsoft.com/download
    goto :end_fail
)

echo === Cashier: !CASHIER_PROJECT! ===
echo.
dotnet run --project "!CASHIER_PROJECT!"
if errorlevel 1 (
    echo [FAILED] The cashier app exited with an error - see messages above.
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
