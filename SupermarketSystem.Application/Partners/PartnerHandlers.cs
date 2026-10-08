using Microsoft.EntityFrameworkCore;
using SupermarketSystem.Application.Common.Interfaces;
using SupermarketSystem.Application.Common.Notifications;
using SupermarketSystem.Application.Common.Results;
using SupermarketSystem.Domain.Common;
using SupermarketSystem.Domain.Notifications;
using SupermarketSystem.Domain.Partners;

namespace SupermarketSystem.Application.Partners;

// =====================================================================================
// الشركاء: إدارة، أرصدة، كشوف شهرية، وكشف حساب كل شريك. (السحوبات والمستحق لصاحب المحل: PartnerMoneyHandlers.cs)
// =====================================================================================

public sealed record PartnerDto(
    Guid Id, Guid BranchId, string FullName, int TypeCode, string TypeTitle,
    Guid? UserId, string? Username, decimal? SpeculativeProfitPercent, bool IsActive, string? Notes,
    decimal CapitalBalance, decimal SharesTotal, decimal WithdrawalsTotal, decimal AtCostDeductedTotal, decimal CurrentBalance,
    string? TelegramPhone = null, bool TelegramLinked = false, bool HasCashierBarcode = false, DateTime? CashierBarcodeIssuedAtUtc = null);

public sealed record GetPartnersQuery(Guid? BranchId);

public sealed record CreatePartnerCommand(Guid BranchId, string FullName, PartnerType Type, Guid? UserId, decimal? SpeculativeProfitPercent, string? Notes);

public sealed record UpdatePartnerCommand(Guid PartnerId, string FullName, Guid? UserId, decimal? SpeculativeProfitPercent, string? Notes);

public sealed record SetPartnerActiveCommand(Guid PartnerId, bool IsActive);

public static class PartnerTitles
{
    public static string Type(PartnerType type) => type switch
    {
        PartnerType.Capital => "شريك رأس مال",
        PartnerType.Speculative => "شريك مضارب",
        _ => type.ToString()
    };

    public static string Source(PartnerWithdrawalSource source) => source switch
    {
        PartnerWithdrawalSource.Drawer => "من الصندوق",
        PartnerWithdrawalSource.OwnerPocket => "من جيب صاحب المحل",
        _ => source.ToString()
    };
}

public sealed class GetPartnersHandler
{
    private readonly IApplicationDbContext _context;

    public GetPartnersHandler(IApplicationDbContext context)
    {
        _context = context;
    }

    public async Task<IReadOnlyList<PartnerDto>> HandleAsync(GetPartnersQuery query, CancellationToken cancellationToken)
    {
        var partnersQuery = _context.Partners.AsNoTracking();
        if (query.BranchId is { } branchId)
        {
            partnersQuery = partnersQuery.Where(p => p.BranchId == branchId);
        }

        var partners = await partnersQuery
            .OrderByDescending(p => p.IsActive).ThenBy(p => p.Type).ThenBy(p => p.FullName)
            .ToListAsync(cancellationToken);
        if (partners.Count == 0)
        {
            return Array.Empty<PartnerDto>();
        }

        var keys = partners.Select(p => (p.Id, p.BranchId, p.UserId)).ToList();
        var balances = await PartnerLedger.LoadBalancesAsync(_context, keys, cancellationToken);
        var capital = await PartnerLedger.LoadCapitalBalancesAsync(_context, partners.Select(p => p.Id).ToList(), null, cancellationToken);

        var userIds = partners.Where(p => p.UserId != null).Select(p => p.UserId!.Value).Distinct().ToList();
        var usernames = await _context.Users.IgnoreQueryFilters().AsNoTracking()
            .Where(u => userIds.Contains(u.Id))
            .ToDictionaryAsync(u => u.Id, u => u.Username, cancellationToken);
        var linkedPhones = await PartnerVerificationSecrets.LinkedPhoneKeysAsync(_context, partners.Select(p => p.TelegramPhone), cancellationToken);

        return partners.Select(p =>
        {
            var b = balances[p.Id];
            return new PartnerDto(
                p.Id, p.BranchId, p.FullName, (int)p.Type, PartnerTitles.Type(p.Type),
                p.UserId, p.UserId is { } uid ? usernames.GetValueOrDefault(uid) : null,
                p.SpeculativeProfitPercent, p.IsActive, p.Notes,
                capital.GetValueOrDefault(p.Id), b.SharesTotal, b.WithdrawalsTotal, b.AtCostDeductedTotal, b.Current,
                p.TelegramPhone,
                PartnerVerificationSecrets.PhoneKey(p.TelegramPhone) is { } key && linkedPhones.Contains(key),
                p.CashierBarcodeHash is not null, p.CashierBarcodeIssuedAtUtc);
        }).ToList();
    }
}

public sealed class CreatePartnerHandler
{
    private readonly IApplicationDbContext _context;

    public CreatePartnerHandler(IApplicationDbContext context)
    {
        _context = context;
    }

    public async Task<Result<Guid>> HandleAsync(CreatePartnerCommand command, CancellationToken cancellationToken)
    {
        if (!await _context.Branches.AsNoTracking().AnyAsync(b => b.Id == command.BranchId, cancellationToken))
        {
            return Result.Failure<Guid>(Error.NotFound("Partner.BranchNotFound", "الفرع مش موجود."));
        }

        if (!Enum.IsDefined(command.Type))
        {
            return Result.Failure<Guid>(Error.Validation("Partner.TypeInvalid", "نوع الشريك مش صحيح."));
        }

        var error = await PartnerRules.ValidateAsync(_context, command.BranchId, null, command.Type, command.UserId,
            command.SpeculativeProfitPercent, command.FullName, command.Notes, cancellationToken);
        if (error is not null)
        {
            return Result.Failure<Guid>(error);
        }

        Partner partner;
        try
        {
            partner = new Partner(command.BranchId, command.FullName, command.Type, command.UserId,
                command.Type == PartnerType.Speculative ? command.SpeculativeProfitPercent : null, command.Notes);
        }
        catch (DomainException ex)
        {
            return Result.Failure<Guid>(Error.Validation("Partner.Invalid", ex.Message));
        }

        _context.Partners.Add(partner);
        await _context.SaveChangesAsync(cancellationToken);
        return Result.Success(partner.Id);
    }
}

public sealed class UpdatePartnerHandler
{
    private readonly IApplicationDbContext _context;

    public UpdatePartnerHandler(IApplicationDbContext context)
    {
        _context = context;
    }

    public async Task<Result> HandleAsync(UpdatePartnerCommand command, CancellationToken cancellationToken)
    {
        var partner = await _context.Partners.FirstOrDefaultAsync(p => p.Id == command.PartnerId, cancellationToken);
        if (partner is null)
        {
            return Result.Failure(Error.NotFound("Partner.NotFound", "الشريك مش موجود."));
        }

        var error = await PartnerRules.ValidateAsync(_context, partner.BranchId, partner.Id, partner.Type, command.UserId,
            command.SpeculativeProfitPercent, command.FullName, command.Notes, cancellationToken);
        if (error is not null)
        {
            return Result.Failure(error);
        }

        try
        {
            partner.Update(command.FullName, command.UserId,
                partner.Type == PartnerType.Speculative ? command.SpeculativeProfitPercent : null, command.Notes);
        }
        catch (DomainException ex)
        {
            return Result.Failure(Error.Validation("Partner.Invalid", ex.Message));
        }

        await _context.SaveChangesAsync(cancellationToken);
        return Result.Success();
    }
}

public sealed class SetPartnerActiveHandler
{
    private readonly IApplicationDbContext _context;

    public SetPartnerActiveHandler(IApplicationDbContext context)
    {
        _context = context;
    }

    public async Task<Result> HandleAsync(SetPartnerActiveCommand command, CancellationToken cancellationToken)
    {
        var partner = await _context.Partners.FirstOrDefaultAsync(p => p.Id == command.PartnerId, cancellationToken);
        if (partner is null)
        {
            return Result.Failure(Error.NotFound("Partner.NotFound", "الشريك مش موجود."));
        }

        if (command.IsActive && partner.Type == PartnerType.Speculative)
        {
            var error = await PartnerRules.ValidateSpeculativeTotalAsync(
                _context, partner.BranchId, partner.Id, partner.SpeculativeProfitPercent ?? 0m, cancellationToken);
            if (error is not null)
            {
                return Result.Failure(error);
            }
        }

        partner.SetActive(command.IsActive);
        await _context.SaveChangesAsync(cancellationToken);
        return Result.Success();
    }
}

internal static class PartnerRules
{
    public static async Task<Error?> ValidateAsync(
        IApplicationDbContext context, Guid branchId, Guid? partnerId, PartnerType type, Guid? userId,
        decimal? speculativePercent, string fullName, string? notes, CancellationToken cancellationToken)
    {
        if (string.IsNullOrWhiteSpace(fullName) || fullName.Trim().Length > Partner.MaxNameLength)
        {
            return Error.Validation("Partner.NameInvalid", "اسم الشريك مطلوب (لحد 200 حرف).");
        }

        if (notes is { Length: > Partner.MaxNotesLength })
        {
            return Error.Validation("Partner.NotesTooLong", "الملاحظات طويلة كتير.");
        }

        if (userId is { } uid)
        {
            if (!await context.Users.AsNoTracking().AnyAsync(u => u.Id == uid, cancellationToken))
            {
                return Error.NotFound("Partner.UserNotFound", "حساب المستخدم مش موجود.");
            }

            var taken = await context.Partners.IgnoreQueryFilters().AsNoTracking()
                .AnyAsync(p => p.BranchId == branchId && p.UserId == uid && p.Id != partnerId, cancellationToken);
            if (taken)
            {
                return Error.Conflict("Partner.UserTaken", "هالحساب مربوط بشريك تاني بنفس الفرع.");
            }
        }

        if (type == PartnerType.Speculative)
        {
            if (speculativePercent is not { } percent || percent <= 0 || percent > 100)
            {
                return Error.Validation("Partner.PercentInvalid", "نسبة المضارب لازم تكون بين 0 و100.");
            }

            return await ValidateSpeculativeTotalAsync(context, branchId, partnerId, percent, cancellationToken);
        }

        return null;
    }

    /// <summary>مجموع نسب المضاربين الفعّالين بالفرع ما بيتجاوز 100%.</summary>
    public static async Task<Error?> ValidateSpeculativeTotalAsync(
        IApplicationDbContext context, Guid branchId, Guid? partnerId, decimal percent, CancellationToken cancellationToken)
    {
        var others = await context.Partners.IgnoreQueryFilters().AsNoTracking()
            .Where(p => p.BranchId == branchId && p.IsActive && p.Type == PartnerType.Speculative && p.Id != partnerId)
            .SumAsync(p => p.SpeculativeProfitPercent ?? 0m, cancellationToken);

        return others + percent > 100
            ? Error.Validation("Partner.SpeculativeTotalExceeded", $"مجموع نسب المضاربين بالفرع رح يصير {others + percent:0.##}% - أكتر من 100%.")
            : null;
    }
}

// ===== الكشوف الشهرية =====

public sealed record PartnerStatementLineDto(
    Guid PartnerId, string PartnerName, int TypeCode, string TypeTitle, decimal? CapitalBalance, decimal SharePercent, decimal ShareAmount);

public sealed record PartnerStatementDto(
    Guid Id, Guid BranchId, string BranchName, int Year, int Month, decimal NetProfit, decimal UnallocatedAmount,
    DateTime GeneratedAtUtc, bool IsAutomatic, IReadOnlyList<PartnerStatementLineDto> Lines, int UncostedItemsCount = 0,
    DateTime? StaleSinceUtc = null, string? StaleReason = null);

public sealed record PartnerStatementSummaryDto(
    Guid Id, Guid BranchId, string BranchName, int Year, int Month, decimal NetProfit, decimal UnallocatedAmount,
    DateTime GeneratedAtUtc, bool IsAutomatic, int PartnerCount, int UncostedItemsCount = 0,
    DateTime? StaleSinceUtc = null, string? StaleReason = null);

public sealed record GeneratePartnerStatementCommand(Guid BranchId, int Year, int Month);

public sealed record GetPartnerStatementsQuery(Guid? BranchId);

public sealed class GeneratePartnerStatementHandler
{
    private readonly IApplicationDbContext _context;
    private readonly IDateTimeProvider _dateTimeProvider;
    private readonly ICurrentUserContext _currentUser;
    private readonly INotificationDispatcher _notificationDispatcher;

    public GeneratePartnerStatementHandler(
        IApplicationDbContext context, IDateTimeProvider dateTimeProvider, ICurrentUserContext currentUser,
        INotificationDispatcher notificationDispatcher)
    {
        _context = context;
        _dateTimeProvider = dateTimeProvider;
        _currentUser = currentUser;
        _notificationDispatcher = notificationDispatcher;
    }

    /// <summary>إصدار يدوي (أو إعادة إصدار لو موجود) - بيحسب الربح من جديد وبيستبدل الأنصبة.</summary>
    public async Task<Result<PartnerStatementDto>> HandleAsync(GeneratePartnerStatementCommand command, CancellationToken cancellationToken)
    {
        if (!_currentUser.IsCrossBranchAccessAllowed && _currentUser.BranchId != command.BranchId)
        {
            return Result.Failure<PartnerStatementDto>(Error.Forbidden("PartnerStatement.BranchNotAllowed", "مش فرعك."));
        }

        var generated = await new PartnerStatementGenerator(_context, _dateTimeProvider)
            .GenerateAsync(command.BranchId, command.Year, command.Month, isAutomatic: false, cancellationToken);
        if (generated.IsFailure)
        {
            return Result.Failure<PartnerStatementDto>(generated.Error!);
        }

        await PartnerStatementAlerts.NotifyIfUncostedAsync(_context, _notificationDispatcher, generated.Value, cancellationToken);

        var dto = await GetPartnerStatementByIdHandler.LoadAsync(_context, generated.Value, cancellationToken);
        return dto is null
            ? Result.Failure<PartnerStatementDto>(Error.NotFound("PartnerStatement.NotFound", "الكشف مش موجود."))
            : Result.Success(dto);
    }
}

public sealed class GetPartnerStatementsHandler
{
    private readonly IApplicationDbContext _context;

    public GetPartnerStatementsHandler(IApplicationDbContext context)
    {
        _context = context;
    }

    public async Task<IReadOnlyList<PartnerStatementSummaryDto>> HandleAsync(GetPartnerStatementsQuery query, CancellationToken cancellationToken)
    {
        var statements = _context.PartnerMonthlyStatements.AsNoTracking();
        if (query.BranchId is { } branchId)
        {
            statements = statements.Where(s => s.BranchId == branchId);
        }

        var rows = await statements
            .OrderByDescending(s => s.Year).ThenByDescending(s => s.Month)
            .Select(s => new { s.Id, s.BranchId, s.Year, s.Month, s.NetProfit, s.UnallocatedAmount, s.GeneratedAtUtc, s.IsAutomatic, s.UncostedItemsCount, s.StaleSinceUtc, s.StaleReason, Count = s.Lines.Count })
            .Take(60)
            .ToListAsync(cancellationToken);

        var branchIds = rows.Select(r => r.BranchId).Distinct().ToList();
        var branchNames = await _context.Branches.AsNoTracking()
            .Where(b => branchIds.Contains(b.Id))
            .ToDictionaryAsync(b => b.Id, b => b.Name, cancellationToken);

        return rows.Select(r => new PartnerStatementSummaryDto(
            r.Id, r.BranchId, branchNames.GetValueOrDefault(r.BranchId, ""), r.Year, r.Month, r.NetProfit, r.UnallocatedAmount,
            r.GeneratedAtUtc, r.IsAutomatic, r.Count, r.UncostedItemsCount, r.StaleSinceUtc, r.StaleReason)).ToList();
    }
}

public sealed class GetPartnerStatementByIdHandler
{
    private readonly IApplicationDbContext _context;

    public GetPartnerStatementByIdHandler(IApplicationDbContext context)
    {
        _context = context;
    }

    public async Task<PartnerStatementDto?> HandleAsync(Guid statementId, CancellationToken cancellationToken) =>
        await LoadAsync(_context, statementId, cancellationToken);

    internal static async Task<PartnerStatementDto?> LoadAsync(IApplicationDbContext context, Guid statementId, CancellationToken cancellationToken)
    {
        var statement = await context.PartnerMonthlyStatements.AsNoTracking()
            .Include(s => s.Lines)
            .FirstOrDefaultAsync(s => s.Id == statementId, cancellationToken);
        if (statement is null)
        {
            return null;
        }

        var partnerIds = statement.Lines.Select(l => l.PartnerId).ToList();
        var names = await context.Partners.IgnoreQueryFilters().AsNoTracking()
            .Where(p => partnerIds.Contains(p.Id))
            .ToDictionaryAsync(p => p.Id, p => p.FullName, cancellationToken);
        var branchName = await context.Branches.AsNoTracking()
            .Where(b => b.Id == statement.BranchId).Select(b => b.Name).FirstOrDefaultAsync(cancellationToken) ?? "";

        return new PartnerStatementDto(
            statement.Id, statement.BranchId, branchName, statement.Year, statement.Month, statement.NetProfit, statement.UnallocatedAmount,
            statement.GeneratedAtUtc, statement.IsAutomatic,
            statement.Lines
                .OrderBy(l => l.PartnerType).ThenByDescending(l => l.ShareAmount)
                .Select(l => new PartnerStatementLineDto(
                    l.PartnerId, names.GetValueOrDefault(l.PartnerId, "(شريك محذوف)"), (int)l.PartnerType, PartnerTitles.Type(l.PartnerType),
                    l.CapitalBalance, l.SharePercent, l.ShareAmount))
                .ToList(),
            statement.UncostedItemsCount,
            statement.StaleSinceUtc,
            statement.StaleReason);
    }
}

/// <summary>
/// تنبيه "خطير" لما كشف شركاء نزل وفيه بنود بلا تكلفة معروفة (الربح مبالغ فيه على الأغلب - بضاعة افتتاح ما انسجّلت، المراجعة
/// النقدية 6/10/2026 بند 1). الكشف بينزل كالعادة والتنبيه بس بيعلّم (§1.6: سماح مع تعليم، مش منع).
/// </summary>
public static class PartnerStatementAlerts
{
    public static async Task NotifyIfUncostedAsync(
        IApplicationDbContext context, INotificationDispatcher dispatcher, Guid statementId, CancellationToken cancellationToken)
    {
        var statement = await context.PartnerMonthlyStatements.IgnoreQueryFilters().AsNoTracking()
            .Where(s => s.Id == statementId)
            .Select(s => new { s.BranchId, s.Year, s.Month, s.NetProfit, s.UncostedItemsCount })
            .FirstOrDefaultAsync(cancellationToken);
        if (statement is null || statement.UncostedItemsCount <= 0)
        {
            return;
        }

        var branchName = await AlertText.BranchNameAsync(context, statement.BranchId, cancellationToken);
        await dispatcher.NotifyAsync(
            $"كشف الشركاء {statement.Month}/{statement.Year} فيه بنود بلا تكلفة — راجعه قبل السحب",
            $"الفرع: {branchName}\n"
            + $"عدد البنود بلا تكلفة معروفة: {statement.UncostedItemsCount} (بيع/جرد/تلف/ضيافة)\n"
            + $"صافي الربح بالكشف: {statement.NetProfit:0.000} د.أ — على الأغلب مبالغ فيه لأنها بتتحسب بلا تكلفة.\n"
            + "الحل: سجّل بضاعة الافتتاح كرصيد افتتاحي (صفحة المشتريات) ثم أعد إصدار الكشف.",
            cancellationToken,
            NotificationSeverity.Critical,
            link: "/partners?tab=statements");
    }
}

// ===== كشف حساب شريك (كل الحركات برصيد متراكم) =====

public sealed record PartnerLedgerEntryDto(DateTime OccurredAtUtc, string KindCode, string KindTitle, string Description, decimal Amount, decimal RunningBalance);

public sealed record PartnerLedgerDto(Guid PartnerId, string PartnerName, decimal CurrentBalance, IReadOnlyList<PartnerLedgerEntryDto> Entries);

public sealed class GetPartnerLedgerHandler
{
    private readonly IApplicationDbContext _context;

    public GetPartnerLedgerHandler(IApplicationDbContext context)
    {
        _context = context;
    }

    public async Task<PartnerLedgerDto?> HandleAsync(Guid partnerId, CancellationToken cancellationToken)
    {
        var partner = await _context.Partners.AsNoTracking().FirstOrDefaultAsync(p => p.Id == partnerId, cancellationToken);
        if (partner is null)
        {
            return null;
        }

        var raw = new List<(DateTime At, string Code, string Title, string Description, decimal Amount)>();

        var shares = await _context.PartnerStatementLines.AsNoTracking()
            .Where(l => l.PartnerId == partnerId)
            .Join(_context.PartnerMonthlyStatements.IgnoreQueryFilters().AsNoTracking(), l => l.StatementId, s => s.Id,
                (l, s) => new { s.Year, s.Month, s.GeneratedAtUtc, l.ShareAmount, l.SharePercent })
            .ToListAsync(cancellationToken);
        raw.AddRange(shares.Select(s => (
            new DateTime(s.Year, s.Month, 1, 0, 0, 0, DateTimeKind.Utc).AddMonths(1),
            "share", "نصيب شهري", $"كشف {s.Month}/{s.Year} ({s.SharePercent:0.##}%)", s.ShareAmount)));

        var withdrawals = await _context.PartnerWithdrawals.AsNoTracking()
            .Where(w => w.PartnerId == partnerId)
            .Select(w => new { w.OccurredAtUtc, w.Amount, w.Source, w.Notes, w.RecordedAtCashier })
            .ToListAsync(cancellationToken);
        raw.AddRange(withdrawals.Select(w => (
            w.OccurredAtUtc, "withdrawal", "سحب",
            PartnerTitles.Source(w.Source) + (w.RecordedAtCashier ? " (من الكاشير)" : "") + (w.Notes is null ? "" : $" - {w.Notes}"),
            -w.Amount)));

        var atCost = await PartnerLedger.LoadAtCostDeductionsAsync(_context, new[] { (partner.Id, partner.BranchId, partner.UserId) }, cancellationToken);
        raw.AddRange(atCost.Select(a => (a.CreatedAtUtc, "at-cost", "بضاعة بسعر التكلفة", $"فاتورة {a.InvoiceNumber} (اخصمها مني)", -a.Remaining)));

        var running = 0m;
        var entries = raw.OrderBy(r => r.At)
            .Select(r =>
            {
                running += r.Amount;
                return new PartnerLedgerEntryDto(r.At, r.Code, r.Title, r.Description, r.Amount, running);
            })
            .ToList();

        return new PartnerLedgerDto(partner.Id, partner.FullName, running, entries);
    }
}
