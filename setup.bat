@echo off
rem ═══════════════════════════════════════════════════════════════════
rem setup.bat — إعداد المشروع بعد git clone/git pull، من جذر الـrepo.
rem بلا أي مسار مطلق، بلا افتراض اسم .sln أو اسم أي مشروع - كل شي
rem مكتشَف تلقائيًا وقت التشغيل (راجع scripts\_discover.bat).
rem
rem ما بيسوي: dotnet ef database update (سكربت منفصل update-database.bat
rem عمدًا)، ولا أي إنشاء/حذف Migration.
rem ═══════════════════════════════════════════════════════════════════

setlocal enabledelayedexpansion
cd /d "%~dp0"

echo === 1. اكتشاف بنية المشروع ===
call "%~dp0scripts\_discover.bat"
if errorlevel 1 (
    echo [فشل] تعذّر اكتشاف بنية المشروع - راجع الرسالة فوق.
    exit /b 1
)
echo   .sln:              %SLN_FILE%
echo   مشروع API:          %API_PROJECT%
echo   مشروع DbContext:    %DB_PROJECT%
echo.

echo === 2. التحقق من .NET SDK ===
where dotnet >nul 2>&1
if errorlevel 1 (
    echo [فشل] dotnet CLI مش موجود بالـPATH. نزّل .NET SDK 10 من https://dotnet.microsoft.com/download أولًا.
    exit /b 1
)
set "HAS_SDK10="
for /f "delims=" %%v in ('dotnet --list-sdks 2^>nul') do (
    echo %%v | findstr /b /c:"10." >nul
    if not errorlevel 1 set "HAS_SDK10=1"
)
if not defined HAS_SDK10 (
    echo [تحذير] ما لقيت .NET SDK 10.x مثبَّت. المشروع مبني على net10.0 - المفروض تنزّله.
    echo          الـSDKs المثبَّتة عندك حاليًا:
    dotnet --list-sdks
    echo          كمّل بأي حال، بس متوقَّع فشل بالخطوات الجاية لو فعلًا مش موجود.
)
echo.

echo === 3. dotnet restore ===
dotnet restore "%SLN_FILE%"
if errorlevel 1 (
    echo [فشل] dotnet restore طلع بخطأ - راجع الرسالة فوق.
    exit /b 1
)
echo.

echo === 4. dotnet tool restore (dotnet-ef محلي، من .config\dotnet-tools.json) ===
dotnet tool restore
if errorlevel 1 (
    echo [فشل] dotnet tool restore طلع بخطأ - راجع الرسالة فوق.
    exit /b 1
)
echo.

echo === 5. dotnet build ===
dotnet build "%SLN_FILE%" --no-restore
if errorlevel 1 (
    echo [فشل] dotnet build طلع بخطأ - راجع الرسالة فوق.
    exit /b 1
)
echo.

echo === 6. حالة Migrations ===
dotnet ef migrations list --project "%DB_PROJECT%" --startup-project "%API_PROJECT%" --no-build
if errorlevel 1 (
    echo [تحذير] تعذّر قراءة قائمة الـMigrations - راجع رسالة الخطأ فوق يدويًا.
) else (
    echo.
    echo === 7. فحص "pending model changes" ===
    dotnet ef migrations has-pending-model-changes --project "%DB_PROJECT%" --startup-project "%API_PROJECT%" --no-build
    if errorlevel 1 (
        echo.
        echo [انتبه] فيه فرق بين موديل الكود الحالي وآخر Migration محفوظة.
        echo          هذا سكربت setup.bat ما بيصلحها تلقائيًا عمدًا - لازم Migration
        echo          يدوية جديدة تعكس الفرق. لو أول مرة تشغّل المشروع وصفر
        echo          Migrations موجودة أصلًا، هذا متوقَّع تمامًا: شغّل
        echo            dotnet ef migrations add InitialCreate --project "%DB_PROJECT%" --startup-project "%API_PROJECT%"
        echo          مرة وحدة بس.
    ) else (
        echo   ولا فرق - موديل الكود مطابق تمامًا لآخر Migration.
    )
)

echo.
echo === خلص الإعداد ===
echo التالي: run-api.bat لتشغيل الـAPI، أو update-database.bat لتحديث قاعدة البيانات (يدوي، بعد ما تتأكد الـMigrations سليمة).
exit /b 0
