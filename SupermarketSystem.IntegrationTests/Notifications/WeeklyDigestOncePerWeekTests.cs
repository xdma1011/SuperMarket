using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging.Abstractions;
using SupermarketSystem.Infrastructure.Services;
using Xunit;

namespace SupermarketSystem.IntegrationTests.Notifications;

/// <summary>
/// انمسك بالتست الشامل (8/10/2026): الملخّص "الأسبوعي" كان بينبعت مع كل تشغيل للـAPI (PeriodicTimer بيبلّش بإرسال فوري)،
/// فكل إعادة تشغيل = ملخّص جديد عالتلغرام. هلق بيتبعت بس لو آخر ملخّص بجدول التنبيهات أقدم من 7 أيام.
/// </summary>
[Collection(DatabaseCollection.Name)]
public sealed class WeeklyDigestOncePerWeekTests : IntegrationTestBase
{
    public WeeklyDigestOncePerWeekTests(DatabaseFixture fixture) : base(fixture) { }

    [Fact]
    public async Task إعادة_تشغيل_الـAPI_ما_بتبعت_ملخص_أسبوعي_تاني_بنفس_الأسبوع()
    {
        var scopeFactory = Fixture.Factory.Services.GetRequiredService<IServiceScopeFactory>();

        // تشغيلتين متتاليتين للخدمة (= إعادة تشغيل الـAPI مرتين).
        await new WeeklyActivityDigestBackgroundService(scopeFactory, NullLogger<WeeklyActivityDigestBackgroundService>.Instance)
            .RunOnceAsync(CancellationToken.None);
        await new WeeklyActivityDigestBackgroundService(scopeFactory, NullLogger<WeeklyActivityDigestBackgroundService>.Instance)
            .RunOnceAsync(CancellationToken.None);

        using var scope = CreateScope();
        var db = CreateDbContext(scope);
        var digests = await db.Notifications.IgnoreQueryFilters().AsNoTracking()
            .Where(n => n.Title == WeeklyActivityDigestBackgroundService.DigestTitle)
            .ToListAsync();

        // التنبيه بينحفظ مرة لكل قناة (داخلي + تلغرام لو مفعّل) - المهم إنه من تشغيلة وحدة بس.
        Assert.NotEmpty(digests);
        var sendTimes = digests.Select(n => n.CreatedAtUtc).ToList();
        Assert.True(sendTimes.Max() - sendTimes.Min() < TimeSpan.FromSeconds(2), "انبعت ملخّصين من تشغيلتين");
        Assert.Contains(digests, n => n.Message.Contains("0.000", StringComparison.Ordinal));
    }
}
