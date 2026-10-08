@echo off
rem ===================================================================
rem run\_find.bat - internal helper, called with "call" by the other
rem files in this folder. Do not run it by itself.
rem
rem Finds every project by WHAT IT CONTAINS, never by its name or an
rem absolute path, so the repo works from wherever it was cloned:
rem   API_PROJECT      .csproj with Sdk="Microsoft.NET.Sdk.Web"
rem   CASHIER_PROJECT  .csproj with <UseWPF>true
rem   TESTS_PROJECT    .csproj referencing Microsoft.NET.Test.Sdk
rem   CASHIER_TESTS_PROJECT  .csproj with <IsCashierHeadlessTests>true (headless cashier tests)
rem   HASHPW_PROJECT   console .csproj (OutputType Exe) using Identity.Core
rem   ADMINWEB_DIR     folder containing angular.json
rem   CUSTOMER_DIR     folder containing pubspec.yaml (Flutter)
rem Repo root = the folder directly above this "run" folder. Searches
rem the root's sub-folders two levels deep (skips node_modules, bin,
rem obj, .git, build...) - fast, and covers tools\X style projects.
rem ===================================================================

setlocal enabledelayedexpansion

set "REPO_ROOT=%~dp0.."
for %%i in ("%REPO_ROOT%") do set "REPO_ROOT=%%~fi"

set "API_PROJECT="
set "CASHIER_PROJECT="
set "TESTS_PROJECT="
set "CASHIER_TESTS_PROJECT="
set "HASHPW_PROJECT="
set "ADMINWEB_DIR="
set "CUSTOMER_DIR="

for /d %%a in ("%REPO_ROOT%\*") do (
    call :inspect "%%~fa"
    call :should_descend "%%~nxa"
    if not errorlevel 1 (
        for /d %%b in ("%%~fa\*") do call :inspect "%%~fb"
    )
)

rem one line, no parentheses block: a path containing ")" (e.g. "Desktop (2)")
rem would break a ( ... ) block when %VAR% is expanded inside it.
endlocal & set "REPO_ROOT=%REPO_ROOT%" & set "API_PROJECT=%API_PROJECT%" & set "CASHIER_PROJECT=%CASHIER_PROJECT%" & set "TESTS_PROJECT=%TESTS_PROJECT%" & set "CASHIER_TESTS_PROJECT=%CASHIER_TESTS_PROJECT%" & set "HASHPW_PROJECT=%HASHPW_PROJECT%" & set "ADMINWEB_DIR=%ADMINWEB_DIR%" & set "CUSTOMER_DIR=%CUSTOMER_DIR%"
exit /b 0

rem --- folders we never look inside (generated/dependency folders) ---
:should_descend
for %%x in (node_modules bin obj .git .vs .dart_tool build dist .angular .idea) do (
    if /i "%~1"=="%%x" exit /b 1
)
exit /b 0

rem --- check one folder for each project marker ---
:inspect
set "DIR=%~1"
call :should_descend "%~nx1"
if errorlevel 1 exit /b 0

if not defined ADMINWEB_DIR if exist "!DIR!\angular.json" set "ADMINWEB_DIR=!DIR!"
if not defined CUSTOMER_DIR if exist "!DIR!\pubspec.yaml" set "CUSTOMER_DIR=!DIR!"

for %%p in ("!DIR!\*.csproj") do (
    set "PROJ=%%~fp"
    findstr /c:"Microsoft.NET.Sdk.Web" "!PROJ!" >nul 2>&1
    if not errorlevel 1 (
        if not defined API_PROJECT set "API_PROJECT=!PROJ!"
    ) else (
        findstr /i /c:"<UseWPF>true" "!PROJ!" >nul 2>&1
        if not errorlevel 1 if not defined CASHIER_PROJECT set "CASHIER_PROJECT=!PROJ!"
        findstr /c:"Microsoft.NET.Test.Sdk" "!PROJ!" >nul 2>&1
        if not errorlevel 1 (
            findstr /c:"<IsCashierHeadlessTests>true" "!PROJ!" >nul 2>&1
            if not errorlevel 1 (
                if not defined CASHIER_TESTS_PROJECT set "CASHIER_TESTS_PROJECT=!PROJ!"
            ) else (
                if not defined TESTS_PROJECT set "TESTS_PROJECT=!PROJ!"
            )
        )
        findstr /i /c:"<OutputType>Exe" "!PROJ!" >nul 2>&1
        if not errorlevel 1 (
            findstr /c:"Microsoft.Extensions.Identity.Core" "!PROJ!" >nul 2>&1
            if not errorlevel 1 if not defined HASHPW_PROJECT set "HASHPW_PROJECT=!PROJ!"
        )
    )
)
exit /b 0
