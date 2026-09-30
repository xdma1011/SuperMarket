@echo off
rem ===================================================================
rem run\1-api.bat - runs the API (dotnet run) with its launchSettings
rem profile: http://localhost:5000 , Swagger at /swagger.
rem No absolute path, no assumed folder/project name - see _find.bat.
rem The window stays open at the end (success or failure).
rem ===================================================================

setlocal enabledelayedexpansion
title Supermarket - API
call "%~dp0_find.bat"
if not defined API_PROJECT (
    echo [FAILED] API project not found ^(no .csproj with Sdk="Microsoft.NET.Sdk.Web"^).
    goto :end_fail
)
where dotnet >nul 2>&1
if errorlevel 1 (
    echo [FAILED] .NET SDK not found. Install .NET SDK 10 from https://dotnet.microsoft.com/download
    goto :end_fail
)

echo === API: !API_PROJECT! ===
echo === Usually http://localhost:5000/swagger - the certain URL is the "Now listening on:" line below ===
echo === Ctrl+C stops the server ===
echo.
dotnet run --project "!API_PROJECT!"
if errorlevel 1 (
    echo [FAILED] dotnet run exited with an error - see messages above.
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
