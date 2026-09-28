using Microsoft.EntityFrameworkCore;
using SupermarketSystem.Application.Authentication.Login;
using SupermarketSystem.Application.Common.Interfaces;
using SupermarketSystem.Application.Common.Notifications;
using SupermarketSystem.Application.Common.Results;
using SupermarketSystem.Domain.CashManagement;
using SupermarketSystem.Domain.Identity;
using SupermarketSystem.Domain.Notifications;
using SupermarketSystem.Domain.Partners;

namespace SupermarketSystem.Application.Partners;

// =====================================================================================
// سحوبات الشركاء + "مستحق لصاحب المحل".
//   - من الصندوق: CashDrawerLog PayOut - بينقص المتوقع بتقفيل الصندوق.
//   - من جيبي: بلا أثر على الصندوق، وبينسجّل تلقائيًا نفس المبلغ كمستحق للي دفع (المستخدم الحالي).
//   - من الكاشير: الشريك بيكتب يوزره وكلمة سره (حتى لو الكاشير داخل بحسابه) - محاولات فاشلة بتنعدّ بنفس قفل
//     الدخول (UserLoginLog)، فما في تخمين كلمات سر من شاشة الكاشير.
// كل سحب بيوصل كتنبيه "مهم".
// =====================================================================================

public sealed record RecordPartnerWithdrawalCommand(
    Guid PartnerId, decimal Amount, PartnerWithdrawalSource Source, string? Notes, DateTime? OccurredAtUtc, Guid ClientRequestId);

public sealed record RecordVerifiedPartnerWithdrawalCommand(
    Guid BranchId, string Username, string Password, decimal Amount, string? Notes, Guid ClientRequestId);

public sealed record PartnerWithdrawalResponse(
    Guid WithdrawalId, Guid PartnerId, string PartnerName, decimal Amount, int SourceCode, string SourceTitle, decimal NewBalance, bool WasReplay);

public sealed record PartnerWithdrawalDto(
    Guid Id, Guid PartnerId, string PartnerName, decimal Amount, int SourceCode, string SourceTitle, DateTime OccurredAtUtc,
    string? Notes, string RecordedByName, string? PaidByName, bool RecordedAtCashier);

public sealed record GetPartnerWithdrawalsQuery(Guid? BranchId, Guid? PartnerId);

internal sealed class PartnerWithdrawalRecorder
{
    private readonly IApplicationDbContext _context;
    private readonly INotificationDispatcher _notificationDispatcher;
    private readonly IDateTimeProvider _dateTimeProvider;

    public PartnerWithdrawalRecorder(IApplicationDbContext context, INotificationDispatcher notificationDispatcher, IDateTimeProvider dateTimeProvider)
    {
        _context = context;
        _notificationDispatcher = notificationDispatcher;
        _dateTimeProvider = dateTimeProvider;
    }

    public async Task<Result<PartnerWithdrawalResponse>> RecordAsync(
        Partner partner, decimal amount, PartnerWithdrawalSource source, string? notes, DateTime? occurredAtUtc,
        Guid clientRequestId, Guid actorUserId, bool atCashier, CancellationToken cancellationToken)
    {
        var replay = await _context.PartnerWithdrawals.IgnoreQueryFilters().AsNoTracking()
            .FirstOrDefaultAsync(w => w.ClientRequestId == clientRequestId, cancellationToken);
        if (replay is not null)
        {
            var replayBalance = await BalanceAsync(partner, cancellationToken);
            return Result.Success(new PartnerWithdrawalResponse(
                replay.Id, partner.Id, partner.FullName, replay.Amount, (int)replay.Source, PartnerTitles.Source(replay.Source), replayBalance, WasReplay: true));
        }

        if (amount <= 0)
        {
            return Result.Failure<PartnerWithdrawalResponse>(Error.Validation("PartnerWithdrawal.AmountInvalid", "المبلغ لازم يكون أكبر من صفر."));
        }

        if (!Enum.IsDefined(source))
        {
            return Result.Failure<PartnerWithdrawalResponse>(Error.Validation("PartnerWithdrawal.SourceInvalid", "مصدر السحب مش صحيح."));
        }

        if (notes is { Length: > PartnerWithdrawal.MaxNotesLength })
        {
            return Result.Failure<PartnerWithdrawalResponse>(Error.Validation("PartnerWithdrawal.NotesTooLong", "الملاحظة طويلة كتير."));
        }

        if (!partner.IsActive)
        {
            return Result.Failure<PartnerWithdrawalResponse>(Error.BusinessRule("PartnerWithdrawal.PartnerInactive", "الشريك موقوف."));
        }

        var now = _dateTimeProvider.UtcNow;
        var at = occurredAtUtc is { } requested && requested <= now ? DateTime.SpecifyKind(requested, DateTimeKind.Utc) : now;
        amount = Math.Round(amount, 3, MidpointRounding.AwayFromZero);

        var withdrawal = new PartnerWithdrawal(
            partner.BranchId, partner.Id, amount, source, at, notes, actorUserId,
            source == PartnerWithdrawalSource.OwnerPocket ? actorUserId : null, atCashier, clientRequestId);
        _context.PartnerWithdrawals.Add(withdrawal);

        if (source == PartnerWithdrawalSource.Drawer)
        {
            _context.CashDrawerLogs.Add(new CashDrawerLog(
                partner.BranchId, CashDrawerMovementType.PayOut, amount, CashDrawerReferenceType.PartnerWithdrawal, withdrawal.Id, actorUserId, at));
        }
        else
        {
            _context.OwnerReceivableEntries.Add(OwnerReceivableEntry.ForPocketWithdrawal(withdrawal));
        }

        await _context.SaveChangesAsync(cancellationToken);

        var balance = await BalanceAsync(partner, cancellationToken);
        var who = await AlertText.UserNameAsync(_context, actorUserId, cancellationToken);
        var branch = await AlertText.BranchNameAsync(_context, partner.BranchId, cancellationToken);
        await _notificationDispatcher.NotifyAsync(
            $"سحب شريك — {partner.FullName} ({amount:0.000} د.أ)",
            $"{partner.FullName} سحب {amount:0.000} د.أ {PartnerTitles.Source(source)} بفرع {branch}" +
            (atCashier ? $" - من الكاشير (سجّله {who} وتحقق بحساب الشريك)" : $" - سجّله {who}") +
            $".\nرصيده بعد السحب: {balance:0.000} د.أ" + (notes is null ? "" : $"\nملاحظة: {notes}") +
            (source == PartnerWithdrawalSource.OwnerPocket ? $"\nانسجّل {amount:0.000} د.أ مستحق لـ{who}." : ""),
            cancellationToken,
            NotificationSeverity.Warning);

        return Result.Success(new PartnerWithdrawalResponse(
            withdrawal.Id, partner.Id, partner.FullName, amount, (int)source, PartnerTitles.Source(source), balance, WasReplay: false));
    }

    private async Task<decimal> BalanceAsync(Partner partner, CancellationToken cancellationToken)
    {
        var balances = await PartnerLedger.LoadBalancesAsync(_context, new[] { (partner.Id, partner.BranchId, partner.UserId) }, cancellationToken);
        return balances[partner.Id].Current;
    }
}

public sealed class RecordPartnerWithdrawalHandler
{
    private readonly IApplicationDbContext _context;
    private readonly ICurrentUserContext _currentUser;
    private readonly PartnerWithdrawalRecorder _recorder;

    public RecordPartnerWithdrawalHandler(
        IApplicationDbContext context, ICurrentUserContext currentUser, INotificationDispatcher notificationDispatcher, IDateTimeProvider dateTimeProvider)
    {
        _context = context;
        _currentUser = currentUser;
        _recorder = new PartnerWithdrawalRecorder(context, notificationDispatcher, dateTimeProvider);
    }

    public async Task<Result<PartnerWithdrawalResponse>> HandleAsync(RecordPartnerWithdrawalCommand command, CancellationToken cancellationToken)
    {
        if (command.ClientRequestId == Guid.Empty)
        {
            return Result.Failure<PartnerWithdrawalResponse>(Error.Validation("PartnerWithdrawal.ClientRequestIdRequired", "A client request id is required."));
        }

        var partner = await _context.Partners.AsNoTracking().FirstOrDefaultAsync(p => p.Id == command.PartnerId, cancellationToken);
        if (partner is null)
        {
            return Result.Failure<PartnerWithdrawalResponse>(Error.NotFound("Partner.NotFound", "الشريك مش موجود."));
        }

        var actor = _currentUser.UserId ?? throw new InvalidOperationException("لا يمكن تسجيل سحب بلا هوية مستخدم.");
        return await _recorder.RecordAsync(partner, command.Amount, command.Source, command.Notes, command.OccurredAtUtc,
            command.ClientRequestId, actor, atCashier: false, cancellationToken);
    }
}

/// <summary>سحب الشريك من شاشة الكاشير: يوزره وكلمة سره بيثبتوا إنه هو (الكاشير داخل بحسابه هو)، والسحب من الصندوق.</summary>
public sealed class RecordVerifiedPartnerWithdrawalHandler
{
    private readonly IApplicationDbContext _context;
    private readonly ICurrentUserContext _currentUser;
    private readonly IPasswordHasher _passwordHasher;
    private readonly ISettingsProvider _settingsProvider;
    private readonly IDateTimeProvider _dateTimeProvider;
    private readonly PartnerWithdrawalRecorder _recorder;

    private static Error InvalidCredentials() =>
        Error.Forbidden("PartnerWithdrawal.InvalidCredentials", "اسم المستخدم أو كلمة السر غير صحيحة.");

    public RecordVerifiedPartnerWithdrawalHandler(
        IApplicationDbContext context, ICurrentUserContext currentUser, IPasswordHasher passwordHasher,
        ISettingsProvider settingsProvider, INotificationDispatcher notificationDispatcher, IDateTimeProvider dateTimeProvider)
    {
        _context = context;
        _currentUser = currentUser;
        _passwordHasher = passwordHasher;
        _settingsProvider = settingsProvider;
        _dateTimeProvider = dateTimeProvider;
        _recorder = new PartnerWithdrawalRecorder(context, notificationDispatcher, dateTimeProvider);
    }

    public async Task<Result<PartnerWithdrawalResponse>> HandleAsync(RecordVerifiedPartnerWithdrawalCommand command, CancellationToken cancellationToken)
    {
        if (command.ClientRequestId == Guid.Empty || string.IsNullOrWhiteSpace(command.Username) || string.IsNullOrWhiteSpace(command.Password))
        {
            return Result.Failure<PartnerWithdrawalResponse>(InvalidCredentials());
        }

        if (!_currentUser.IsCrossBranchAccessAllowed && _currentUser.BranchId != command.BranchId)
        {
            return Result.Failure<PartnerWithdrawalResponse>(Error.Forbidden("PartnerWithdrawal.BranchNotAllowed", "مش فرعك."));
        }

        var now = _dateTimeProvider.UtcNow;
        var user = await _context.Users.IgnoreQueryFilters().FirstOrDefaultAsync(u => u.Username == command.Username.Trim(), cancellationToken);
        if (user is null || string.IsNullOrWhiteSpace(user.PasswordHash) || !user.IsActive || user.IsDeleted)
        {
            return Result.Failure<PartnerWithdrawalResponse>(InvalidCredentials());
        }

        if (await IsLockedOutAsync(user.Id, now, cancellationToken))
        {
            return Result.Failure<PartnerWithdrawalResponse>(Error.Forbidden(
                "Auth.AccountLocked", "حساب الشريك مقفل مؤقتًا بسبب محاولات فاشلة متكررة - جرّب بعدين."));
        }

        if (_passwordHasher.Verify(command.Password, user.PasswordHash) == PasswordVerificationOutcome.Failed)
        {
            _context.UserLoginLogs.Add(new UserLoginLog(user.Id, command.BranchId, now, false, _currentUser.IpAddress));
            await _context.SaveChangesAsync(cancellationToken);
            return Result.Failure<PartnerWithdrawalResponse>(InvalidCredentials());
        }

        var partner = await _context.Partners.IgnoreQueryFilters().AsNoTracking()
            .FirstOrDefaultAsync(p => p.BranchId == command.BranchId && p.UserId == user.Id && p.IsActive, cancellationToken);
        if (partner is null)
        {
            return Result.Failure<PartnerWithdrawalResponse>(Error.Forbidden(
                "PartnerWithdrawal.NotAPartner", "هالحساب مش مربوط بشريك بهالفرع - الربط من صفحة الشركاء بلوحة الإدارة."));
        }

        var actor = _currentUser.UserId ?? user.Id;
        return await _recorder.RecordAsync(partner, command.Amount, PartnerWithdrawalSource.Drawer, command.Notes, null,
            command.ClientRequestId, actor, atCashier: true, cancellationToken);
    }

    /// <summary>نفس قاعدة قفل الدخول (LoginHandler): آخر N محاولات كلها فاشلة وآخرها ضمن مدة القفل.</summary>
    private async Task<bool> IsLockedOutAsync(Guid userId, DateTime utcNow, CancellationToken cancellationToken)
    {
        var maxAttempts = (int)await _settingsProvider.GetDecimalAsync(AuthSettingsKeys.MaxFailedLoginAttempts, 5m, cancellationToken);
        if (maxAttempts <= 0)
        {
            return false;
        }

        var recent = await _context.UserLoginLogs.AsNoTracking()
            .Where(l => l.UserId == userId)
            .OrderByDescending(l => l.AttemptedAtUtc)
            .Take(maxAttempts)
            .Select(l => new { l.Success, l.AttemptedAtUtc })
            .ToListAsync(cancellationToken);

        if (recent.Count < maxAttempts || recent.Any(a => a.Success))
        {
            return false;
        }

        var lockoutMinutes = await _settingsProvider.GetDecimalAsync(AuthSettingsKeys.LockoutDurationMinutes, 15m, cancellationToken);
        return utcNow < recent[0].AttemptedAtUtc.AddMinutes((double)lockoutMinutes);
    }
}

public sealed class GetPartnerWithdrawalsHandler
{
    private readonly IApplicationDbContext _context;

    public GetPartnerWithdrawalsHandler(IApplicationDbContext context)
    {
        _context = context;
    }

    public async Task<IReadOnlyList<PartnerWithdrawalDto>> HandleAsync(GetPartnerWithdrawalsQuery query, CancellationToken cancellationToken)
    {
        var withdrawals = _context.PartnerWithdrawals.AsNoTracking();
        if (query.BranchId is { } branchId)
        {
            withdrawals = withdrawals.Where(w => w.BranchId == branchId);
        }

        if (query.PartnerId is { } partnerId)
        {
            withdrawals = withdrawals.Where(w => w.PartnerId == partnerId);
        }

        var rows = await withdrawals.OrderByDescending(w => w.OccurredAtUtc).Take(300).ToListAsync(cancellationToken);

        var partnerIds = rows.Select(r => r.PartnerId).Distinct().ToList();
        var partnerNames = await _context.Partners.IgnoreQueryFilters().AsNoTracking()
            .Where(p => partnerIds.Contains(p.Id)).ToDictionaryAsync(p => p.Id, p => p.FullName, cancellationToken);
        var userIds = rows.Select(r => r.RecordedByUserId).Concat(rows.Where(r => r.PaidByUserId != null).Select(r => r.PaidByUserId!.Value)).Distinct().ToList();
        var userNames = await _context.Users.IgnoreQueryFilters().AsNoTracking()
            .Where(u => userIds.Contains(u.Id)).ToDictionaryAsync(u => u.Id, u => u.FullName, cancellationToken);

        return rows.Select(r => new PartnerWithdrawalDto(
            r.Id, r.PartnerId, partnerNames.GetValueOrDefault(r.PartnerId, ""), r.Amount, (int)r.Source, PartnerTitles.Source(r.Source),
            r.OccurredAtUtc, r.Notes, userNames.GetValueOrDefault(r.RecordedByUserId, ""),
            r.PaidByUserId is { } paid ? userNames.GetValueOrDefault(paid) : null, r.RecordedAtCashier)).ToList();
    }
}

// ===== مستحق لصاحب المحل =====

public sealed record OwnerReceivableEntryDto(
    Guid Id, int TypeCode, string TypeTitle, decimal Amount, DateTime OccurredAtUtc, string? SourceTitle, string? Notes);

public sealed record OwnerReceivableDto(
    Guid BranchId, string BranchName, Guid OwnerUserId, string OwnerName, decimal Balance, IReadOnlyList<OwnerReceivableEntryDto> Entries);

public sealed record GetOwnerReceivablesQuery(Guid? BranchId);

public sealed record RecordOwnerRepaymentCommand(
    Guid BranchId, Guid OwnerUserId, decimal Amount, OwnerRepaymentSource Source, string? Notes, Guid ClientRequestId);

public sealed class GetOwnerReceivablesHandler
{
    private readonly IApplicationDbContext _context;

    public GetOwnerReceivablesHandler(IApplicationDbContext context)
    {
        _context = context;
    }

    public async Task<IReadOnlyList<OwnerReceivableDto>> HandleAsync(GetOwnerReceivablesQuery query, CancellationToken cancellationToken)
    {
        var entries = _context.OwnerReceivableEntries.AsNoTracking();
        if (query.BranchId is { } branchId)
        {
            entries = entries.Where(e => e.BranchId == branchId);
        }

        var rows = await entries.OrderByDescending(e => e.OccurredAtUtc).ToListAsync(cancellationToken);
        if (rows.Count == 0)
        {
            return Array.Empty<OwnerReceivableDto>();
        }

        var ownerIds = rows.Select(r => r.OwnerUserId).Distinct().ToList();
        var ownerNames = await _context.Users.IgnoreQueryFilters().AsNoTracking()
            .Where(u => ownerIds.Contains(u.Id)).ToDictionaryAsync(u => u.Id, u => u.FullName, cancellationToken);
        var branchIds = rows.Select(r => r.BranchId).Distinct().ToList();
        var branchNames = await _context.Branches.AsNoTracking()
            .Where(b => branchIds.Contains(b.Id)).ToDictionaryAsync(b => b.Id, b => b.Name, cancellationToken);

        return rows.GroupBy(r => (r.BranchId, r.OwnerUserId))
            .Select(g => new OwnerReceivableDto(
                g.Key.BranchId, branchNames.GetValueOrDefault(g.Key.BranchId, ""), g.Key.OwnerUserId, ownerNames.GetValueOrDefault(g.Key.OwnerUserId, ""),
                g.Sum(e => e.Type == OwnerReceivableEntryType.PaidPartnerFromPocket ? e.Amount : -e.Amount),
                g.Select(e => new OwnerReceivableEntryDto(
                    e.Id, (int)e.Type,
                    e.Type == OwnerReceivableEntryType.PaidPartnerFromPocket ? "دفع لشريك من جيبه" : "استرجاع",
                    e.Amount, e.OccurredAtUtc,
                    e.RepaymentSource switch { OwnerRepaymentSource.Drawer => "من الصندوق", OwnerRepaymentSource.Outside => "برّا الصندوق", _ => null },
                    e.Notes)).ToList()))
            .OrderByDescending(d => d.Balance)
            .ToList();
    }
}

/// <summary>صاحب المحل استرجع مستحقه (فعل يدوي صريح - لا استرجاع تلقائي). ما بيقدر يسترجع أكتر من المستحق.</summary>
public sealed class RecordOwnerRepaymentHandler
{
    private readonly IApplicationDbContext _context;
    private readonly ICurrentUserContext _currentUser;
    private readonly IDateTimeProvider _dateTimeProvider;
    private readonly INotificationDispatcher _notificationDispatcher;

    public RecordOwnerRepaymentHandler(
        IApplicationDbContext context, ICurrentUserContext currentUser, IDateTimeProvider dateTimeProvider, INotificationDispatcher notificationDispatcher)
    {
        _context = context;
        _currentUser = currentUser;
        _dateTimeProvider = dateTimeProvider;
        _notificationDispatcher = notificationDispatcher;
    }

    public async Task<Result<decimal>> HandleAsync(RecordOwnerRepaymentCommand command, CancellationToken cancellationToken)
    {
        if (command.ClientRequestId == Guid.Empty)
        {
            return Result.Failure<decimal>(Error.Validation("OwnerRepayment.ClientRequestIdRequired", "A client request id is required."));
        }

        async Task<decimal> BalanceAsync() => await _context.OwnerReceivableEntries.IgnoreQueryFilters().AsNoTracking()
            .Where(e => e.BranchId == command.BranchId && e.OwnerUserId == command.OwnerUserId)
            .SumAsync(e => e.Type == OwnerReceivableEntryType.PaidPartnerFromPocket ? e.Amount : -e.Amount, cancellationToken);

        if (await _context.OwnerReceivableEntries.IgnoreQueryFilters().AnyAsync(e => e.ClientRequestId == command.ClientRequestId, cancellationToken))
        {
            return Result.Success(await BalanceAsync());
        }

        if (command.Amount <= 0 || !Enum.IsDefined(command.Source))
        {
            return Result.Failure<decimal>(Error.Validation("OwnerRepayment.Invalid", "المبلغ لازم يكون أكبر من صفر."));
        }

        if (!_currentUser.IsCrossBranchAccessAllowed && _currentUser.BranchId != command.BranchId)
        {
            return Result.Failure<decimal>(Error.Forbidden("OwnerRepayment.BranchNotAllowed", "مش فرعك."));
        }

        var amount = Math.Round(command.Amount, 3, MidpointRounding.AwayFromZero);
        var balance = await BalanceAsync();
        if (amount > balance)
        {
            return Result.Failure<decimal>(Error.BusinessRule(
                "OwnerRepayment.ExceedsBalance", $"المستحق {balance:0.000} د.أ بس - ما بينفع تسترجع أكتر منه."));
        }

        var actor = _currentUser.UserId ?? throw new InvalidOperationException("لا يمكن تسجيل استرجاع بلا هوية مستخدم.");
        var now = _dateTimeProvider.UtcNow;
        var entry = OwnerReceivableEntry.Repayment(command.BranchId, command.OwnerUserId, amount, now, command.Source, command.Notes, actor, command.ClientRequestId);
        _context.OwnerReceivableEntries.Add(entry);

        if (command.Source == OwnerRepaymentSource.Drawer)
        {
            _context.CashDrawerLogs.Add(new CashDrawerLog(
                command.BranchId, CashDrawerMovementType.PayOut, amount, CashDrawerReferenceType.OwnerReceivableRepayment, entry.Id, actor, now));
        }

        await _context.SaveChangesAsync(cancellationToken);

        var owner = await AlertText.UserNameAsync(_context, command.OwnerUserId, cancellationToken);
        await _notificationDispatcher.NotifyAsync(
            $"استرجاع مستحق — {owner} ({amount:0.000} د.أ)",
            $"{owner} استرجع {amount:0.000} د.أ من مستحقه ({(command.Source == OwnerRepaymentSource.Drawer ? "من الصندوق" : "برّا الصندوق")}). الباقي إله: {balance - amount:0.000} د.أ.",
            cancellationToken,
            NotificationSeverity.Warning);

        return Result.Success(balance - amount);
    }
}
