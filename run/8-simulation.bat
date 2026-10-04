@echo off
rem ===================================================================
rem run\8-simulation.bat - random full-store simulation test.
rem Random customers buy random items, the manager buys stock, pays
rem suppliers and expenses, salaries, returns, voids, waste, stocktakes,
rem month closes, partners get last month's profit and withdraw random
rem amounts - and an independent calculator checks every report.
rem Each run = a new random scenario. A failed run prints its SEED:
rem type that seed at the third question to replay it exactly.
rem Needs the same TEST database as 5-tests-backend.bat (name must
rem contain "test"), e.g. on a machine without Docker:
rem   setx INTEGRATION_TEST_CONNECTION_STRING "Server=localhost;Database=sprmrkt_integration_tests;Trusted_Connection=True;TrustServerCertificate=True;"
rem Results of every run: run\simulation-results\run-N.txt
rem ===================================================================

setlocal enabledelayedexpansion
chcp 65001 >nul
title Supermarket - Random Simulation
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
    echo [NOTE] INTEGRATION_TEST_CONNECTION_STRING is not set - using the default ^(Docker^) test database.
    echo.
)

set "RUNS="
set /p "RUNS=How many runs (each one a new random scenario)? [5]: "
if "!RUNS!"=="" set "RUNS=5"
set "OPS="
set /p "OPS=Operations in the first month per run (second month = half)? [150]: "
if "!OPS!"=="" set "OPS=150"
set "FIXED="
set /p "FIXED=Replay a specific SEED? (Enter = random): "

set "OUTDIR=%~dp0simulation-results"
if not exist "!OUTDIR!" mkdir "!OUTDIR!"

echo.
echo === Building !TESTS_PROJECT! ===
dotnet build "!TESTS_PROJECT!" -v q -nologo
if errorlevel 1 (
    echo [FAILED] Build failed - see messages above.
    goto :end_fail
)

set /a FAILS=0
for /l %%i in (1,1,!RUNS!) do call :one_run %%i

echo.
echo ===================================================================
if !FAILS! gtr 0 (
    echo [FAILED] !FAILS! of !RUNS! runs found a mismatch. Open the run-N.txt files in
    echo          run\simulation-results - the SEED is at the top, the mismatches at the end.
    goto :end_fail
)
echo [OK] All !RUNS! random runs matched every report.
echo.
echo Press any key to close this window . . .
pause >nul
exit /b 0

:one_run
set "SIM_OPS=!OPS!"
set "SIM_SEED="
if defined FIXED set "SIM_SEED=!FIXED!"
set "RESULT=!OUTDIR!\run-%1.txt"
echo.
echo --- Run %1 of !RUNS! ---
dotnet test "!TESTS_PROJECT!" --no-build --filter "FullyQualifiedName~RandomStoreSimulation" --logger "console;verbosity=detailed" > "!RESULT!" 2>&1
set "CODE=!errorlevel!"
findstr /c:"SIM_SEED=" "!RESULT!" | findstr /v /c:"لإعادة" > "!RESULT!.seed"
for /f "usebackq delims=" %%s in ("!RESULT!.seed") do (
    echo    %%s
    goto :seed_done
)
:seed_done
del "!RESULT!.seed" >nul 2>&1
if not "!CODE!"=="0" (
    set /a FAILS+=1
    echo    [FAILED] mismatch - details in run\simulation-results\run-%1.txt
) else (
    echo    [OK]
)
exit /b 0

:end_fail
echo.
echo Press any key to close this window . . .
pause >nul
exit /b 1
