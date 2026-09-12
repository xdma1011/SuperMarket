using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;

namespace SupermarketSystem.IntegrationTests;

/// <summary>
/// يشغّل الـAPI فعليًا بالذاكرة (in-process TestServer) — نفس Program.cs
/// ونفس تسجيل DI بالضبط، بس بـconnection string يشاور على قاعدة بيانات
/// اختبار معزولة (SQL Server بحاوية Docker منفصلة، راجع TestDatabaseFixture)
/// لا قاعدة بيانات المستخدم الحقيقية المعتمدة بـappsettings.json (المذكورة
/// بـCLAUDE.md §1.1 — لا تُلمس إطلاقًا).
///
/// الاستبدال عبر ConfigureAppConfiguration (لا تعديل أي appsettings.*.json
/// بالمستودع) — قيمة الإعداد الوحيدة المُستبدلة هون هي الاتصال بقاعدة
/// البيانات، بقية الإعدادات (JWT، إلخ) تُقرأ من appsettings.json العادي.
/// </summary>
public sealed class CustomWebApplicationFactory : WebApplicationFactory<Program>
{
    public string ConnectionString { get; }

    public CustomWebApplicationFactory(string connectionString)
    {
        ConnectionString = connectionString;
    }

    protected override void ConfigureWebHost(IWebHostBuilder builder)
    {
        builder.UseEnvironment("Testing");

        builder.ConfigureAppConfiguration((_, configBuilder) =>
        {
            configBuilder.AddInMemoryCollection(new Dictionary<string, string?>
            {
                ["ConnectionStrings:DefaultConnection"] = ConnectionString
            });
        });

        builder.ConfigureServices(services =>
        {
            // بلا هذا، DailyBackupBackgroundService/PendingReviewEscalationBackgroundService
            // بتشتغل بالخلفية أثناء الاختبارات وتلمس قاعدة بيانات الاختبار
            // بتوقيت غير متوقَّع — نشغّل فحصنا للسلوك المُختبَر صراحة بس،
            // لا خدمات خلفية عشوائية التوقيت.
            services.RemoveAll<Microsoft.Extensions.Hosting.IHostedService>();
        });
    }
}
