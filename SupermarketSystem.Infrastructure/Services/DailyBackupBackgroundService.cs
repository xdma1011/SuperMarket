using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using SupermarketSystem.Application.Backups.TriggerBackup;

namespace SupermarketSystem.Infrastructure.Services;

/// <summary>
/// خدمة خلفية مدمجة بـ.NET (BackgroundService) — بلا أي مكتبة جدولة خارجية
/// (Hangfire/Quartz)، كافية تمامًا لمهمة "مرة كل 24 ساعة".
///
/// نطاق محدود بوضوح، موثَّق لا مخفي: بتشتغل مرة عند بدء تشغيل التطبيق،
/// وبعدها كل 24 ساعة بالضبط. لو السيرفر كان مطفي وقت الموعد المتوقع
/// (تعطّل، إعادة تشغيل)، تلك النسخة "تُفوَّت" ولا تُعوَّض تلقائيًا — نظام
/// جدولة حقيقي (Windows Task Scheduler، أو حتى Hangfire لاحقًا) بيحل هذا
/// لو صار مطلوب فعليًا؛ هذا التصميم مقصود يكون بسيط لهذه المرحلة، لا
/// يدّعي قوة أكتر مما هو عليه.
/// </summary>
public sealed class DailyBackupBackgroundService : BackgroundService
{
    private static readonly TimeSpan Interval = TimeSpan.FromHours(24);

    private readonly IServiceScopeFactory _scopeFactory;
    private readonly ILogger<DailyBackupBackgroundService> _logger;

    public DailyBackupBackgroundService(IServiceScopeFactory scopeFactory, ILogger<DailyBackupBackgroundService> logger)
    {
        _scopeFactory = scopeFactory;
        _logger = logger;
    }

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        using var timer = new PeriodicTimer(Interval);

        // أول تشغيل فورًا عند بدء التطبيق (لا ننتظر 24 ساعة الأولى)، وبعدها
        // كل دورة PeriodicTimer.
        do
        {
            await RunBackupAsync(stoppingToken);
        }
        while (!stoppingToken.IsCancellationRequested && await timer.WaitForNextTickAsync(stoppingToken));
    }

    private async Task RunBackupAsync(CancellationToken cancellationToken)
    {
        try
        {
            // Scope جديد لكل تشغيل — BackgroundService نفسه Singleton، بس
            // AppDbContext وباقي الخدمات Scoped، محتاجين scope منفصل صريح.
            using var scope = _scopeFactory.CreateScope();
            var handler = scope.ServiceProvider.GetRequiredService<TriggerBackupHandler>();

            var result = await handler.HandleAsync(new TriggerBackupCommand(), cancellationToken);

            if (result.IsSuccess)
            {
                _logger.LogInformation(
                    "نسخة احتياطية يومية تلقائية نجحت: {FileName} ({Size} بايت)",
                    result.Value.FileName, result.Value.FileSizeBytes);
            }
            else
            {
                _logger.LogWarning("فشلت النسخة الاحتياطية اليومية التلقائية: {Error}", result.Error?.Message);
            }
        }
        catch (Exception ex)
        {
            // استثناء هون ما لازم يوقف الخدمة الخلفية كليًا — نسجّله
            // ونحاول تاني بالدورة الجاية بعد 24 ساعة.
            _logger.LogError(ex, "استثناء غير متوقع أثناء النسخ الاحتياطي اليومي التلقائي.");
        }
    }
}
