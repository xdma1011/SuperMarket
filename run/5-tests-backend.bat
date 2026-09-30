@echo off
rem ===================================================================
rem run\5-tests-backend.bat - runs the backend integration tests.
rem They need a SQL Server TEST database (name must contain "test" -
rem the tests refuse anything else, so the real database is never
rem touched). On a machine without Docker set it once, e.g.:
rem   setx INTEGRATION_TEST_CONNECTION_STRING "Server=localhost;Database=sprmrkt_integration_tests;Trusted_Connection=True;TrustServerCertificate=True;"
rem No absolute path, no assumed folder/project name - see _find.bat.
rem The window stays open at the end (success or failure).
rem ===================================================================

setlocal enabledelayedexpansion
title Supermarket - Backend Tests
call "%~dp0_find.bat"
if not defined TESTS_PROJECT (
    echo [FAILED] Test project not found ^(no .csproj referencing Microsoft.NET.Test.Sdk^).
    goto :end_fail
)
where dotnet >nul 2>&1
if errorlevel 1 (
    echo [FAILED] .NET SDK not found. Install .NET SDK 10 from https://dotnet.microsoft.com/download
    goto :end_fail
)

if not defined INTEGRATION_TEST_CONNECTION_STRING (
    echo [NOTE] INTEGRATION_TEST_CONNECTION_STRING is not set - the tests will use their default ^(Docker^) database.
    echo        Without Docker, set it to a local TEST database - see the comment at the top of this file.
    echo.
)
echo === Tests: !TESTS_PROJECT! ===
echo.
dotnet test "!TESTS_PROJECT!"
if errorlevel 1 (
    echo.
    echo [FAILED] Some tests failed ^(or the build failed^) - see messages above.
    goto :end_fail
)
echo.
echo [OK] All backend tests passed.
echo.
echo Press any key to close this window . . .
pause >nul
exit /b 0

:end_fail
echo.
echo Press any key to close this window . . .
pause >nul
exit /b 1
