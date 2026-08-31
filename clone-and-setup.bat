@echo off
rem ===================================================================
rem clone-and-setup.bat - the full first-time entry point (or a full
rem reset): pulls the project from GitHub into a fresh folder next to
rem this script, then runs setup.bat automatically. This file must be
rem saved *outside* any existing copy of the project (e.g. on the
rem Desktop) - it is the starting point, not part of an existing clone.
rem
rem No absolute path is hardcoded - the target folder is relative to
rem wherever this file itself is saved (%~dp0).
rem
rem This window stays open and waits for a key press before closing,
rem whether it finished, cancelled, or failed.
rem ===================================================================

setlocal enabledelayedexpansion

set "REPO_URL=https://github.com/xdma1011/SuperMarket.git"
set "TARGET_DIR=%~dp0SuperMarket"

echo === Full reset and fresh pull from GitHub ===
echo Source:      %REPO_URL%
echo Destination: %TARGET_DIR%
echo.

where git >nul 2>&1
if errorlevel 1 (
    echo [FAILED] git not found on PATH. Install Git for Windows from https://git-scm.com/download/win first.
    goto :end_fail
)

if exist "%TARGET_DIR%" (
    echo [WARNING] The folder "%TARGET_DIR%" already exists.
    set /p CONFIRM_DELETE="Type YES in capital letters to delete it completely and start fresh, anything else to cancel: "
    if not "!CONFIRM_DELETE!"=="YES" (
        echo Cancelled - no changes made.
        goto :end_ok
    )
    echo Deleting "%TARGET_DIR%"...
    rd /s /q "%TARGET_DIR%"
    if exist "%TARGET_DIR%" (
        echo [FAILED] Could not fully delete the folder - make sure no file inside it is open in another program ^(e.g. Visual Studio^) and try again.
        goto :end_fail
    )
)

echo.
echo === git clone ===
git clone "%REPO_URL%" "%TARGET_DIR%"
if errorlevel 1 (
    echo [FAILED] git clone failed - see message above.
    goto :end_fail
)

echo.
echo === Running setup.bat from the fresh clone ===
call "%TARGET_DIR%\setup.bat"
set "SETUP_RESULT=%errorlevel%"

echo.
if "%SETUP_RESULT%"=="0" (
    echo === All done ===
    echo Project is now at: %TARGET_DIR%
    echo Next step: open CMD in this folder and run dotnet ef migrations add InitialCreate ^(see setup.bat instructions above^).
    goto :end_ok
) else (
    echo [FAILED] setup.bat reported an error - see messages above.
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
