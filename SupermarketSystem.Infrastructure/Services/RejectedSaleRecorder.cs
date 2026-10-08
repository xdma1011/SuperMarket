using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using SupermarketSystem.Application.Common.Interfaces;
using SupermarketSystem.Application.Common.Notifications;
using SupermarketSystem.Application.Common.Results;
using SupermarketSystem.Application.Sales.CompleteSale;
using SupermarketSystem.Application.Sales.RejectedSales;
using SupermarketSystem.Domain.Notifications;
using SupermarketSystem.Domain.Sales;
using SupermarketSystem.Infrastructure.Persistence;

namespace SupermarketSystem.Infrastructure.Services;

/// <summary>
/// بند 24: تسجيل البيعات المرفوضة نهائيًا. بيكتب بـAppDbContext جديد ومنفصل (بلا فلتر فرع، نفس نمط
/// PartnerStatementBackgroundService) لسببين: (1) معاملة البيع الفاشلة ممكن تكون اتراجعت، فالتسجيل لازم يكون
/// برّاها تمامًا؛ (2) NotificationDispatcher بيحفظ بالـcontext اللي بياخده، فلو أخدنا context الطلب ممكن
/// يحفظ معه حالة نص-معاملة. كله أفضل جهد: أي استثناء بينلقط ويتسجل بالـlog، ورد البيع الأصلي ما بيتأثر أبدًا.
/// </summary>
public sealed class RejectedSaleRecorder : IRejectedSaleRecorder
{
    private static readonly JsonSerializerOptions JsonOptions = new();

    private readonly DbContextOptions<AppDbContext> _options;
    private readonly ICurrentUserContext _currentUser;
    private readonly IDateTimeProvider _dateTimeProvider;
    private readonly IEnumerable<INotificationSender> _senders;
    private readonly ILogger<NotificationDispatcher> _dispatcherLogger;
    private readonly ILogger<RejectedSaleRecorder> _logger;

    public RejectedSaleRecorder(
        DbContextOptions<AppDbContext> options, ICurrentUserContext currentUser, IDateTimeProvider dateTimeProvider,
        IEnumerable<INotificationSender> senders, ILogger<NotificationDispatcher> dispatcherLogger, ILogger<RejectedSaleRecorder> logger)
    {
        _options = options;
        _currentUser = currentUser;
        _dateTimeProvider = dateTimeProvider;
        _senders = senders;
        _dispatcherLogger = dispatcherLogger;
        _logger = logger;
    }

    public async Task RecordRejectionAsync(CompleteSaleCommand command, Error error, CancellationToken cancellationToken)
    {
        try
        {
            if (!RejectedSaleRules.ShouldRecord(error) || command.ClientRequestId == Guid.Empty || command.BranchId == Guid.Empty)
            {
                return;
            }

            await using var db = new AppDbContext(_options, new PlaceholderCurrentUserContext());
            var now = _dateTimeProvider.UtcNow;

            var existing = await db.RejectedSaleAttempts.FirstOrDefaultAsync(a => a.ClientRequestId == command.ClientRequestId, cancellationToken);
            if (existing is not null)
            {
                existing.RegisterRetry(error.Code, error.Message, now);
                await db.SaveChangesAsync(cancellationToken);
                return;
            }

            var attempt = new RejectedSaleAttempt(
                command.BranchId, command.ClientRequestId, _currentUser.UserId, error.Code, error.Message,
                JsonSerializer.Serialize(command, JsonOptions), command.Payments.Sum(p => p.Amount), command.Items.Count, now);
            db.RejectedSaleAttempts.Add(attempt);

            try
            {
                await db.SaveChangesAsync(cancellationToken);
            }
            catch (DbUpdateException)
            {
                // سباق: طلبين بنفس الـClientRequestId سجّلوا بنفس اللحظة - الأول كسب، هاد بيصير إعادة محاولة.
                db.ChangeTracker.Clear();
                var winner = await db.RejectedSaleAttempts.FirstOrDefaultAsync(a => a.ClientRequestId == command.ClientRequestId, cancellationToken);
                if (winner is not null)
                {
                    winner.RegisterRetry(error.Code, error.Message, now);
                    await db.SaveChangesAsync(cancellationToken);
                }

                return;
            }

            // تنبيه خطير عند أول رفض بس (إعادة المحاولة كل دقيقة ما لازم تغرق التنبيهات).
            var branchName = await AlertText.BranchNameAsync(db, command.BranchId, cancellationToken);
            var cashierName = await AlertText.UserNameAsync(db, _currentUser.UserId, cancellationToken);
            var dispatcher = new NotificationDispatcher(db, _senders, _dateTimeProvider, _dispatcherLogger);
            await dispatcher.NotifyAsync(
                "بيعة انرفضت من السيرفر",
                $"الكاشير {cashierName} بفرع {branchName}: بيعة فيها {command.Items.Count} أصناف ومدفوع {attempt.PaidAmountHint:0.000} د.أ انرفضت ({error.Message}). " +
                "المصاري ممكن تكون بالدرج بلا فاتورة وبلا خصم مخزون - راجعها من صفحة \"بيعات مرفوضة\".",
                cancellationToken, NotificationSeverity.Critical, link: "/rejected-sales");
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "فشل تسجيل بيعة مرفوضة (ClientRequestId={ClientRequestId}).", command.ClientRequestId);
        }
    }

    public async Task MarkAcceptedAsync(Guid clientRequestId, CancellationToken cancellationToken)
    {
        try
        {
            await using var db = new AppDbContext(_options, new PlaceholderCurrentUserContext());
            var open = await db.RejectedSaleAttempts
                .FirstOrDefaultAsync(a => a.ClientRequestId == clientRequestId && a.ResolvedAtUtc == null, cancellationToken);
            if (open is null)
            {
                return;
            }

            open.MarkAcceptedLater(_dateTimeProvider.UtcNow);
            await db.SaveChangesAsync(cancellationToken);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "فشل تعليم بيعة مرفوضة كمقبولة لاحقًا (ClientRequestId={ClientRequestId}).", clientRequestId);
        }
    }
}
