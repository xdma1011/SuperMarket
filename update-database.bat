@echo off
rem ═══════════════════════════════════════════════════════════════════
rem update-database.bat — تشغيل يدوي فقط (لا setup.bat ولا run-api.bat
rem بيستدعوه تلقائيًا). بيتصل فعليًا بقاعدة البيانات المحدَّدة بـ
rem DefaultConnection بملف appsettings.json الخاص بمشروع الـAPI المكتشَف،
rem ويطبّق كل الـMigrations غير المطبَّقة بعد. تأكيد صريح مطلوب قبل التنفيذ.
rem ═══════════════════════════════════════════════════════════════════

setlocal enabledelayedexpansion
cd /d "%~dp0"

call "%~dp0scripts\_discover.bat"
if errorlevel 1 (
    echo [فشل] تعذّر اكتشاف بنية المشروع - راجع الرسالة فوق.
    exit /b 1
)

echo === فحص الـMigrations أولًا (بلا أي اتصال بقاعدة البيانات) ===
dotnet ef migrations has-pending-model-changes --project "%DB_PROJECT%" --startup-project "%API_PROJECT%"
if errorlevel 1 (
    echo.
    echo [توقف] فيه فرق بين موديل الكود وآخر Migration محفوظة ^(pending model changes^).
    echo         لازم تضيف Migration جديدة تعكس الفرق قبل ما تكمل - راجع setup.bat.
    echo         ما رح أشغّل database update وموديل الكود مش متطابق مع آخر Migration.
    exit /b 1
)

echo   تمام، موديل الكود مطابق لآخر Migration.
echo.

set "APPSETTINGS=%API_PROJECT_DIR%appsettings.json"
echo === تحذير ===
echo هاي العملية رح تتصل فعليًا بقاعدة البيانات المحدَّدة بـ DefaultConnection
echo بملف: %APPSETTINGS%
echo وتطبّق عليها كل الـMigrations غير المطبَّقة. تأكد إنها القاعدة الصحيحة
echo قبل ما تكمل.
echo.
set /p CONFIRM="اكتب YES بالحروف الكبيرة للمتابعة، أي شي تاني للإلغاء: "
if not "%CONFIRM%"=="YES" (
    echo تم الإلغاء - صفر تغيير.
    exit /b 0
)

echo.
echo === dotnet ef database update ===
dotnet ef database update --project "%DB_PROJECT%" --startup-project "%API_PROJECT%"
exit /b %errorlevel%
