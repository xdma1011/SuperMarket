using Microsoft.EntityFrameworkCore.Design;

namespace SupermarketSystem.CashierApp.Local;

/// <summary>
/// لازمة عشان أداة dotnet ef تقدر تنشئ LocalDbContext وقت التصميم
/// (Add-Migration) - LocalDbContext بلا منشئ بلا معاملات (يحتاج dbPath
/// دائمًا لحظة التشغيل الفعلي)، فبلا هذا المصنع أمر Add-Migration كان
/// رح يفشل فورًا. المسار هون تصميمي بس (لتوليد الـMigration)، ما إله
/// علاقة بمسار local.db الفعلي وقت تشغيل التطبيق.
/// </summary>
public sealed class LocalDbContextFactory : IDesignTimeDbContextFactory<LocalDbContext>
{
    public LocalDbContext CreateDbContext(string[] args)
    {
        return new LocalDbContext("design-time.db");
    }
}
