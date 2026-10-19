using Microsoft.EntityFrameworkCore;
using SupermarketSystem.Application.Authentication.Login;
using SupermarketSystem.Application.Common.Interfaces;
using SupermarketSystem.Application.Common.Notifications;
using SupermarketSystem.Application.Common.Results;
using SupermarketSystem.Domain.Finance;
using SupermarketSystem.Domain.Identity;
using SupermarketSystem.Domain.Notifications;
using SupermarketSystem.Domain.Partners;

namespace SupermarketSystem.Application.Partners;

// =====================================================================================
// إعادة استثمار ربح الشريك برأس ماله (16/10/2026، طلب صاحب المشروع):
//   - مبلغ من رصيد الشريك المتراكم (أنصبة − سحوبات) بيتحوّل لرأس مال إله. بلا حركة نقد (الفلوس أصلًا بالمحل).
//   - رصيده بينقص، ورأس ماله بيزيد (CapitalTransaction Deposit)، فنسبته من أرباح الشهور الجاية بتزيد - والنسبة بتنحسب
//     برأس المال آخر الشهر، يعني الإعادة بنص الشهر بتأثر على كل ربح هالشهر. الأنسب تتعمل بعد نزول كشف الشهر.
//   - ما بتتنفذ بكبسة غلط: Partners.Manage (Master Admin) + إعادة إدخال كلمة سر المستخدم الحالي (المحاولات الغلط بتنعدّ بنفس قفل
//     الدخول) + شاشة مراجعة بالنسبة قبل/بعد والواجهة بتطلب كتابة اسم الشريك + مفتاح ClientRequestId (ضغطتين = مرة وحدة).
//   - شريك رأس مال فعّال بس (المضارب ما إله رأس مال)، والمبلغ ما بيتجاوز رصيده الموجب (اتساق بيانات: ما بنحوّل ربح مش موجود).
// =====================================================================================

public sealed record ReinvestPartnerProfitCommand(Guid PartnerId, decimal Amount, string? Password, string? Notes, Guid ClientRequestId);

public sealed record GetReinvestPreviewQuery(Guid PartnerId, decimal Amount);

/// <summary>أرقام الشاشة الأخيرة قبل التنفيذ: الرصيد ورأس المال والنسبة من رأس مال شركاء الفرع، قبل وبعد.</summary>
public sealed record ReinvestPreviewDto(
    Guid PartnerId, string PartnerName, decimal Amount, decimal BalanceBefore, decimal BalanceAfter,
    decimal CapitalBefore, decimal CapitalAfter, decimal SharePercentBefore, decimal SharePercentAfter,
    bool CanReinvest, string? BlockedReason);

public sealed record ReinvestPartnerProfitResponse(
    Guid ReinvestmentId, Guid PartnerId, string PartnerName, decimal Amount, decimal NewBalance,
    decimal CapitalBefore, decimal CapitalAfter, decimal SharePercentBefore, decimal SharePercentAfter, bool WasReplay);

/// <summary>قفل صف الشريك لحد نهاية المعاملة - عشان طلبين متزامنين ما يعيدوا استثمار نفس الرصيد مرتين (الرصيد محسوب مش مخزَّن).</summary>
public interface IPartnerLock
{
    Task LockAsync(Guid partnerId, CancellationToken cancellationToken);
}

internal static class PartnerReinvestmentMath
{
    public static decimal Percent(decimal capital, decimal totalCapital) =>
        totalCapital <= 0 ? 0m : Math.Round(capital * 100m / totalCapital, 2, MidpointRounding.AwayFromZero);

    public sealed record State(
        Partner Partner, decimal Balance, decimal CapitalBefore, decimal CapitalAfter, decimal PercentBefore, decimal PercentAfter);

    public static async Task<State> LoadAsync(IApplicationDbContext context, Partner partner, decimal amount, CancellationToken cancellationToken)
    {
        var balances = await PartnerLedger.LoadBalancesAsync(context, new[] { (partner.Id, partner.BranchId, partner.UserId) }, cancellationToken);

        // نسبة الشريك من رأس مال شركاء رأس المال الفعّالين بنفس الفرع (نفس أساس الكشف الشهري).
        var peers = await context.Partners.IgnoreQueryFilters().AsNoTracking()
            .Where(p => p.BranchId == partner.BranchId && p.IsActive && p.Type == PartnerType.Capital)
            .Select(p => p.Id)
            .ToListAsync(cancellationToken);
        var capitals = await PartnerLedger.LoadCapitalBalancesAsync(context, peers, null, cancellationToken);
        var total = capitals.Where(c => peers.Contains(c.Key)).Sum(c => Math.Max(c.Value, 0m));
        var own = capitals.GetValueOrDefault(partner.Id);

        var ownPositive = Math.Max(own, 0m);
        var totalAfter = total - ownPositive + Math.Max(own + amount, 0m);
        return new State(partner, balances[partner.Id].Current, own, own + amount,
            Percent(ownPositive, total), Percent(Math.Max(own + amount, 0m), totalAfter));
    }
}

public sealed class GetReinvestPreviewHandler
{
    private readonly IApplicationDbContext _context;

    public GetReinvestPreviewHandler(IApplicationDbContext context)
    {
        _context = context;
    }

    public async Task<Result<ReinvestPreviewDto>> HandleAsync(GetReinvestPreviewQuery query, CancellationToken cancellationToken)
    {
        var partner = await _context.Partners.AsNoTracking().FirstOrDefaultAsync(p => p.Id == query.PartnerId, cancellationToken);
        if (partner is null)
        {
            return Result.Failure<ReinvestPreviewDto>(Error.NotFound("Partner.NotFound", "الشريك مش موجود."));
        }

        var amount = Math.Round(query.Amount, 3, MidpointRounding.AwayFromZero);
        var state = await PartnerReinvestmentMath.LoadAsync(_context, partner, amount > 0 ? amount : 0m, cancellationToken);
        var blocked = PartnerReinvestRules.Check(partner, amount, state.Balance);
        return Result.Success(new ReinvestPreviewDto(
            partner.Id, partner.FullName, amount, state.Balance, state.Balance - Math.Max(amount, 0m),
            state.CapitalBefore, state.CapitalAfter, state.PercentBefore, state.PercentAfter,
            blocked is null, blocked?.Message));
    }
}

public static class PartnerReinvestRules
{
    /// <summary>null = مسموح. نفس الفحص بالمعاينة والتنفيذ (التنفيذ بيعيده تحت القفل).</summary>
    public static Error? Check(Partner partner, decimal amount, decimal balance)
    {
        if (partner.Type != PartnerType.Capital)
        {
            return Error.BusinessRule("PartnerReinvest.NotCapitalPartner", "الشريك المضارب ما إله رأس مال - نصيبه نسبة ثابتة، فما في إعادة استثمار.");
        }

        if (!partner.IsActive)
        {
            return Error.BusinessRule("PartnerReinvest.PartnerInactive", "الشريك موقوف.");
        }

        if (amount <= 0)
        {
            return Error.Validation("PartnerReinvest.AmountInvalid", "المبلغ لازم يكون أكبر من صفر.");
        }

        if (balance <= 0)
        {
            return Error.BusinessRule("PartnerReinvest.NoBalance", "ما عند الشريك رصيد موجب لإعادة استثماره.");
        }

        if (amount > balance)
        {
            return Error.BusinessRule("PartnerReinvest.ExceedsBalance",
                $"المبلغ ({amount:0.000} د.أ) أكبر من رصيد الشريك ({balance:0.000} د.أ) - ما بنقدر نحوّل ربح مش موجود.");
        }

        return null;
    }
}

public sealed class ReinvestPartnerProfitHandler
{
    private readonly IApplicationDbContext _context;
    private readonly ICurrentUserContext _currentUser;
    private readonly IPasswordHasher _passwordHasher;
    private readonly ISettingsProvider _settingsProvider;
    private readonly IDateTimeProvider _dateTimeProvider;
    private readonly INotificationDispatcher _notificationDispatcher;
    private readonly ITransactionalExecutor _transactionalExecutor;
    private readonly IPartnerLock _partnerLock;

    public ReinvestPartnerProfitHandler(
        IApplicationDbContext context, ICurrentUserContext currentUser, IPasswordHasher passwordHasher, ISettingsProvider settingsProvider,
        IDateTimeProvider dateTimeProvider, INotificationDispatcher notificationDispatcher, ITransactionalExecutor transactionalExecutor,
        IPartnerLock partnerLock)
    {
        _context = context;
        _currentUser = currentUser;
        _passwordHasher = passwordHasher;
        _settingsProvider = settingsProvider;
        _dateTimeProvider = dateTimeProvider;
        _notificationDispatcher = notificationDispatcher;
        _transactionalExecutor = transactionalExecutor;
        _partnerLock = partnerLock;
    }

    private static Error InvalidPassword() =>
        Error.Forbidden("PartnerReinvest.InvalidPassword", "كلمة السر غير صحيحة - ما انعملت إعادة الاستثمار.");

    public async Task<Result<ReinvestPartnerProfitResponse>> HandleAsync(ReinvestPartnerProfitCommand command, CancellationToken cancellationToken)
    {
        if (command.ClientRequestId == Guid.Empty)
        {
            return Result.Failure<ReinvestPartnerProfitResponse>(
                Error.Validation("PartnerReinvest.ClientRequestIdRequired", "A client request id is required."));
        }

        if (command.Notes is { Length: > PartnerReinvestment.MaxNotesLength })
        {
            return Result.Failure<ReinvestPartnerProfitResponse>(Error.Validation("PartnerReinvest.NotesTooLong", "الملاحظة طويلة كتير."));
        }

        var partner = await _context.Partners.AsNoTracking().FirstOrDefaultAsync(p => p.Id == command.PartnerId, cancellationToken);
        if (partner is null)
        {
            return Result.Failure<ReinvestPartnerProfitResponse>(Error.NotFound("Partner.NotFound", "الشريك مش موجود."));
        }

        // إعادة إرسال نفس الطلب (ضغطتين، انقطاع نت) = نفس النتيجة، بلا تنفيذ ثاني وبلا طلب كلمة سر.
        var replay = await _context.PartnerReinvestments.IgnoreQueryFilters().AsNoTracking()
            .FirstOrDefaultAsync(r => r.ClientRequestId == command.ClientRequestId, cancellationToken);
        if (replay is not null)
        {
            var state0 = await PartnerReinvestmentMath.LoadAsync(_context, partner, 0m, cancellationToken);
            return Result.Success(new ReinvestPartnerProfitResponse(
                replay.Id, partner.Id, partner.FullName, replay.Amount, state0.Balance,
                state0.CapitalBefore - replay.Amount, state0.CapitalBefore, 0m, state0.PercentBefore, WasReplay: true));
        }

        var actorId = _currentUser.UserId ?? throw new InvalidOperationException("لا يمكن إعادة استثمار بلا هوية مستخدم.");
        var amount = Math.Round(command.Amount, 3, MidpointRounding.AwayFromZero);

        // كلمة السر قبل أي فحص رصيد: ما بنكشف أرقام لمين ما أثبت نفسه.
        var passwordError = await VerifyPasswordAsync(actorId, partner.BranchId, command.Password, cancellationToken);
        if (passwordError is not null)
        {
            return Result.Failure<ReinvestPartnerProfitResponse>(passwordError);
        }

        var result = await _transactionalExecutor.ExecuteAsync<ReinvestPartnerProfitResponse>(async ct =>
        {
            await _partnerLock.LockAsync(partner.Id, ct);

            var state = await PartnerReinvestmentMath.LoadAsync(_context, partner, amount > 0 ? amount : 0m, ct);
            var blocked = PartnerReinvestRules.Check(partner, amount, state.Balance);
            if (blocked is not null)
            {
                return Result.Failure<ReinvestPartnerProfitResponse>(blocked);
            }

            var now = _dateTimeProvider.UtcNow;
            var capital = new CapitalTransaction(
                partner.BranchId, CapitalTransactionType.Deposit, amount, now,
                $"إعادة استثمار ربح الشريك {partner.FullName}", actorId, partner.Id);
            var reinvestment = new PartnerReinvestment(
                partner.BranchId, partner.Id, amount, now, command.Notes, actorId, capital.Id, command.ClientRequestId);
            _context.CapitalTransactions.Add(capital);
            _context.PartnerReinvestments.Add(reinvestment);
            await _context.SaveChangesAsync(ct);

            return Result.Success(new ReinvestPartnerProfitResponse(
                reinvestment.Id, partner.Id, partner.FullName, amount, state.Balance - amount,
                state.CapitalBefore, state.CapitalAfter, state.PercentBefore, state.PercentAfter, WasReplay: false));
        }, cancellationToken);

        if (result.IsSuccess)
        {
            var r = result.Value;
            var who = await AlertText.UserNameAsync(_context, actorId, cancellationToken);
            var branch = await AlertText.BranchNameAsync(_context, partner.BranchId, cancellationToken);
            await _notificationDispatcher.NotifyAsync(
                $"إعادة استثمار ربح — {partner.FullName} ({amount:0.000} د.أ)",
                $"{who} حوّل {amount:0.000} د.أ من رصيد {partner.FullName} لرأس ماله بفرع {branch}.\n" +
                $"رأس ماله: {r.CapitalBefore:0.000} ← {r.CapitalAfter:0.000} د.أ، ونسبته من الأرباح: {r.SharePercentBefore:0.##}% ← {r.SharePercentAfter:0.##}%.\n" +
                $"رصيده بعد التحويل: {r.NewBalance:0.000} د.أ" + (command.Notes is null ? "" : $"\nملاحظة: {command.Notes}"),
                cancellationToken,
                NotificationSeverity.Warning,
                link: "/partners");
        }

        return result;
    }

    /// <summary>كلمة سر المستخدم الحالي نفسه (مش أي حساب) - الفشل بيتسجّل بنفس سجل الدخول فيتفعّل نفس القفل.</summary>
    private async Task<Error?> VerifyPasswordAsync(Guid userId, Guid branchId, string? password, CancellationToken cancellationToken)
    {
        if (string.IsNullOrEmpty(password))
        {
            return InvalidPassword();
        }

        var user = await _context.Users.IgnoreQueryFilters().FirstOrDefaultAsync(u => u.Id == userId, cancellationToken);
        if (user is null || string.IsNullOrWhiteSpace(user.PasswordHash) || !user.IsActive || user.IsDeleted)
        {
            return InvalidPassword();
        }

        var now = _dateTimeProvider.UtcNow;
        if (await IsLockedOutAsync(user.Id, now, cancellationToken))
        {
            return Error.Forbidden("Auth.AccountLocked", "الحساب مقفل مؤقتًا بسبب محاولات كلمة سر فاشلة متكررة - جرّب بعدين.");
        }

        if (_passwordHasher.Verify(password, user.PasswordHash) == PasswordVerificationOutcome.Failed)
        {
            _context.UserLoginLogs.Add(new UserLoginLog(user.Id, branchId, now, false, _currentUser.IpAddress));
            await _context.SaveChangesAsync(cancellationToken);
            return InvalidPassword();
        }

        return null;
    }

    private async Task<bool> IsLockedOutAsync(Guid userId, DateTime utcNow, CancellationToken cancellationToken)
    {
        var maxAttempts = (int)await _settingsProvider.GetDecimalAsync(AuthSettingsKeys.MaxFailedLoginAttempts, 5m, cancellationToken);
        if (maxAttempts <= 0)
        {
            return false;
        }

        // نفس قفل الدخول، بس العدّ ضمن نافذة القفل بس: كلمة سر غلط من أيام ما بتتراكم مع غلطة اليوم (تسجيل الدخول الناجح ما بيصير بين
        // عمليات الشاشة عشان يصفّر العدّاد، فبدون النافذة كانت 5 غلطات متفرقة بتقفل صاحب المحل).
        var lockoutMinutes = await _settingsProvider.GetDecimalAsync(AuthSettingsKeys.LockoutDurationMinutes, 15m, cancellationToken);
        var windowStart = utcNow.AddMinutes(-(double)lockoutMinutes);
        var recent = await _context.UserLoginLogs.AsNoTracking()
            .Where(l => l.UserId == userId && l.AttemptedAtUtc > windowStart)
            .OrderByDescending(l => l.AttemptedAtUtc)
            .Take(maxAttempts)
            .Select(l => new { l.Success, l.AttemptedAtUtc })
            .ToListAsync(cancellationToken);

        return recent.Count >= maxAttempts && !recent.Any(a => a.Success);
    }
}
