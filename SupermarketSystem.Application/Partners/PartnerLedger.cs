using Microsoft.EntityFrameworkCore;
using SupermarketSystem.Application.Common.Interfaces;
using SupermarketSystem.Application.Common.Results;
using SupermarketSystem.Application.Common.Time;
using SupermarketSystem.Application.Finance.GetMonthlyProfitStatement;
using SupermarketSystem.Domain.Finance;
using SupermarketSystem.Domain.Partners;
using SupermarketSystem.Domain.Sales;

namespace SupermarketSystem.Application.Partners;

/// <summary>
/// رصيد الشريك (ديناميكي، بلا Ledger مخزَّن - نفس فلسفة GetCustomerDebts):
///   مجموع أنصبته من كل الكشوف الشهرية
///   − سحوباته (صندوق أو جيب)
///   − إعادة استثمار برأس المال (PartnerReinvestment) + تراجعاتها (PartnerReinvestmentReversal)
///   − "اخصمها مني" (سحب بضاعة بسعر التكلفة، راجع SaleInvoice.IsDeductedFromShare): المتبقي على الفاتورة
///     (الإجمالي − المرتجع − المدفوع لاحقًا)، بس للفواتير اللي عملها حساب الشريك نفسه (Partner.UserId) بنفس الفرع.
/// ممكن يكون سالب (سحب مسبق/سلفة) - بيترحّل ويتغطّى من أنصبة الشهور الجاية.
/// </summary>
public sealed record PartnerBalance(Guid PartnerId, decimal SharesTotal, decimal WithdrawalsTotal, decimal AtCostDeductedTotal, decimal ReinvestedTotal = 0m)
{
    /// <summary>الرصيد الحالي. إعادة الاستثمار برأس المال بتنقصه (المبلغ صار رأس مال، مش رصيد قابل للسحب).</summary>
    public decimal Current => SharesTotal - WithdrawalsTotal - AtCostDeductedTotal - ReinvestedTotal;
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

        var reinvested = await context.PartnerReinvestments.IgnoreQueryFilters().AsNoTracking()
            .Where(r => ids.Contains(r.PartnerId))
            .GroupBy(r => r.PartnerId)
            .Select(g => new { PartnerId = g.Key, Total = g.Sum(r => r.Amount) })
            .ToDictionaryAsync(x => x.PartnerId, x => x.Total, cancellationToken);

        // الصافي بعد التراجعات: التراجع بيرجّع المبلغ لرصيد الشريك.
        var reversed = await context.PartnerReinvestmentReversals.IgnoreQueryFilters().AsNoTracking()
            .Where(r => ids.Contains(r.PartnerId))
            .GroupBy(r => r.PartnerId)
            .Select(g => new { PartnerId = g.Key, Total = g.Sum(r => r.Amount) })
            .ToDictionaryAsync(x => x.PartnerId, x => x.Total, cancellationToken);

        var atCost = await LoadAtCostDeductionsAsync(context, partners, cancellationToken);

        return partners.ToDictionary(
            p => p.Id,
            p => new PartnerBalance(
                p.Id,
                shares.GetValueOrDefault(p.Id),
                withdrawals.GetValueOrDefault(p.Id),
                atCost.Where(a => a.PartnerId == p.Id).Sum(a => a.Remaining),
                reinvested.GetValueOrDefault(p.Id) - reversed.GetValueOrDefault(p.Id)));
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

    /// <summary>
    /// متوسط رأس مال كل شريك **مرجَّح بالزمن** خلال الفترة [periodStartUtc, periodEndUtc) (27/10/2026، ثغرة #1 "تلميع رأس المال"): رصيد افتتاح
    /// الفترة ثم كل حركة بوقتها، والرصيد بيتحسب بمدة بقائه (بالتِكّات). قبل هيك كانت النسبة برأس المال آخر لحظة بالشهر، فشريك بيودع مبلغ ضخم
    /// آخر يوم وبيسحبه أول يوم الجاي كان ياخد حصة الباقين (حصة شريك نزلت من 11.25 إلى 1.324 باختبار هجومي). الرصيد السالب بيحتسب صفر
    /// بمدته (ما بيخصم من غيره). رأس مال ثابت طول الشهر = نفس الرقم القديم بالضبط.
    /// </summary>
    public static async Task<Dictionary<Guid, decimal>> LoadTimeWeightedCapitalAsync(
        IApplicationDbContext context, IReadOnlyCollection<Guid> partnerIds, DateTime periodStartUtc, DateTime periodEndUtc,
        CancellationToken cancellationToken)
    {
        var ids = partnerIds.ToList();
        var transactions = await context.CapitalTransactions.IgnoreQueryFilters().AsNoTracking()
            .Where(c => c.PartnerId != null && ids.Contains(c.PartnerId.Value) && c.OccurredAtUtc < periodEndUtc)
            .Select(c => new { PartnerId = c.PartnerId!.Value, c.OccurredAtUtc, Signed = c.Type == CapitalTransactionType.Deposit ? c.Amount : -c.Amount })
            .ToListAsync(cancellationToken);

        var totalTicks = (decimal)(periodEndUtc - periodStartUtc).Ticks;
        var result = new Dictionary<Guid, decimal>();
        foreach (var id in ids)
        {
            var own = transactions.Where(t => t.PartnerId == id).OrderBy(t => t.OccurredAtUtc).ToList();
            var balance = own.Where(t => t.OccurredAtUtc < periodStartUtc).Sum(t => t.Signed);
            var cursor = periodStartUtc;
            var weighted = 0m;
            foreach (var t in own.Where(t => t.OccurredAtUtc >= periodStartUtc))
            {
                weighted += Math.Max(balance, 0m) * (t.OccurredAtUtc - cursor).Ticks;
                balance += t.Signed;
                cursor = t.OccurredAtUtc;
            }

            weighted += Math.Max(balance, 0m) * (periodEndUtc - cursor).Ticks;
            result[id] = totalTicks > 0 ? Math.Round(weighted / totalTicks, 4, MidpointRounding.AwayFromZero) : Math.Max(balance, 0m);
        }

        return result;
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

    /// <summary>
    /// الخسارة المرحّلة الداخلة لكل مضارب = LossCarriedOut من آخر كشف مخزَّن إله قبل هالشهر (سجل مجمَّد، مش إعادة حساب). أول كشف يشارك فيه = 0.
    /// السلسلة لازم تكون متصلة: أي شهر ناقص بين آخر كشف إله وهالشهر = رفض برسالة، عشان خسارة ما تضيع بصمت (أموال ناس).
    /// </summary>
    private async Task<Result<Dictionary<Guid, decimal>>> LoadMudaribCarryInAsync(
        Guid branchId, int year, int month, IReadOnlyCollection<Guid> mudaribIds, CancellationToken cancellationToken)
    {
        var carry = new Dictionary<Guid, decimal>();
        if (mudaribIds.Count == 0)
        {
            return Result.Success(carry);
        }

        var target = year * 12 + month - 1;
        var statementMonths = (await _context.PartnerMonthlyStatements.IgnoreQueryFilters().AsNoTracking()
            .Where(s => s.BranchId == branchId && s.Year * 12 + s.Month - 1 < target)
            .Select(s => s.Year * 12 + s.Month - 1)
            .ToListAsync(cancellationToken)).ToHashSet();

        var previousLines = await (
                from l in _context.PartnerStatementLines.AsNoTracking()
                join s in _context.PartnerMonthlyStatements.IgnoreQueryFilters().AsNoTracking() on l.StatementId equals s.Id
                where s.BranchId == branchId && mudaribIds.Contains(l.PartnerId) && s.Year * 12 + s.Month - 1 < target
                select new { l.PartnerId, MonthIndex = s.Year * 12 + s.Month - 1, l.LossCarriedOut })
            .ToListAsync(cancellationToken);

        foreach (var id in mudaribIds)
        {
            var last = previousLines.Where(x => x.PartnerId == id).OrderByDescending(x => x.MonthIndex).FirstOrDefault();
            if (last is null)
            {
                carry[id] = 0m; // أول كشف يشارك فيه المضارب: الترحيل بيبدأ من هون.
                continue;
            }

            for (var index = last.MonthIndex + 1; index < target; index++)
            {
                if (!statementMonths.Contains(index))
                {
                    return Result.Failure<Dictionary<Guid, decimal>>(Error.BusinessRule(
                        "PartnerStatement.PreviousMonthMissing",
                        $"كشف شهر {index % 12 + 1}/{index / 12} مش موجود - أصدره أول (بالترتيب) عشان الخسارة المرحّلة للمضارب تنحسب صح."));
                }
            }

            carry[id] = last.LossCarriedOut;
        }

        return Result.Success(carry);
    }

    public async Task<Result<Guid>> GenerateAsync(Guid branchId, int year, int month, bool isAutomatic, CancellationToken cancellationToken)
    {
        if (month is < 1 or > 12 || year is < 2000 or > 2200)
        {
            return Result.Failure<Guid>(Error.Validation("PartnerStatement.InvalidPeriod", "الشهر مش صحيح."));
        }

        var (periodStartUtc, periodEndUtc) = (await BusinessTime.LoadAsync(_context, cancellationToken)).MonthRangeUtc(year, month);
        if (periodEndUtc > _dateTimeProvider.UtcNow)
        {
            return Result.Failure<Guid>(Error.BusinessRule("PartnerStatement.MonthNotEnded", "الشهر لسه ما خلص - الكشف بينزل بعد نهايته."));
        }

        var partners = await _context.Partners.IgnoreQueryFilters().AsNoTracking()
            .Where(p => p.BranchId == branchId && p.IsActive)
            // ترتيب ثابت (الأقدم أول): لو شريكين رأس مالهم متساوي، فلس التقريب بيروح دايمًا للأقدم - كان بيعتمد على ترتيب
            // القاعدة فإعادة إصدار نفس الكشف ممكن تنقل فلس من شريك لشريك (4/10/2026، انمسك بالمحاكاة العشوائية).
            .OrderBy(p => p.CreatedAtUtc).ThenBy(p => p.Id)
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

        // بنود استُبعدت من الربح لأن ما إلها تكلفة معروفة (بضاعة افتتاح ما انسجّلت...) - الربح على الأغلب مبالغ فيه. بنحفظ العدد
        // بالكشف وبننبّه (مش بنمنع - §1.6)، عشان ما ينسحب رأس مال كأنه ربح بدون ما حدا ينتبه (المراجعة النقدية 6/10/2026، بند 1).
        var profit = profitResult.Value;
        var uncostedItems = profit.ItemsExcludedNoCostHistory
            + profit.StocktakeMovementsExcludedNoCostHistory
            + profit.WasteMovementsExcludedNoCostHistory
            + profit.ComplimentaryMovementsExcludedNoCostHistory
            + profit.EstimatedCostItemsCount;
        var lines = new List<StatementLineInput>();

        // (2) المضاربين - مع ترحيل الخسارة: ما بيربح المضارب إلا بعد ما تتغطى خسارة الشهور اللي قبل (راجع MudaribLossCarryForward).
        var speculativePartners = partners.Where(p => p.Type == PartnerType.Speculative).ToList();
        var carryInResult = await LoadMudaribCarryInAsync(branchId, year, month, speculativePartners.Select(p => p.Id).ToList(), cancellationToken);
        if (carryInResult.IsFailure)
        {
            return Result.Failure<Guid>(carryInResult.Error!);
        }

        var speculativeTotal = 0m;
        foreach (var partner in speculativePartners)
        {
            var percent = partner.SpeculativeProfitPercent ?? 0m;
            var calc = MudaribLossCarryForward.Calculate(netProfit, carryInResult.Value.GetValueOrDefault(partner.Id), percent);
            speculativeTotal += calc.Share;
            lines.Add(new StatementLineInput(
                partner.Id, PartnerType.Speculative, null, netProfit > 0 ? percent : 0m, calc.Share, calc.CarriedIn, calc.CarriedOut));
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
            // متوسط رأس المال خلال الشهر مرجَّحًا بالزمن (مش لقطة آخر لحظة) - راجع LoadTimeWeightedCapitalAsync.
            var capital = await PartnerLedger.LoadTimeWeightedCapitalAsync(_context, capitalPartners, periodStartUtc, periodEndUtc, cancellationToken);
            var positive = capitalPartners.Where(id => capital.GetValueOrDefault(id) > 0).ToList();
            var totalCapital = positive.Sum(id => capital[id]);

            if (totalCapital <= 0)
            {
                return Result.Failure<Guid>(Error.BusinessRule(
                    "PartnerStatement.NoCapital",
                    "ما في رأس مال مسجَّل لشركاء رأس المال بهالفرع خلال هالشهر - سجّله من صفحة المالية (حركة رأس مال مع اختيار الشريك)."));
            }

            var shares = positive.ToDictionary(id => id, id => Math.Round(remaining * capital[id] / totalCapital, 3, MidpointRounding.AwayFromZero));
            var roundingDiff = remaining - shares.Values.Sum();
            if (roundingDiff != 0)
            {
                var largest = positive.OrderByDescending(id => capital[id]).First(); // OrderBy ثابت: التعادل للأقدم
                shares[largest] += roundingDiff;
            }

            foreach (var id in capitalPartners)
            {
                var balance = capital.GetValueOrDefault(id);
                var share = shares.GetValueOrDefault(id);
                var percent = netProfit != 0 ? Math.Round(share / netProfit * 100m, 4) : (balance > 0 ? Math.Round(balance / totalCapital * 100m, 4) : 0m);
                lines.Add(new StatementLineInput(id, PartnerType.Capital, balance, percent, share));
            }
        }

        var statement = await _context.PartnerMonthlyStatements.IgnoreQueryFilters()
            .Include(s => s.Lines)
            .FirstOrDefaultAsync(s => s.BranchId == branchId && s.Year == year && s.Month == month, cancellationToken);

        var previousOut = statement is null
            ? new Dictionary<Guid, decimal>()
            : statement.Lines.Where(l => l.PartnerType == PartnerType.Speculative).ToDictionary(l => l.PartnerId, l => l.LossCarriedOut);

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

        // الخسارة المرحّلة اللي طلعت من هالكشف قبل وبعد: لو تغيّرت (إعادة إصدار، أو كشف أقدم انضاف) الكشوف اللي بعده انحسبت على
        // رقم قديم - بنعلّمها "قديمة" بنفس الحفظ (مش منع - §1.6 - بس الأرقام ما بتتشوه بصمت). ناقص = 0.
        var newOut = lines.Where(l => l.Type == PartnerType.Speculative).ToDictionary(l => l.PartnerId, l => l.LossCarriedOut);
        var carryChanged = newOut.Keys.Union(previousOut.Keys)
            .Any(id => newOut.GetValueOrDefault(id) != previousOut.GetValueOrDefault(id));
        if (carryChanged)
        {
            var target = year * 12 + month - 1;
            var later = await _context.PartnerMonthlyStatements.IgnoreQueryFilters()
                .Where(s => s.BranchId == branchId && s.Year * 12 + s.Month - 1 > target)
                .ToListAsync(cancellationToken);
            foreach (var l in later)
            {
                l.MarkStale(_dateTimeProvider.UtcNow,
                    $"تغيّرت الخسارة المرحّلة للمضارب بعد إصدار كشف {month}/{year} - أعد إصداره (بالترتيب من الأقدم للأحدث).");
            }
        }

        statement.SetContent(netProfit, unallocated, _dateTimeProvider.UtcNow, isAutomatic, lines, uncostedItems);

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

/// <summary>
/// ترحيل خسارة المضارب (قرار صاحب المشروع 27/10/2026: "بتترحّل طبعًا - المضارب ما يربح لحاله وأنا أتحمّل الخسارة"): نسبة المضارب بتنحسب
/// من الربح <b>بعد</b> تغطية خسارة الشهور اللي قبل. شهر خسارة: نصيبه صفر (ما بيتحمّل كخسارة سالبة) والخسارة بتنضاف للمرحَّل.
/// شهر ربح: الخسارة المرحّلة بتنخصم من الربح أول، والباقي بس بتنحسب منه نسبته. دالة نقية (بلا قاعدة بيانات) عشان تنختبر لحالها.
/// كل المبالغ بـ3 خانات، والتقريب AwayFromZero زي باقي التوزيع.
/// </summary>
public static class MudaribLossCarryForward
{
    public readonly record struct CarryResult(decimal CarriedIn, decimal ProfitAfterCarry, decimal Share, decimal CarriedOut);

    public static CarryResult Calculate(decimal netProfit, decimal carriedIn, decimal percent)
    {
        carriedIn = Math.Max(carriedIn, 0m);
        if (netProfit <= 0)
        {
            return new CarryResult(carriedIn, 0m, 0m, carriedIn - netProfit);
        }

        var offset = Math.Min(carriedIn, netProfit);
        var profitAfterCarry = netProfit - offset;
        var share = Math.Round(profitAfterCarry * percent / 100m, 3, MidpointRounding.AwayFromZero);
        return new CarryResult(carriedIn, profitAfterCarry, share, carriedIn - offset);
    }
}
