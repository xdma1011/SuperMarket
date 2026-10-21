using Microsoft.EntityFrameworkCore;
using SupermarketSystem.Application.Common.Interfaces;
using SupermarketSystem.Application.Common.Notifications;
using SupermarketSystem.Application.Common.Results;
using SupermarketSystem.Domain.Finance;
using SupermarketSystem.Domain.Notifications;
using SupermarketSystem.Domain.Partners;

namespace SupermarketSystem.Application.Partners;

// =====================================================================================
// التراجع عن إعادة استثمار (احتياطي - الأصل قرار نهائي): بيرجّع مبلغ الإعادة كامل من رأس مال الشريك لرصيده.
//   - إعادة وحدة = تراجع وحيد (فهرس فريد + قفل الشريك)، وكامل بس (ما في تراجع جزئي).
//   - شرط: رأس مال الشريك الحالي ≥ المبلغ (لو انسحب رأس مال بعدها ما بنقدر نرجّعها كاملة - المرفوض مش مبلغ شك، اتساق بيانات).
//   - الكشوف اللي نزلت قبل التراجع ما بتتغير (انحسبت على رأس المال وقتها)؛ النسبة من الأرباح الجاية بترجع.
//   - نفس حماية الإعادة: Partners.Manage + كلمة سر المستخدم الحالي (قفل الدخول) + ClientRequestId + معاينة بالواجهة + اسم الشريك.
//   - ما بيمسح ولا بيعدّل الإعادة الأصلية (سجل تاريخي): بيضيف PartnerReinvestmentReversal + سحب رأس مال مربوط.
// =====================================================================================

public sealed record PartnerReinvestmentDto(
    Guid Id, Guid PartnerId, decimal Amount, DateTime OccurredAtUtc, string? Notes, string RecordedByName,
    bool IsReversed, DateTime? ReversedAtUtc, string? ReversalReason, string? ReversedByName);

public sealed record ReverseReinvestmentCommand(Guid ReinvestmentId, string? Password, string? Reason, Guid ClientRequestId);

public sealed record ReverseReinvestmentPreviewDto(
    Guid ReinvestmentId, Guid PartnerId, string PartnerName, decimal Amount, decimal BalanceBefore, decimal BalanceAfter,
    decimal CapitalBefore, decimal CapitalAfter, decimal SharePercentBefore, decimal SharePercentAfter,
    bool CanReverse, string? BlockedReason);

public sealed record ReverseReinvestmentResponse(
    Guid ReversalId, Guid ReinvestmentId, Guid PartnerId, string PartnerName, decimal Amount, decimal NewBalance,
    decimal CapitalBefore, decimal CapitalAfter, decimal SharePercentBefore, decimal SharePercentAfter, bool WasReplay);

public static class PartnerReversalRules
{
    /// <summary>null = مسموح. نفس الفحص بالمعاينة والتنفيذ (التنفيذ بيعيده تحت القفل).</summary>
    public static Error? Check(bool alreadyReversed, decimal amount, decimal currentCapital)
    {
        if (alreadyReversed)
        {
            return Error.Conflict("PartnerReinvest.AlreadyReversed", "هاي الإعادة انعمل عليها تراجع قبل.");
        }

        if (currentCapital < amount)
        {
            return Error.BusinessRule("PartnerReinvest.CapitalInsufficient",
                $"رأس مال الشريك الحالي ({currentCapital:0.000} د.أ) أقل من مبلغ الإعادة ({amount:0.000} د.أ) - انسحب من رأس ماله بعدها، فما بنقدر نرجّعها كاملة.");
        }

        return null;
    }
}

public sealed class GetPartnerReinvestmentsHandler
{
    private readonly IApplicationDbContext _context;

    public GetPartnerReinvestmentsHandler(IApplicationDbContext context)
    {
        _context = context;
    }

    /// <summary>null = الشريك مش موجود (أو بفرع مش فرع المستخدم). الأحدث أول.</summary>
    public async Task<IReadOnlyList<PartnerReinvestmentDto>?> HandleAsync(Guid partnerId, CancellationToken cancellationToken)
    {
        if (!await _context.Partners.AsNoTracking().AnyAsync(p => p.Id == partnerId, cancellationToken))
        {
            return null;
        }

        var reinvestments = await _context.PartnerReinvestments.AsNoTracking()
            .Where(r => r.PartnerId == partnerId)
            .OrderByDescending(r => r.OccurredAtUtc)
            .Select(r => new { r.Id, r.PartnerId, r.Amount, r.OccurredAtUtc, r.Notes, r.RecordedByUserId })
            .ToListAsync(cancellationToken);
        var reversals = await _context.PartnerReinvestmentReversals.AsNoTracking()
            .Where(r => r.PartnerId == partnerId)
            .Select(r => new { r.ReinvestmentId, r.OccurredAtUtc, r.Reason, r.RecordedByUserId })
            .ToDictionaryAsync(r => r.ReinvestmentId, cancellationToken);

        var userIds = reinvestments.Select(r => r.RecordedByUserId).Concat(reversals.Values.Select(r => r.RecordedByUserId)).Distinct().ToList();
        var names = await _context.Users.IgnoreQueryFilters().AsNoTracking()
            .Where(u => userIds.Contains(u.Id))
            .Select(u => new { u.Id, u.FullName })
            .ToDictionaryAsync(u => u.Id, u => u.FullName, cancellationToken);

        return reinvestments.Select(r =>
        {
            reversals.TryGetValue(r.Id, out var reversal);
            return new PartnerReinvestmentDto(
                r.Id, r.PartnerId, r.Amount, r.OccurredAtUtc, r.Notes, names.GetValueOrDefault(r.RecordedByUserId, "غير معروف"),
                reversal is not null, reversal?.OccurredAtUtc, reversal?.Reason,
                reversal is null ? null : names.GetValueOrDefault(reversal.RecordedByUserId, "غير معروف"));
        }).ToList();
    }
}

public sealed class GetReverseReinvestmentPreviewHandler
{
    private readonly IApplicationDbContext _context;

    public GetReverseReinvestmentPreviewHandler(IApplicationDbContext context)
    {
        _context = context;
    }

    public async Task<Result<ReverseReinvestmentPreviewDto>> HandleAsync(Guid reinvestmentId, CancellationToken cancellationToken)
    {
        var reinvestment = await _context.PartnerReinvestments.AsNoTracking().FirstOrDefaultAsync(r => r.Id == reinvestmentId, cancellationToken);
        var partner = reinvestment is null
            ? null
            : await _context.Partners.AsNoTracking().FirstOrDefaultAsync(p => p.Id == reinvestment.PartnerId, cancellationToken);
        if (reinvestment is null || partner is null)
        {
            return Result.Failure<ReverseReinvestmentPreviewDto>(Error.NotFound("PartnerReinvest.NotFound", "إعادة الاستثمار مش موجودة."));
        }

        var reversed = await _context.PartnerReinvestmentReversals.AsNoTracking().AnyAsync(r => r.ReinvestmentId == reinvestmentId, cancellationToken);
        var state = await PartnerReinvestmentMath.LoadAsync(_context, partner, -reinvestment.Amount, cancellationToken);
        var blocked = PartnerReversalRules.Check(reversed, reinvestment.Amount, state.CapitalBefore);
        return Result.Success(new ReverseReinvestmentPreviewDto(
            reinvestment.Id, partner.Id, partner.FullName, reinvestment.Amount, state.Balance, state.Balance + reinvestment.Amount,
            state.CapitalBefore, state.CapitalAfter, state.PercentBefore, state.PercentAfter, blocked is null, blocked?.Message));
    }
}

public sealed class ReverseReinvestmentHandler
{
    private readonly IApplicationDbContext _context;
    private readonly ICurrentUserContext _currentUser;
    private readonly PartnerActionPasswordVerifier _passwordVerifier;
    private readonly IDateTimeProvider _dateTimeProvider;
    private readonly INotificationDispatcher _notificationDispatcher;
    private readonly ITransactionalExecutor _transactionalExecutor;
    private readonly IPartnerLock _partnerLock;

    public ReverseReinvestmentHandler(
        IApplicationDbContext context, ICurrentUserContext currentUser, PartnerActionPasswordVerifier passwordVerifier,
        IDateTimeProvider dateTimeProvider, INotificationDispatcher notificationDispatcher, ITransactionalExecutor transactionalExecutor,
        IPartnerLock partnerLock)
    {
        _context = context;
        _currentUser = currentUser;
        _passwordVerifier = passwordVerifier;
        _dateTimeProvider = dateTimeProvider;
        _notificationDispatcher = notificationDispatcher;
        _transactionalExecutor = transactionalExecutor;
        _partnerLock = partnerLock;
    }

    private static Error InvalidPassword() =>
        Error.Forbidden("PartnerReinvest.InvalidPassword", "كلمة السر غير صحيحة - ما انعمل التراجع.");

    public async Task<Result<ReverseReinvestmentResponse>> HandleAsync(ReverseReinvestmentCommand command, CancellationToken cancellationToken)
    {
        if (command.ClientRequestId == Guid.Empty)
        {
            return Result.Failure<ReverseReinvestmentResponse>(
                Error.Validation("PartnerReinvest.ClientRequestIdRequired", "A client request id is required."));
        }

        if (command.Reason is { Length: > PartnerReinvestmentReversal.MaxReasonLength })
        {
            return Result.Failure<ReverseReinvestmentResponse>(Error.Validation("PartnerReinvest.ReasonTooLong", "السبب طويل كتير."));
        }

        var reinvestment = await _context.PartnerReinvestments.AsNoTracking().FirstOrDefaultAsync(r => r.Id == command.ReinvestmentId, cancellationToken);
        var partner = reinvestment is null
            ? null
            : await _context.Partners.AsNoTracking().FirstOrDefaultAsync(p => p.Id == reinvestment.PartnerId, cancellationToken);
        if (reinvestment is null || partner is null)
        {
            return Result.Failure<ReverseReinvestmentResponse>(Error.NotFound("PartnerReinvest.NotFound", "إعادة الاستثمار مش موجودة."));
        }

        // إعادة إرسال نفس الطلب (ضغطتين، انقطاع نت) = نفس النتيجة، بلا تنفيذ ثاني وبلا طلب كلمة سر.
        var replay = await _context.PartnerReinvestmentReversals.IgnoreQueryFilters().AsNoTracking()
            .FirstOrDefaultAsync(r => r.ClientRequestId == command.ClientRequestId, cancellationToken);
        if (replay is not null)
        {
            var current = await PartnerReinvestmentMath.LoadAsync(_context, partner, 0m, cancellationToken);
            return Result.Success(new ReverseReinvestmentResponse(
                replay.Id, replay.ReinvestmentId, partner.Id, partner.FullName, replay.Amount, current.Balance,
                current.CapitalBefore + replay.Amount, current.CapitalBefore, 0m, current.PercentBefore, WasReplay: true));
        }

        var actorId = _currentUser.UserId ?? throw new InvalidOperationException("لا يمكن التراجع بلا هوية مستخدم.");

        // كلمة السر قبل أي فحص رصيد/رأس مال: ما بنكشف أرقام لمين ما أثبت نفسه.
        var passwordError = await _passwordVerifier.VerifyAsync(actorId, partner.BranchId, command.Password, InvalidPassword(), cancellationToken);
        if (passwordError is not null)
        {
            return Result.Failure<ReverseReinvestmentResponse>(passwordError);
        }

        var result = await _transactionalExecutor.ExecuteAsync<ReverseReinvestmentResponse>(async ct =>
        {
            await _partnerLock.LockAsync(partner.Id, ct);

            var reversed = await _context.PartnerReinvestmentReversals.AsNoTracking().AnyAsync(r => r.ReinvestmentId == reinvestment.Id, ct);
            var state = await PartnerReinvestmentMath.LoadAsync(_context, partner, -reinvestment.Amount, ct);
            var blocked = PartnerReversalRules.Check(reversed, reinvestment.Amount, state.CapitalBefore);
            if (blocked is not null)
            {
                return Result.Failure<ReverseReinvestmentResponse>(blocked);
            }

            var now = _dateTimeProvider.UtcNow;
            var capital = new CapitalTransaction(
                partner.BranchId, CapitalTransactionType.Withdrawal, reinvestment.Amount, now,
                $"تراجع عن إعادة استثمار ربح الشريك {partner.FullName}", actorId, partner.Id);
            var reversal = new PartnerReinvestmentReversal(
                partner.BranchId, partner.Id, reinvestment.Id, reinvestment.Amount, now, command.Reason, actorId, capital.Id, command.ClientRequestId);
            _context.CapitalTransactions.Add(capital);
            _context.PartnerReinvestmentReversals.Add(reversal);
            await _context.SaveChangesAsync(ct);

            return Result.Success(new ReverseReinvestmentResponse(
                reversal.Id, reinvestment.Id, partner.Id, partner.FullName, reinvestment.Amount, state.Balance + reinvestment.Amount,
                state.CapitalBefore, state.CapitalAfter, state.PercentBefore, state.PercentAfter, WasReplay: false));
        }, cancellationToken);

        if (result.IsSuccess)
        {
            var r = result.Value;
            var who = await AlertText.UserNameAsync(_context, actorId, cancellationToken);
            var branch = await AlertText.BranchNameAsync(_context, partner.BranchId, cancellationToken);
            await _notificationDispatcher.NotifyAsync(
                $"تراجع عن إعادة استثمار — {partner.FullName} ({r.Amount:0.000} د.أ)",
                $"{who} تراجع عن إعادة استثمار {r.Amount:0.000} د.أ ({reinvestment.OccurredAtUtc:yyyy-MM-dd}) لـ{partner.FullName} بفرع {branch}.\n" +
                $"رأس ماله: {r.CapitalBefore:0.000} ← {r.CapitalAfter:0.000} د.أ، ونسبته من الأرباح: {r.SharePercentBefore:0.##}% ← {r.SharePercentAfter:0.##}%.\n" +
                $"رصيده بعد التراجع: {r.NewBalance:0.000} د.أ" + (command.Reason is null ? "" : $"\nالسبب: {command.Reason}"),
                cancellationToken,
                NotificationSeverity.Warning,
                link: "/partners");
        }

        return result;
    }
}
