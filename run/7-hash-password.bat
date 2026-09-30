@echo off
rem ===================================================================
rem run\7-hash-password.bat - small tool: type a password, it prints
rem the hash ready to paste into Users.PasswordHash.
rem No absolute path, no assumed folder/project name - see _find.bat.
rem The window stays open at the end (success or failure).
rem ===================================================================

setlocal enabledelayedexpansion
title Supermarket - Hash Password
call "%~dp0_find.bat"
if not defined HASHPW_PROJECT (
    echo [FAILED] HashPassword tool not found ^(no console .csproj using Microsoft.Extensions.Identity.Core^).
    goto :end_fail
)
where dotnet >nul 2>&1
if errorlevel 1 (
    echo [FAILED] .NET SDK not found. Install .NET SDK 10 from https://dotnet.microsoft.com/download
    goto :end_fail
)

dotnet run --project "!HASHPW_PROJECT!"
if errorlevel 1 (
    echo [FAILED] The tool exited with an error - see messages above.
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
