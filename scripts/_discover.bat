@echo off
rem ═══════════════════════════════════════════════════════════════════
rem مكتبة داخلية مشتركة — مستدعاة بـ "call" من setup.bat وrun-api.bat
rem وupdate-database.bat. لا تشغّلها لحالها، بترجّع صفر شي مفيد.
rem
rem بتكتشف المشاريع تلقائيًا بلا افتراض أي اسم (لا اسم .sln، لا اسم
rem مشروع API، لا اسم مشروع Infrastructure) وبلا أي مسار مطلق:
rem   SLN_FILE      - ملف .sln الوحيد بجذر الـrepo
rem   API_PROJECT   - .csproj اللي فيه Sdk="Microsoft.NET.Sdk.Web"
rem                   (أول مشروع Web SDK يُلقى، هذا الوصف الأدق لـ"مشروع
rem                   API/Startup" بلا اعتماد على أي اسم مجلد)
rem   API_PROJECT_DIR
rem   DB_PROJECT    - .csproj اللي فيه ملف .cs بصنف يرث من DbContext
rem                   (نفس مشروع الـMigrations بنفس الاتفاقية القياسية
rem                   لـEF Core - مشروع واحد بيحمل الاثنين سوا)
rem   DB_PROJECT_DIR
rem
rem كل المتغيرات دي بتترجع بمسارات مطلقة مبنية من %~dp0 وقت التشغيل، لا
rem أي مسار مكتوب يدويًا - بتشتغل من أي مكان اتعمله clone فيه المشروع.
rem ═══════════════════════════════════════════════════════════════════

setlocal enabledelayedexpansion

rem جذر الـrepo = مجلد فوق مجلد scripts مباشرة (هاي السكربت داخل scripts\)
set "REPO_ROOT=%~dp0.."
for %%i in ("%REPO_ROOT%") do set "REPO_ROOT=%%~fi"

set "SLN_FILE="
set "API_PROJECT="
set "DB_PROJECT="

rem --- اكتشاف ملف .sln (أول واحد بجذر الـrepo فقط، لا بحث متداخل) ---
for %%f in ("%REPO_ROOT%\*.sln") do (
    if not defined SLN_FILE set "SLN_FILE=%%~ff"
)

if not defined SLN_FILE (
    endlocal
    echo [خطأ] ما لقيت أي ملف .sln بجذر الـrepo: %REPO_ROOT%
    exit /b 1
)

rem --- اكتشاف مشروع API: أول .csproj فيه Sdk="Microsoft.NET.Sdk.Web" ---
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
    echo [خطأ] ما لقيت أي مشروع Web SDK ^(Sdk="Microsoft.NET.Sdk.Web"^) - محتاج مشروع API/Startup.
    exit /b 1
)

for %%i in ("!API_PROJECT!") do set "API_PROJECT_DIR=%%~dpi"

rem --- اكتشاف مشروع DbContext: أول .csproj فيه ملف .cs يحتوي ": DbContext" ---
rem مستبعَد صراحة: أي مشروع بيستخدم EntityFrameworkCore.Sqlite - هاي علامة
rem "قاعدة محلية/أوفلاين" (زي تطبيق كاشير WPF)، مش قاعدة السيرفر الرئيسية
rem اللي API بيتعامل معها. فحص بالحزمة المستخدَمة لا باسم المجلد/المشروع.
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
    echo [خطأ] ما لقيت أي مشروع فيه صنف يرث من DbContext.
    exit /b 1
)

for %%i in ("!DB_PROJECT!") do set "DB_PROJECT_DIR=%%~dpi"

rem --- تصدير المتغيرات للسكربت المستدعي (بدون endlocal هون عمدًا) ---
endlocal & (
    set "REPO_ROOT=%REPO_ROOT%"
    set "SLN_FILE=%SLN_FILE%"
    set "API_PROJECT=%API_PROJECT%"
    set "API_PROJECT_DIR=%API_PROJECT_DIR%"
    set "DB_PROJECT=%DB_PROJECT%"
    set "DB_PROJECT_DIR=%DB_PROJECT_DIR%"
)

exit /b 0
