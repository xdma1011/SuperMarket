@echo off
rem ===================================================================
rem run-api.bat - runs the auto-discovered API project. No absolute
rem path, no assumed project name. Tries to print the expected Swagger
rem URL from launchSettings.json before starting - the certain URL is
rem the one printed to the console under "Now listening on:"
rem once it is actually running.
rem ===================================================================

setlocal enabledelayedexpansion
cd /d "%~dp0"

call "%~dp0scripts\_discover.bat"
if errorlevel 1 (
    echo [FAILED] Could not discover project structure - see message above.
    exit /b 1
)

echo === Running: %API_PROJECT% ===

set "LAUNCH_SETTINGS=%API_PROJECT_DIR%Properties\launchSettings.json"
set "RAW_URLS="
if exist "%LAUNCH_SETTINGS%" (
    rem We don't split the line by ":" - the URLs themselves contain
    rem ":" (https://...) which breaks any colon-based split. Instead:
    rem grab the whole line and strip the fixed JSON keys via text
    rem substitution (not tokenizing) until only the URL values remain.
    for /f "usebackq delims=" %%L in (`findstr /c:"applicationUrl" "%LAUNCH_SETTINGS%"`) do set "RAW_URLS=%%L"
    if defined RAW_URLS (
        set "RAW_URLS=!RAW_URLS:"applicationUrl": "=!"
        set "RAW_URLS=!RAW_URLS:",=!"
        set "RAW_URLS=!RAW_URLS: =!"
        for %%u in (!RAW_URLS:;= !) do (
            echo   Expected Swagger URL: %%u/swagger
        )
    ) else (
        echo   [NOTE] Could not find applicationUrl in launchSettings.json - the real URL will show below.
    )
) else (
    echo   [NOTE] launchSettings.json not found for this project - .NET will pick a random port.
    echo          Watch for the "Now listening on: https://..." line below and append /swagger to it.
)

echo.
echo === The real, certain URL will appear below under "Now listening on:" ===
echo === Press Ctrl+C to stop the server ===
echo.

dotnet run --project "%API_PROJECT%"
exit /b %errorlevel%
