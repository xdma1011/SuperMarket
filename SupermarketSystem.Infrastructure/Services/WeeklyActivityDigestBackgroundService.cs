using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using SupermarketSystem.Application.Common.Interfaces;
using SupermarketSystem.Application.Reporting.GetWeeklyActivityDigest;

namespace SupermarketSystem.Infrastructure.Services;

/// <summary>
/// نفس نمط DailyBackupBackgroundService/PendingReviewEscalationBackgroundService
/// بالضبط — BackgroundService مدمج بلا مكتبة جدولة خارجية، بس هون كل 7
/// أيام بدل كل 24 ساعة. أول تشغيل فورًا عند بدء التطبيق (نفس تبرير
/// DailyBackupBackgroundService: ما في داعي ننتظر أسبوع كامل أول مرة)،
/// وبعدها كل أسبوع بالضبط. نفس القيد الموثَّق هناك: لو السيرفر كان مطفي
/// وقت الموعد المتوقع، تلك النسخة "تُفوَّت" بلا تعويض تلقائي.
///
/// الهدف: رسالة تلغرام واحدة أسبوعيًا تلخّص أنشطة حسّاسة (إلغاء بيع،
/// إرجاع، حركة مخزون يدوية/ضيافة) بدل الاعتماد حصرًا على تنبيه فوري لكل
/// عملية لحالها — راجع GetWeeklyActivityDigestHandler لتفاصيل الاستعلام
/// ولسبب استخدام IgnoreQueryFilters هناك (خدمة خلفية بلا HttpContext).
/// </summary>
public sealed class WeeklyActivityDigestBackgroundService : BackgroundService
{
    private static readonly TimeSpan Interval = TimeSpan.FromDays(7);

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
        using var timer = new PeriodicTimer(Interval);

        do
        {
            await RunDigestAsync(stoppingToken);
        }
        while (!stoppingToken.IsCancellationRequested && await timer.WaitForNextTickAsync(stoppingToken));
    }

    private async Task RunDigestAsync(CancellationToken cancellationToken)
    {
        try
        {
            using var scope = _scopeFactory.CreateScope();
            var handler = scope.ServiceProvider.GetRequiredService<GetWeeklyActivityDigestHandler>();
            var notificationDispatcher = scope.ServiceProvider.GetRequiredService<INotificationDispatcher>();
            var dateTimeProvider = scope.ServiceProvider.GetRequiredService<IDateTimeProvider>();

            var sinceUtc = dateTimeProvider.UtcNow.AddDays(-7);

            var digest = await handler.HandleAsync(new GetWeeklyActivityDigestQuery(sinceUtc), cancellationToken);

            var body = string.Join(
                "\n",
                $"- إلغاء بيع: {digest.VoidedSalesCount} عملية بقيمة إجمالية {digest.VoidedSalesTotalAmount:0.##}",
                $"- إرجاع: {digest.ReturnsCount} عملية بقيمة إجمالية {digest.ReturnsTotalAmount:0.##}",
                $"- حركات مخزون يدوية (ضيافة/تعديل يدوي): {digest.ManualStockMovementsCount} عملية بكمية إجمالية {digest.ManualStockMovementsTotalQuantity:0.##}");

            await notificationDispatcher.NotifyAsync(
                "الملخّص الأسبوعي لأنشطة النظام",
                body,
                cancellationToken);

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
