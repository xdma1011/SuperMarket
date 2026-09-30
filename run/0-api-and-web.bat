@echo off
rem ===================================================================
rem run\0-api-and-web.bat - the everyday start: opens the API and the
rem admin panel, each in its own window (close a window = stop it).
rem Pass "cashier" to also open the cashier app:  0-api-and-web.bat cashier
rem No absolute path - it just starts the other files in this folder.
rem ===================================================================

title Supermarket - start
echo === Starting the API in a new window ===
start "Supermarket - API" cmd /c call "%~dp01-api.bat"

echo === Waiting a few seconds for the API before starting the admin panel ===
timeout /t 8 /nobreak >nul

echo === Starting the admin panel in a new window ===
start "Supermarket - Admin Web" cmd /c call "%~dp02-admin-web.bat"

if /i "%~1"=="cashier" (
    echo === Starting the cashier app in a new window ===
    start "Supermarket - Cashier" cmd /c call "%~dp03-cashier.bat"
)

echo.
echo Done - the windows above are running. This window closes in 5 seconds.
timeout /t 5 >nul
exit /b 0
