using Microsoft.EntityFrameworkCore;
using SupermarketSystem.Application.Common.Interfaces;
using SupermarketSystem.Application.Common.Results;
using SupermarketSystem.Application.Finance.GetMonthlyProfitStatement;
using SupermarketSystem.Domain.Finance;
using SupermarketSystem.Domain.Partners;
using SupermarketSystem.Domain.Sales;

namespace SupermarketSystem.Application.Partners;

/// <summary>
/// رصيد الشريك (ديناميكي، بلا Ledger مخزَّن - نفس فلسفة GetCustomerDebts):
///   مجموع أنصبته من كل الكشوف الشهرية
///   − سحوباته (صندوق أو جيب)
///   − "اخصمها مني" (سحب بضاعة بسعر التكلفة، راجع SaleInvoice.IsDeductedFromShare): المتبقي على الفاتورة
///     (الإجمالي − المرتجع − المدفوع لاحقًا)، بس للفواتير اللي عملها حساب الشريك نفسه (Partner.UserId) بنفس الفرع.
/// ممكن يكون سالب (سحب مسبق/سلفة) - بيترحّل ويتغطّى من أنصبة الشهور الجاية.
/// </summary>
public sealed record PartnerBalance(Guid PartnerId, decimal SharesTotal, decimal WithdrawalsTotal, decimal AtCostDeductedTotal)
{
    public decimal Current => SharesTotal - WithdrawalsTotal - AtCostDeductedTotal;
}

public static class PartnerLedger
{
    public static async Task<Dictionary<Guid, PartnerBalance>> LoadBalancesAsync(
        IApplicationDbContext context,
        IReadOnlyCollection<(Guid Id, Guid BranchId, Guid? UserId)> partners,
        CancellationToken cancellationToken)
    {
        var ids = partners.Select(p => p.Id).ToList();

        var shares = await context.PartnerStatementLines.AsNoTracking()
            .Where(l => ids.Contains(l.PartnerId))
            .GroupBy(l => l.PartnerId)
            .Select(g => new { PartnerId = g.Key, Total = g.Sum(l => l.ShareAmount) })
            .ToDictionaryAsync(x => x.PartnerId, x => x.Total, cancellationToken);

        var withdrawals = await context.PartnerWithdrawals.IgnoreQueryFilters().AsNoTracking()
            .Where(w => ids.Contains(w.PartnerId))
            .GroupBy(w => w.PartnerId)
            .Select(g => new { PartnerId = g.Key, Total = g.Sum(w => w.Amount) })
            .ToDictionaryAsync(x => x.PartnerId, x => x.Total, cancellationToken);

        var atCost = await LoadAtCostDeductionsAsync(context, partners, cancellationToken);

        return partners.ToDictionary(
            p => p.Id,
            p => new PartnerBalance(
                p.Id,
                shares.GetValueOrDefault(p.Id),
                withdrawals.GetValueOrDefault(p.Id),
                atCost.Where(a => a.PartnerId == p.Id).Sum(a => a.Remaining)));
    }

    public sealed record AtCostDeduction(Guid PartnerId, Guid SaleInvoiceId, string InvoiceNumber, DateTime CreatedAtUtc, decimal Remaining);

    /// <summary>فواتير "اخصمها مني" لكل شريك (مش ملغاة، وعليها متبقٍّ).</summary>
    public static async Task<List<AtCostDeduction>> LoadAtCostDeductionsAsync(
        IApplicationDbContext context,
        IReadOnlyCollection<(Guid Id, Guid BranchId, Guid? UserId)> partners,
        CancellationToken cancellationToken)
    {
        var linked = partners.Where(p => p.UserId is not null).ToList();
        if (linked.Count == 0)
        {
            return new List<AtCostDeduction>();
        }

        var userIds = linked.Select(p => p.UserId!.Value).Distinct().ToList();
        var branchIds = linked.Select(p => p.BranchId).Distinct().ToList();

        var invoices = await context.SaleInvoices.IgnoreQueryFilters().AsNoTracking()
            .Where(s => s.IsAtCostWithdrawal && s.IsDeductedFromShare && s.Status != SaleInvoiceStatus.Voided
                        && s.CreatedByUserId != null && userIds.Contains(s.CreatedByUserId.Value) && branchIds.Contains(s.BranchId))
            .Select(s => new
            {
                s.Id, s.InvoiceNumber, s.BranchId, UserId = s.CreatedByUserId!.Value, s.CreatedAtUtc,
                Remaining = s.TotalAmount - s.TotalReturnedAmount - s.TotalPaidAmount
            })
            .ToListAsync(cancellationToken);

        return invoices
            .Where(i => i.Remaining > 0)
            .SelectMany(i => linked
                .Where(p => p.BranchId == i.BranchId && p.UserId == i.UserId)
                .Select(p => new AtCostDeduction(p.Id, i.Id, i.InvoiceNumber, i.CreatedAtUtc, i.Remaining)))
            .ToList();
    }

    /// <summary>رأس مال كل شريك = إضافاته − سحوباته (CapitalTransaction.PartnerId)، لحد لحظة معيّنة (حصرية) أو لليوم.</summary>
    public static async Task<Dictionary<Guid, decimal>> LoadCapitalBalancesAsync(
        IApplicationDbContext context, IReadOnlyCollection<Guid> partnerIds, DateTime? beforeUtc, CancellationToken cancellationToken)
    {
        var ids = partnerIds.ToList();
        var query = context.CapitalTransactions.IgnoreQueryFilters().AsNoTracking()
            .Where(c => c.PartnerId != null && ids.Contains(c.PartnerId.Value));
        if (beforeUtc is { } cutoff)
        {
            query = query.Where(c => c.OccurredAtUtc < cutoff);
        }

        var rows = await query
            .GroupBy(c => c.PartnerId!.Value)
            .Select(g => new
            {
                PartnerId = g.Key,
                Total = g.Sum(c => c.Type == CapitalTransactionType.Deposit ? c.Amount : -c.Amount)
            })
            .ToListAsync(cancellationToken);

        return rows.ToDictionary(r => r.PartnerId, r => r.Total);
    }
}

/// <summary>
/// توليد الكشف الشهري لفرع (قرارات 20-21/9/2026) - مشترك بين زر "إصدار" بلوحة الإدارة والتوليد التلقائي ببداية
/// الشهر (PartnerStatementBackgroundService). بياخد IApplicationDbContext صريح عشان الخدمة الخلفية تبعتله context
/// بلا فلتر فرع (ما في مستخدم بالطلب).
///
/// التوزيع:
///   1. صافي ربح الشهر = نفس GetMonthlyProfitStatement بالضبط (Snapshot مجمَّد بالكشف).
///   2. المضارب: نسبته الثابتة من الصافي - بس بشهر ربح. بشهر خسارة نصيبه صفر (ما بيشارك برأس المال فما بيتحمّل خسارته).
///   3. الباقي (ربح أو خسارة) بين شركاء رأس المال بنسبة رأس مال كل واحد آخر الشهر. الخسارة بتنقص أرصدتهم.
///   4. التقريب لأقرب فلس، وفرق التقريب بيروح لصاحب أكبر رأس مال - المجموع بيطابق بالضبط.
///   5. فرع بلا شركاء رأس مال = الباقي "غير موزَّع" (بيبين بالكشف)؛ شركاء رأس مال بلا رأس مال مسجَّل = رفض برسالة واضحة.
/// </summary>
public sealed class PartnerStatementGenerator
{
    private readonly IApplicationDbContext _context;
    private readonly IDateTimeProvider _dateTimeProvider;

    public PartnerStatementGenerator(IApplicationDbContext context, IDateTimeProvider dateTimeProvider)
    {
        _context = context;
        _dateTimeProvider = dateTimeProvider;
    }

    public async Task<Result<Guid>> GenerateAsync(Guid branchId, int year, int month, bool isAutomatic, CancellationToken cancellationToken)
    {
        if (month is < 1 or > 12 || year < 2000)
        {
            return Result.Failure<Guid>(Error.Validation("PartnerStatement.InvalidPeriod", "الشهر مش صحيح."));
        }

        var periodStartUtc = new DateTime(year, month, 1, 0, 0, 0, DateTimeKind.Utc);
        var periodEndUtc = periodStartUtc.AddMonths(1);
        if (periodEndUtc > _dateTimeProvider.UtcNow)
        {
            return Result.Failure<Guid>(Error.BusinessRule("PartnerStatement.MonthNotEnded", "الشهر لسه ما خلص - الكشف بينزل بعد نهايته."));
        }

        var partners = await _context.Partners.IgnoreQueryFilters().AsNoTracking()
            .Where(p => p.BranchId == branchId && p.IsActive)
            .Select(p => new { p.Id, p.Type, p.SpeculativeProfitPercent })
            .ToListAsync(cancellationToken);

        if (partners.Count == 0)
        {
            return Result.Failure<Guid>(Error.BusinessRule("PartnerStatement.NoPartners", "ما في شركاء فعّالين بهالفرع."));
        }

        var profitResult = await new GetMonthlyProfitStatementHandler(_context)
            .HandleAsync(new GetMonthlyProfitStatementQuery(branchId, year, month), cancellationToken);
        if (profitResult.IsFailure)
        {
            return Result.Failure<Guid>(profitResult.Error!);
        }

        var netProfit = Math.Round(profitResult.Value.NetProfit, 3, MidpointRounding.AwayFromZero);
        var lines = new List<(Guid PartnerId, PartnerType Type, decimal? CapitalBalance, decimal SharePercent, decimal ShareAmount)>();

        // (2) المضاربين.
        var speculativeTotal = 0m;
        foreach (var partner in partners.Where(p => p.Type == PartnerType.Speculative))
        {
            var percent = partner.SpeculativeProfitPercent ?? 0m;
            var share = netProfit > 0 ? Math.Round(netProfit * percent / 100m, 3, MidpointRounding.AwayFromZero) : 0m;
            speculativeTotal += share;
            lines.Add((partner.Id, PartnerType.Speculative, null, netProfit > 0 ? percent : 0m, share));
        }

        // (3) شركاء رأس المال.
        var remaining = netProfit - speculativeTotal;
        var capitalPartners = partners.Where(p => p.Type == PartnerType.Capital).Select(p => p.Id).ToList();
        var unallocated = 0m;

        if (capitalPartners.Count == 0)
        {
            unallocated = remaining;
        }
        else
        {
            var capital = await PartnerLedger.LoadCapitalBalancesAsync(_context, capitalPartners, periodEndUtc, cancellationToken);
            var positive = capitalPartners.Where(id => capital.GetValueOrDefault(id) > 0).ToList();
            var totalCapital = positive.Sum(id => capital[id]);

            if (totalCapital <= 0)
            {
                return Result.Failure<Guid>(Error.BusinessRule(
                    "PartnerStatement.NoCapital",
                    "ما في رأس مال مسجَّل لشركاء رأس المال بهالفرع لحد آخر الشهر - سجّله من صفحة المالية (حركة رأس مال مع اختيار الشريك)."));
            }

            var shares = positive.ToDictionary(id => id, id => Math.Round(remaining * capital[id] / totalCapital, 3, MidpointRounding.AwayFromZero));
            var roundingDiff = remaining - shares.Values.Sum();
            if (roundingDiff != 0)
            {
                var largest = positive.OrderByDescending(id => capital[id]).First();
                shares[largest] += roundingDiff;
            }

            foreach (var id in capitalPartners)
            {
                var balance = capital.GetValueOrDefault(id);
                var share = shares.GetValueOrDefault(id);
                var percent = netProfit != 0 ? Math.Round(share / netProfit * 100m, 4) : (balance > 0 ? Math.Round(balance / totalCapital * 100m, 4) : 0m);
                lines.Add((id, PartnerType.Capital, balance, percent, share));
            }
        }

        var statement = await _context.PartnerMonthlyStatements.IgnoreQueryFilters()
            .Include(s => s.Lines)
            .FirstOrDefaultAsync(s => s.BranchId == branchId && s.Year == year && s.Month == month, cancellationToken);

        if (statement is null)
        {
            statement = new PartnerMonthlyStatement(branchId, year, month);
            _context.PartnerMonthlyStatements.Add(statement);
        }
        else if (isAutomatic)
        {
            // التوليد التلقائي ما بيكتب فوق كشف موجود أبدًا (ممكن يكون انعاد إصداره يدويًا).
            return Result.Success(statement.Id);
        }
        else
        {
            _context.PartnerStatementLines.RemoveRange(statement.Lines.ToList());
        }

        statement.SetContent(netProfit, unallocated, _dateTimeProvider.UtcNow, isAutomatic, lines);

        // الأسطر معرّفها بينولد بالكود (Entity)، فـEF بيفكّر السطر الجديد جوّا كشف موجود "صف موجود" وبيعمل
        // UPDATE بدل INSERT (كان بيطلع Concurrency 409 بإعادة الإصدار) - فبنعلّمها جديدة صراحة.
        foreach (var line in statement.Lines)
        {
            _context.PartnerStatementLines.Add(line);
        }

        await _context.SaveChangesAsync(cancellationToken);
        return Result.Success(statement.Id);
    }
}
