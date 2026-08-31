@echo off
rem ===================================================================
rem update-database.bat - manual run only (not called automatically by
rem setup.bat or run-api.bat). This actually connects to the database
rem specified by DefaultConnection in the discovered API project's
rem appsettings.json, and applies every migration not yet applied.
rem Explicit confirmation is required before it runs.
rem ===================================================================

setlocal enabledelayedexpansion
cd /d "%~dp0"

call "%~dp0scripts\_discover.bat"
if errorlevel 1 (
    echo [FAILED] Could not discover project structure - see message above.
    exit /b 1
)

echo === Checking migrations first (no database connection yet) ===
dotnet ef migrations has-pending-model-changes --project "%DB_PROJECT%" --startup-project "%API_PROJECT%"
if errorlevel 1 (
    echo.
    echo [STOPPED] There is a difference between the code model and the last saved migration
    echo           ^(pending model changes^).
    echo           Add a new migration to capture the difference before continuing - see setup.bat.
    echo           Refusing to run database update while the code model does not match the last migration.
    exit /b 1
)

echo   OK, code model matches the last migration.
echo.

set "APPSETTINGS=%API_PROJECT_DIR%appsettings.json"
echo === WARNING ===
echo This will actually connect to the database defined by DefaultConnection
echo in this file: %APPSETTINGS%
echo and apply every pending migration to it. Make sure this is the right
echo database before continuing.
echo.
set /p CONFIRM="Type YES in capital letters to continue, anything else to cancel: "
if not "%CONFIRM%"=="YES" (
    echo Cancelled - no changes made.
    exit /b 0
)

echo.
echo === dotnet ef database update ===
dotnet ef database update --project "%DB_PROJECT%" --startup-project "%API_PROJECT%"
exit /b %errorlevel%
