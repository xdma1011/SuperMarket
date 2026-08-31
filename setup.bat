@echo off
rem ===================================================================
rem setup.bat - project setup after git clone/git pull, run from repo root.
rem No absolute paths, no assumed .sln or project names - everything is
rem auto-discovered at run time (see scripts\_discover.bat).
rem
rem Does NOT run: dotnet ef database update (separate update-database.bat
rem on purpose), and does NOT create/delete any migration.
rem
rem This window stays open and waits for a key press before closing,
rem whether it finished, warned, or failed - so double-clicking this
rem file never hides the output.
rem ===================================================================

setlocal enabledelayedexpansion
cd /d "%~dp0"

echo === 1. Discovering project structure ===
call "%~dp0scripts\_discover.bat"
if errorlevel 1 (
    echo [FAILED] Could not discover project structure - see message above.
    goto :end_fail
)
echo   .sln file:       %SLN_FILE%
echo   API project:     %API_PROJECT%
echo   DbContext proj:  %DB_PROJECT%
echo.

echo === 2. Checking .NET SDK ===
where dotnet >nul 2>&1
if errorlevel 1 (
    echo [FAILED] dotnet CLI not found on PATH. Install .NET SDK 10 from https://dotnet.microsoft.com/download first.
    goto :end_fail
)
set "HAS_SDK10="
for /f "delims=" %%v in ('dotnet --list-sdks 2^>nul') do (
    echo %%v | findstr /b /c:"10." >nul
    if not errorlevel 1 set "HAS_SDK10=1"
)
if not defined HAS_SDK10 (
    echo [WARNING] .NET SDK 10.x not found. This project targets net10.0 - you should install it.
    echo           SDKs currently installed on this machine:
    dotnet --list-sdks
    echo           Continuing anyway, but the next steps are expected to fail if it is really missing.
)
echo.

echo === 3. dotnet restore ===
dotnet restore "%SLN_FILE%"
if errorlevel 1 (
    echo [FAILED] dotnet restore failed - see message above.
    goto :end_fail
)
echo.

echo === 4. dotnet tool restore (local dotnet-ef, from .config\dotnet-tools.json) ===
dotnet tool restore
if errorlevel 1 (
    echo [FAILED] dotnet tool restore failed - see message above.
    goto :end_fail
)
echo.

echo === 5. dotnet build ===
dotnet build "%SLN_FILE%" --no-restore
if errorlevel 1 (
    echo [FAILED] dotnet build failed - see message above.
    goto :end_fail
)
echo.

echo === 6. Migrations status ===
dotnet ef migrations list --project "%DB_PROJECT%" --startup-project "%API_PROJECT%" --no-build
if errorlevel 1 (
    echo [WARNING] Could not read the migrations list - check the error message above manually.
) else (
    echo.
    echo === 7. Checking for pending model changes ===
    dotnet ef migrations has-pending-model-changes --project "%DB_PROJECT%" --startup-project "%API_PROJECT%" --no-build
    if errorlevel 1 (
        echo.
        echo [NOTE] There is a difference between the current code model and the last saved migration.
        echo        setup.bat deliberately does NOT fix this automatically - a new manual migration
        echo        is needed to capture the difference. If this is the first run and there are zero
        echo        migrations yet, this is completely expected: run this once:
        echo          dotnet ef migrations add InitialCreate --project "%DB_PROJECT%" --startup-project "%API_PROJECT%"
    ) else (
        echo   No difference - code model matches the last migration exactly.
    )
)

echo.
echo === Setup finished ===
echo Next: run-api.bat to start the API, or update-database.bat to update the database (manual, after confirming migrations are correct).
echo.
echo Press any key to close this window . . .
pause >nul
exit /b 0

:end_fail
echo.
echo Press any key to close this window . . .
pause >nul
exit /b 1
