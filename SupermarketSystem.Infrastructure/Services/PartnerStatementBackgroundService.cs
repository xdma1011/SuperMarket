using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using SupermarketSystem.Application.Common.Interfaces;
using SupermarketSystem.Application.Common.Time;
using SupermarketSystem.Application.Partners;
using SupermarketSystem.Domain.Notifications;
using SupermarketSystem.Infrastructure.Persistence;

namespace SupermarketSystem.Infrastructure.Services;

/// <summary>
/// "ببداية كل شهر بينزل كشف جاهز ومسجَّل" (قرار صاحب المشروع 20/9/2026): كل 6 ساعات بيفحص كل فرع فيه شركاء
/// فعّالين، ولو كشف الشهر الماضي مش موجود بيولّده (IsAutomatic). ما بيكتب فوق كشف موجود أبدًا (ممكن يكون
/// انعاد إصداره يدويًا). فشل (مثلًا ما في رأس مال مسجَّل) = تنبيه مهم مرة باليوم لنفس الفرع/الشهر.
/// context بلا فلتر فرع (PlaceholderCurrentUserContext) - ما في مستخدم بالطلب هون.
/// </summary>
public sealed class PartnerStatementBackgroundService : BackgroundService
{
    private static readonly TimeSpan Interval = TimeSpan.FromHours(6);

    private readonly IServiceScopeFactory _scopeFactory;
    private readonly ILogger<PartnerStatementBackgroundService> _logger;

    public PartnerStatementBackgroundService(IServiceScopeFactory scopeFactory, ILogger<PartnerStatementBackgroundService> logger)
    {
        _scopeFactory = scopeFactory;
        _logger = logger;
    }

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        using var timer = new PeriodicTimer(Interval);
        do
        {
            await RunAsync(stoppingToken);
        }
        while (!stoppingToken.IsCancellationRequested && await timer.WaitForNextTickAsync(stoppingToken));
    }

    public async Task RunAsync(CancellationToken cancellationToken)
    {
        try
        {
            using var scope = _scopeFactory.CreateScope();
            var options = scope.ServiceProvider.GetRequiredService<DbContextOptions<AppDbContext>>();
            var dateTimeProvider = scope.ServiceProvider.GetRequiredService<IDateTimeProvider>();
            var notificationDispatcher = scope.ServiceProvider.GetRequiredService<INotificationDispatcher>();

            await using var db = new AppDbContext(options, new PlaceholderCurrentUserContext());

            var now = dateTimeProvider.UtcNow;
            // "الشهر الماضي" بتوقيت المحل (BusinessTime) - نفس حدود كشف الربح الشهري.
            var (localYear, localMonth) = (await BusinessTime.LoadAsync(db, cancellationToken)).LocalMonth(now);
            var previousMonth = new DateTime(localYear, localMonth, 1).AddMonths(-1);

            var branchIds = await db.Partners.AsNoTracking().Where(p => p.IsActive).Select(p => p.BranchId).Distinct().ToListAsync(cancellationToken);
            foreach (var branchId in branchIds)
            {
                var exists = await db.PartnerMonthlyStatements.AsNoTracking()
                    .AnyAsync(s => s.BranchId == branchId && s.Year == previousMonth.Year && s.Month == previousMonth.Month, cancellationToken);
                if (exists)
                {
                    continue;
                }

                var result = await new PartnerStatementGenerator(db, dateTimeProvider)
                    .GenerateAsync(branchId, previousMonth.Year, previousMonth.Month, isAutomatic: true, cancellationToken);

                if (result.IsSuccess)
                {
                    await PartnerStatementAlerts.NotifyIfUncostedAsync(db, notificationDispatcher, result.Value, cancellationToken);
                    _logger.LogInformation("نزل كشف الشركاء التلقائي لفرع {BranchId} عن {Month}/{Year}.", branchId, previousMonth.Month, previousMonth.Year);
                    continue;
                }

                var title = $"ما نزل كشف الشركاء عن {previousMonth.Month}/{previousMonth.Year}";
                var alreadyNotified = await db.Notifications.AsNoTracking()
                    .AnyAsync(n => n.Title == title && n.CreatedAtUtc > now.AddDays(-1), cancellationToken);
                if (!alreadyNotified)
                {
                    await notificationDispatcher.NotifyAsync(title, result.Error!.Message, cancellationToken, NotificationSeverity.Warning, link: "/partners?tab=statements");
                }

                _logger.LogWarning("كشف الشركاء التلقائي فشل لفرع {BranchId}: {Reason}", branchId, result.Error!.Message);
            }
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "استثناء غير متوقع أثناء توليد كشوف الشركاء التلقائية.");
        }
    }
}
