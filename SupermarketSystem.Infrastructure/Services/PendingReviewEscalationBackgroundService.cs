using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using SupermarketSystem.Application.Common.Interfaces;
using SupermarketSystem.Application.Reviews.GetPendingReviews;

namespace SupermarketSystem.Infrastructure.Services;

public static class PendingReviewSettingsKeys
{
    /// <summary>
    /// عدد الأيام اللي لو ضلّت عملية "بانتظار مراجعة" (إرجاع غير مُراجَع،
    /// أو ضيافة NeedsReview) بدونها، تُحسب "متأخرة" وتُدرج بإشعار التصعيد
    /// التالي. "سماح مع مراجعة" بلا متابعة فعلية = بلا فائدة عملية؛ هذا
    /// التصعيد لا يوقف أي عملية ولا يغيّر حالتها — بس يذكّر.
    /// </summary>
    public const string EscalationThresholdDays = "PendingReview.EscalationThresholdDays";
}

/// <summary>
/// نفس نمط DailyBackupBackgroundService بالضبط — BackgroundService مدمج
/// بلا مكتبة جدولة خارجية، يشتغل مرة عند بدء التطبيق وبعدها كل 24 ساعة.
///
/// يعيد استخدام GetPendingReviewsHandler الموجود أصلًا (نفس المصدر اللي
/// يغذّي شاشة "بانتظار المراجعة" بالويب) بدل تكرار الاستعلام — مصدر حقيقة
/// واحد لتعريف "شو بانتظار مراجعة" بكل النظام.
///
/// تبسيط متعمَّد عن الطلب الأصلي: INotificationDispatcher الموجود بالنظام
/// قناة بث واحدة (نفس تلغرام اللي يستخدمه ProcessReturnCommand لتنبيهات
/// المراجعة الفورية)، بلا آلية توجيه لمستخدمين محددين حسب صلاحياتهم
/// (Stocktake.Approve/Returns.Review) — إضافة هالتوجيه تحتاج نظام إشعارات
/// per-user غير موجود حاليًا بالمشروع. الإشعار هون بيوصل لنفس القناة
/// المستخدَمة لكل تنبيهات المراجعة الحالية، لا لمستخدمين محددين.
/// </summary>
public sealed class PendingReviewEscalationBackgroundService : BackgroundService
{
    private static readonly TimeSpan Interval = TimeSpan.FromHours(24);

    private readonly IServiceScopeFactory _scopeFactory;
    private readonly ILogger<PendingReviewEscalationBackgroundService> _logger;

    public PendingReviewEscalationBackgroundService(
        IServiceScopeFactory scopeFactory, ILogger<PendingReviewEscalationBackgroundService> logger)
    {
        _scopeFactory = scopeFactory;
        _logger = logger;
    }

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        using var timer = new PeriodicTimer(Interval);

        do
        {
            await RunEscalationCheckAsync(stoppingToken);
        }
        while (!stoppingToken.IsCancellationRequested && await timer.WaitForNextTickAsync(stoppingToken));
    }

    private async Task RunEscalationCheckAsync(CancellationToken cancellationToken)
    {
        try
        {
            using var scope = _scopeFactory.CreateScope();
            var pendingReviewsHandler = scope.ServiceProvider.GetRequiredService<GetPendingReviewsHandler>();
            var settingsProvider = scope.ServiceProvider.GetRequiredService<ISettingsProvider>();
            var notificationDispatcher = scope.ServiceProvider.GetRequiredService<INotificationDispatcher>();
            var dateTimeProvider = scope.ServiceProvider.GetRequiredService<IDateTimeProvider>();

            var thresholdDays = await settingsProvider.GetDecimalAsync(
                PendingReviewSettingsKeys.EscalationThresholdDays, defaultValue: 3m, cancellationToken);

            var cutoff = dateTimeProvider.UtcNow.AddDays(-(double)thresholdDays);

            var pending = await pendingReviewsHandler.HandleAsync(cancellationToken);
            var overdue = pending.Items.Where(i => i.OccurredAtUtc < cutoff).ToList();

            if (overdue.Count == 0)
            {
                _logger.LogInformation("فحص تصعيد المراجعات المعلَّقة: صفر عنصر متأخر.");
                return;
            }

            var body = string.Join(
                "\n",
                overdue.Select(i => $"- [{i.TypeTitle}] {i.Title} — {i.OccurredAtUtc:yyyy-MM-dd HH:mm} UTC"));

            await notificationDispatcher.NotifyAsync(
                $"تصعيد: {overdue.Count} عملية بانتظار مراجعة منذ أكثر من {thresholdDays:0} يوم",
                body,
                cancellationToken);

            _logger.LogWarning(
                "تصعيد مراجعات معلَّقة: {Count} عنصر تجاوز {ThresholdDays} يوم بدون مراجعة.",
                overdue.Count, thresholdDays);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "استثناء غير متوقع أثناء فحص تصعيد المراجعات المعلَّقة.");
        }
    }
}
