@echo off
rem ═══════════════════════════════════════════════════════════════════
rem clone-and-setup.bat — نقطة البداية الكاملة لأول مرة (أو لتصفير كامل):
rem يسحب المشروع من GitHub بمجلد جديد نظيف جنب هالسكربت، وبعدين يشغّل
rem setup.bat تلقائيًا. لازم يكون هذا الملف محفوظ *برّا* أي نسخة قديمة
rem من المشروع (مثلًا على سطح المكتب) - هو نقطة البداية، مو جزء من
rem نسخة موجودة أصلًا.
rem
rem بلا أي مسار مطلق مكتوب يدويًا - المجلد الهدف بيتحدد نسبةً لمكان هالملف
rem نفسه (%~dp0)، بغض النظر وين حفظته.
rem ═══════════════════════════════════════════════════════════════════

setlocal enabledelayedexpansion

set "REPO_URL=https://github.com/xdma1011/SuperMarket.git"
set "TARGET_DIR=%~dp0SuperMarket"

echo === تصفير كامل وسحب نسخة جديدة من GitHub ===
echo المصدر: %REPO_URL%
echo الوجهة:  %TARGET_DIR%
echo.

where git >nul 2>&1
if errorlevel 1 (
    echo [فشل] git مش موجود بالـPATH. نزّل Git for Windows من https://git-scm.com/download/win أولًا.
    exit /b 1
)

if exist "%TARGET_DIR%" (
    echo [تحذير] المجلد "%TARGET_DIR%" موجود أصلًا.
    set /p CONFIRM_DELETE="اكتب YES بالحروف الكبيرة لحذفه بالكامل والبدء من جديد، أي شي تاني للإلغاء: "
    if not "!CONFIRM_DELETE!"=="YES" (
        echo تم الإلغاء - صفر تغيير.
        exit /b 0
    )
    echo جاري حذف "%TARGET_DIR%"...
    rd /s /q "%TARGET_DIR%"
    if exist "%TARGET_DIR%" (
        echo [فشل] تعذّر حذف المجلد بالكامل - تأكد ما فيه ملف مفتوح ببرنامج تاني ^(Visual Studio مثلًا^) وحاول تاني.
        exit /b 1
    )
)

echo.
echo === git clone ===
git clone "%REPO_URL%" "%TARGET_DIR%"
if errorlevel 1 (
    echo [فشل] git clone طلع بخطأ - راجع الرسالة فوق.
    exit /b 1
)

echo.
echo === تشغيل setup.bat من داخل النسخة الجديدة ===
call "%TARGET_DIR%\setup.bat"
set "SETUP_RESULT=%errorlevel%"

echo.
if "%SETUP_RESULT%"=="0" (
    echo === خلص كل شي بنجاح ===
    echo المشروع الآن بـ: %TARGET_DIR%
    echo الخطوة الجاية: افتح CMD بهالمجلد وشغّل dotnet ef migrations add InitialCreate ^(راجع تعليمات setup.bat فوق^).
) else (
    echo [فشل] setup.bat طلع بخطأ - راجع الرسائل فوق.
)

exit /b %SETUP_RESULT%
