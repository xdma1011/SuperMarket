@echo off
rem ===================================================================
rem run\9-cashier-headless-tests.bat - the REAL cashier code (ApiClient,
rem PendingSaleSyncService, CatalogSyncService, TrustedClock, Local\*)
rem with no screens, against the real API on a real port:
rem   - offline: sell online, the API is STOPPED, sell offline (carton,
rem     bundle promotion, a price changed during the outage, credit sale),
rem     the API comes back - every sale arrives exactly once, at the price
rem     the customer paid, at the time it was sold; cash closing matches.
rem   - concurrency: 15-20 cashiers at the same instant (last item in
rem     stock, invoice numbering, same ClientRequestId x10, concurrent
rem     cash closings, same user logging in from 6 devices).
rem Uses its OWN test database: the test database name + "_cashier"
rem (e.g. sprmrkt_integration_tests_cashier), created automatically.
rem Same setting as 5-tests-backend.bat, e.g. without Docker:
rem   setx INTEGRATION_TEST_CONNECTION_STRING "Server=localhost;Database=sprmrkt_integration_tests;Trusted_Connection=True;TrustServerCertificate=True;"
rem Local cashier data goes to %TEMP%\spkt-cashier-headless (deleted
rem after each test) - never the real %LocalAppData% cashier database.
rem ===================================================================

setlocal enabledelayedexpansion
chcp 65001 >nul
title Supermarket - Headless Cashier Tests
call "%~dp0_find.bat"
if not defined CASHIER_TESTS_PROJECT (
    echo [FAILED] Headless cashier test project not found ^(no .csproj with IsCashierHeadlessTests^).
    goto :end_fail
)
where dotnet >nul 2>&1
if errorlevel 1 (
    echo [FAILED] .NET SDK not found. Install .NET SDK 10 from https://dotnet.microsoft.com/download
    goto :end_fail
)
if not defined INTEGRATION_TEST_CONNECTION_STRING (
    echo [NOTE] INTEGRATION_TEST_CONNECTION_STRING is not set - using the default ^(Docker^) test database.
    echo.
)

echo === Headless cashier tests: !CASHIER_TESTS_PROJECT! ===
echo.
dotnet test "!CASHIER_TESTS_PROJECT!" --logger "console;verbosity=normal"
if errorlevel 1 (
    echo.
    echo [FAILED] Some tests failed ^(or the build failed^) - see messages above.
    echo          "Application Control policy has blocked this file" = Windows Smart App Control
    echo          blocked a freshly built DLL - not a test failure; see docs\TEST-REPORT-2026-10-08.md.
    goto :end_fail
)
echo.
echo [OK] All headless cashier tests passed.
echo.
echo Press any key to close this window . . .
pause >nul
exit /b 0

:end_fail
echo.
echo Press any key to close this window . . .
pause >nul
exit /b 1
