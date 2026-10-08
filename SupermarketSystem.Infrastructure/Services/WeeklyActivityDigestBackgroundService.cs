using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using SupermarketSystem.Domain.Notifications;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using SupermarketSystem.Application.Common.Interfaces;
using SupermarketSystem.Application.Reporting.GetWeeklyActivityDigest;

namespace SupermarketSystem.Infrastructure.Services;

/// <summary>
/// نفس نمط DailyBackupBackgroundService/PendingReviewEscalationBackgroundService
/// بالضبط — BackgroundService مدمج بلا مكتبة جدولة خارجية. بتفحص عند الإقلاع وكل 6 ساعات، وبترسل بس لو آخر
/// ملخّص محفوظ بجدول التنبيهات أقدم من 7 أيام (أو ما في) - فإعادة تشغيل الـAPI ما بتبعت ملخّص جديد، والسيرفر
/// المطفي وقت الموعد بيبعته أول ما يرجع (8/10/2026؛ قبل كان بيبعت مع كل تشغيل).
///
/// الهدف: رسالة تلغرام واحدة أسبوعيًا تلخّص أنشطة حسّاسة (إلغاء بيع،
/// إرجاع، حركة مخزون يدوية/ضيافة) بدل الاعتماد حصرًا على تنبيه فوري لكل
/// عملية لحالها — راجع GetWeeklyActivityDigestHandler لتفاصيل الاستعلام
/// ولسبب استخدام IgnoreQueryFilters هناك (خدمة خلفية بلا HttpContext).
/// </summary>
public sealed class WeeklyActivityDigestBackgroundService : BackgroundService
{
    private static readonly TimeSpan Interval = TimeSpan.FromDays(7);

    // الفحص كل 6 ساعات، والإرسال بس لو آخر ملخّص (بجدول التنبيهات) أقدم من أسبوع. قبل (انمسك بالتست 8/10/2026): الملخّص كان
    // بينبعت مع كل تشغيل للـAPI (PeriodicTimer بيبلّش بإرسال فوري) - كل إعادة تشغيل = ملخّص "أسبوعي" جديد عالتلغرام.
    private static readonly TimeSpan CheckInterval = TimeSpan.FromHours(6);

    public const string DigestTitle = "الملخّص الأسبوعي لأنشطة النظام";

    private readonly IServiceScopeFactory _scopeFactory;
    private readonly ILogger<WeeklyActivityDigestBackgroundService> _logger;

    public WeeklyActivityDigestBackgroundService(
        IServiceScopeFactory scopeFactory, ILogger<WeeklyActivityDigestBackgroundService> logger)
    {
        _scopeFactory = scopeFactory;
        _logger = logger;
    }

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        using var timer = new PeriodicTimer(CheckInterval);

        do
        {
            await RunOnceAsync(stoppingToken);
        }
        while (!stoppingToken.IsCancellationRequested && await timer.WaitForNextTickAsync(stoppingToken));
    }

    /// <summary>دورة وحدة: بترسل الملخّص بس لو ما انبعت ملخّص خلال آخر 7 أيام (من جدول التنبيهات). public للاختبار.</summary>
    public async Task RunOnceAsync(CancellationToken cancellationToken)
    {
        try
        {
            using var scope = _scopeFactory.CreateScope();
            var db = scope.ServiceProvider.GetRequiredService<IApplicationDbContext>();
            var lastSentUtc = await db.Notifications.IgnoreQueryFilters().AsNoTracking()
                .Where(n => n.Title == DigestTitle)
                .MaxAsync(n => (DateTime?)n.CreatedAtUtc, cancellationToken);
            if (lastSentUtc is { } last && scope.ServiceProvider.GetRequiredService<IDateTimeProvider>().UtcNow - last < Interval)
            {
                return;
            }

            var handler = scope.ServiceProvider.GetRequiredService<GetWeeklyActivityDigestHandler>();
            var notificationDispatcher = scope.ServiceProvider.GetRequiredService<INotificationDispatcher>();
            var dateTimeProvider = scope.ServiceProvider.GetRequiredService<IDateTimeProvider>();

            var sinceUtc = dateTimeProvider.UtcNow.AddDays(-7);

            var digest = await handler.HandleAsync(new GetWeeklyActivityDigestQuery(sinceUtc), cancellationToken);

            var body = string.Join(
                "\n",
                $"- إلغاء بيع: {digest.VoidedSalesCount} عملية بقيمة إجمالية {digest.VoidedSalesTotalAmount:0.000}",
                $"- إرجاع: {digest.ReturnsCount} عملية بقيمة إجمالية {digest.ReturnsTotalAmount:0.000}",
                $"- حركات مخزون يدوية (ضيافة/تعديل يدوي): {digest.ManualStockMovementsCount} عملية بكمية إجمالية {digest.ManualStockMovementsTotalQuantity:0.##}");

            await notificationDispatcher.NotifyAsync(
                DigestTitle,
                body,
                cancellationToken,
                NotificationSeverity.Info,
                link: "/reports");

            _logger.LogInformation(
                "ملخّص أسبوعي أُرسل: {VoidCount} إلغاء ({VoidAmount})، {ReturnCount} إرجاع ({ReturnAmount})، {ManualCount} حركة يدوية ({ManualQty}).",
                digest.VoidedSalesCount, digest.VoidedSalesTotalAmount,
                digest.ReturnsCount, digest.ReturnsTotalAmount,
                digest.ManualStockMovementsCount, digest.ManualStockMovementsTotalQuantity);
        }
        catch (Exception ex)
        {
            // نفس مبدأ باقي الخدمات الخلفية بالمشروع — استثناء هون ما لازم
            // يوقف الخدمة كليًا، نسجّله ونحاول تاني بالدورة الأسبوعية الجاية.
            _logger.LogError(ex, "استثناء غير متوقع أثناء إعداد/إرسال الملخّص الأسبوعي.");
        }
    }
}
