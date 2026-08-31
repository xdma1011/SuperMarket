@echo off
rem ===================================================================
rem Internal shared library - called with "call" from setup.bat,
rem run-api.bat and update-database.bat. Do not run it by itself, it
rem returns nothing useful on its own.
rem
rem Auto-discovers projects without assuming any name (not the .sln
rem name, not the API project name, not the Infrastructure project
rem name) and without any absolute path:
rem   SLN_FILE      - the single .sln file at the repo root
rem   API_PROJECT   - the .csproj with Sdk="Microsoft.NET.Sdk.Web"
rem                   (first Web SDK project found - the most accurate
rem                   description of "the API/Startup project" without
rem                   relying on any folder name)
rem   API_PROJECT_DIR
rem   DB_PROJECT    - the .csproj containing a .cs file with a class
rem                   inheriting from DbContext (same project as the
rem                   migrations, per standard EF Core convention -
rem                   one project holds both)
rem   DB_PROJECT_DIR
rem
rem All these variables are returned as absolute paths built from
rem %~dp0 at run time, never a manually written path - this works from
rem wherever the repo was cloned.
rem ===================================================================

setlocal enabledelayedexpansion

rem repo root = the folder directly above the scripts folder (this
rem script lives inside scripts\)
set "REPO_ROOT=%~dp0.."
for %%i in ("%REPO_ROOT%") do set "REPO_ROOT=%%~fi"

set "SLN_FILE="
set "API_PROJECT="
set "DB_PROJECT="

rem --- discover the .sln file (only the first one at repo root, no recursive search) ---
for %%f in ("%REPO_ROOT%\*.sln") do (
    if not defined SLN_FILE set "SLN_FILE=%%~ff"
)

if not defined SLN_FILE (
    endlocal
    echo [ERROR] No .sln file found at repo root: %REPO_ROOT%
    exit /b 1
)

rem --- discover the API project: first .csproj with Sdk="Microsoft.NET.Sdk.Web" ---
for /f "delims=" %%p in ('dir /s /b "%REPO_ROOT%\*.csproj" 2^>nul ^| findstr /v /i "\\bin\\ \\obj\\"') do (
    if not defined API_PROJECT (
        findstr /c:"Microsoft.NET.Sdk.Web" "%%p" >nul 2>&1
        if not errorlevel 1 (
            set "API_PROJECT=%%p"
        )
    )
)

if not defined API_PROJECT (
    endlocal
    echo [ERROR] No Web SDK project found ^(Sdk="Microsoft.NET.Sdk.Web"^) - need an API/Startup project.
    exit /b 1
)

for %%i in ("!API_PROJECT!") do set "API_PROJECT_DIR=%%~dpi"

rem --- discover the DbContext project: first .csproj with a .cs file containing ": DbContext" ---
rem explicitly excluded: any project using EntityFrameworkCore.Sqlite - that
rem is a marker of a local/offline database (e.g. a WPF cashier app), not
rem the main server database that the API talks to. Checked by package
rem usage, not by folder/project name.
for /f "delims=" %%p in ('dir /s /b "%REPO_ROOT%\*.csproj" 2^>nul ^| findstr /v /i "\\bin\\ \\obj\\"') do (
    if not defined DB_PROJECT (
        findstr /c:"EntityFrameworkCore.Sqlite" "%%p" >nul 2>&1
        if errorlevel 1 (
            for %%d in ("%%p") do set "CANDIDATE_DIR=%%~dpd"
            findstr /s /r /c:": *DbContext" "!CANDIDATE_DIR!*.cs" >nul 2>&1
            if not errorlevel 1 (
                set "DB_PROJECT=%%p"
            )
        )
    )
)

if not defined DB_PROJECT (
    endlocal
    echo [ERROR] No project found containing a class that inherits from DbContext.
    exit /b 1
)

for %%i in ("!DB_PROJECT!") do set "DB_PROJECT_DIR=%%~dpi"

rem --- export the variables to the caller script (no endlocal here on purpose) ---
endlocal & (
    set "REPO_ROOT=%REPO_ROOT%"
    set "SLN_FILE=%SLN_FILE%"
    set "API_PROJECT=%API_PROJECT%"
    set "API_PROJECT_DIR=%API_PROJECT_DIR%"
    set "DB_PROJECT=%DB_PROJECT%"
    set "DB_PROJECT_DIR=%DB_PROJECT_DIR%"
)

exit /b 0
