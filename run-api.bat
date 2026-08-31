@echo off
rem ═══════════════════════════════════════════════════════════════════
rem run-api.bat — يشغّل مشروع الـAPI المكتشَف تلقائيًا. بلا مسار مطلق،
rem بلا اسم مشروع مفترَض. يحاول يطلع رابط Swagger المتوقَّع من
rem launchSettings.json قبل التشغيل - الرابط الأكيد 100% هو اللي بيطبع
rem بالكونسول تحت "Now listening on:" بعد ما يشتغل فعليًا.
rem ═══════════════════════════════════════════════════════════════════

setlocal enabledelayedexpansion
cd /d "%~dp0"

call "%~dp0scripts\_discover.bat"
if errorlevel 1 (
    echo [فشل] تعذّر اكتشاف بنية المشروع - راجع الرسالة فوق.
    exit /b 1
)

echo === تشغيل: %API_PROJECT% ===

set "LAUNCH_SETTINGS=%API_PROJECT_DIR%Properties\launchSettings.json"
set "RAW_URLS="
if exist "%LAUNCH_SETTINGS%" (
    rem ما بنستخدم delims=: لتقسيم السطر - الروابط نفسها فيها ":" (https://...)
    rem بتكسر أي تقسيم بالنقطتين. بدالها: نجيب السطر كامل ونشيل منه المفاتيح
    rem الثابتة نصيًا (استبدال، لا تقسيم) لحد ما تضل قيمة الروابط بس.
    for /f "usebackq delims=" %%L in (`findstr /c:"applicationUrl" "%LAUNCH_SETTINGS%"`) do set "RAW_URLS=%%L"
    if defined RAW_URLS (
        set "RAW_URLS=!RAW_URLS:"applicationUrl": "=!"
        set "RAW_URLS=!RAW_URLS:",=!"
        set "RAW_URLS=!RAW_URLS: =!"
        for %%u in (!RAW_URLS:;= !) do (
            echo   Swagger المتوقَّع: %%u/swagger
        )
    ) else (
        echo   [ملاحظة] ما لقيت applicationUrl بـlaunchSettings.json - الرابط الفعلي بيطلع تحت بالكونسول.
    )
) else (
    echo   [ملاحظة] launchSettings.json مش موجود لهالمشروع - .NET بيختار منفذ عشوائي.
    echo             راقب سطر "Now listening on: https://..." تحت، وأضف /swagger على آخره.
)

echo.
echo === الرابط الفعلي الأكيد رح يطلع بالأسفل تحت "Now listening on:" ===
echo === Ctrl+C لإيقاف السيرفر ===
echo.

dotnet run --project "%API_PROJECT%"
exit /b %errorlevel%
