@echo off
rem ===================================================================
rem update-database.bat - manual run only (not called automatically by
rem setup.bat or run-api.bat). This actually connects to the database
rem specified by DefaultConnection in the discovered API project's
rem appsettings.json, and applies every migration not yet applied.
rem Explicit confirmation is required before it runs.
rem
rem This window stays open and waits for a key press before closing,
rem whether it finished, cancelled, or failed.
rem ===================================================================

setlocal enabledelayedexpansion
cd /d "%~dp0"

call "%~dp0scripts\_discover.bat"
if errorlevel 1 (
    echo [FAILED] Could not discover project structure - see message above.
    goto :end_fail
)

echo === Checking migrations first (no database connection yet) ===
dotnet ef migrations has-pending-model-changes --project "%DB_PROJECT%" --startup-project "%API_PROJECT%"
if errorlevel 1 (
    echo.
    echo [STOPPED] There is a difference between the code model and the last saved migration
    echo           ^(pending model changes^).
    echo           Add a new migration to capture the difference before continuing - see setup.bat.
    echo           Refusing to run database update while the code model does not match the last migration.
    goto :end_fail
)

echo   OK, code model matches the last migration.
echo.

set "APPSETTINGS=%API_PROJECT_DIR%appsettings.json"
set "FACTORY_FILE=%DB_PROJECT_DIR%Persistence\AppDbContextFactory.cs"

echo === Connection string ===
where powershell >nul 2>&1
if errorlevel 1 (
    echo   [NOTE] PowerShell not found on PATH - skipping the replace-connection-string step.
    echo          Edit ConnectionStrings:DefaultConnection in %APPSETTINGS% by hand if needed.
) else (
    echo   Current value in %APPSETTINGS%:
    powershell -NoProfile -Command "try { (Get-Content -Raw '%APPSETTINGS%' | ConvertFrom-Json).ConnectionStrings.DefaultConnection } catch { Write-Output '(could not read it)' }"
    echo.
    set "NEW_CONNSTR="
    set /p NEW_CONNSTR="Enter a NEW connection string to replace it everywhere, or press Enter to keep the current one: "
    if not "!NEW_CONNSTR!"=="" (
        echo.
        echo   New value will be:
        echo     !NEW_CONNSTR!
        echo   This will overwrite DefaultConnection in BOTH:
        echo     %APPSETTINGS%
        echo     %FACTORY_FILE%
        echo.
        set "CONFIRM_CONNSTR="
        set /p CONFIRM_CONNSTR="Type YES in capital letters to apply this replacement, anything else to skip it: "
        if "!CONFIRM_CONNSTR!"=="YES" (
            set "CONNSTR_VALUE_FILE=%TEMP%\_setupdb_connstr_%RANDOM%.txt"
            > "!CONNSTR_VALUE_FILE!" (echo|set /p="!NEW_CONNSTR!")
            powershell -NoProfile -ExecutionPolicy Bypass -File "%~dp0scripts\set-connection-string.ps1" -AppSettingsPath "%APPSETTINGS%" -FactoryPath "%FACTORY_FILE%" -ValueFile "!CONNSTR_VALUE_FILE!"
            set "CONNSTR_RESULT=!errorlevel!"
            del "!CONNSTR_VALUE_FILE!" >nul 2>&1
            if not "!CONNSTR_RESULT!"=="0" (
                echo [FAILED] Could not update the connection string - see message above.
                goto :end_fail
            )
        ) else (
            echo   Skipped - connection string left unchanged.
        )
    )
)
echo.

echo === WARNING ===
echo This will actually connect to the database defined by DefaultConnection
echo in this file: %APPSETTINGS%
echo and apply every pending migration to it. Make sure this is the right
echo database before continuing.
echo.
set /p CONFIRM="Type YES in capital letters to continue, anything else to cancel: "
if not "%CONFIRM%"=="YES" (
    echo Cancelled - no changes made.
    goto :end_ok
)

echo.
echo === dotnet ef database update ===
dotnet ef database update --project "%DB_PROJECT%" --startup-project "%API_PROJECT%"
if errorlevel 1 (
    echo [FAILED] dotnet ef database update failed - see message above.
    goto :end_fail
)

:end_ok
echo.
echo Press any key to close this window . . .
pause >nul
exit /b 0

:end_fail
echo.
echo Press any key to close this window . . .
pause >nul
exit /b 1
