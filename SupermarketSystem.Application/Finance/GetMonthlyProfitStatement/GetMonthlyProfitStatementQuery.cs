using Microsoft.EntityFrameworkCore;
using SupermarketSystem.Application.Common.Interfaces;
using SupermarketSystem.Application.Common.Results;
using SupermarketSystem.Domain.Finance;
using SupermarketSystem.Domain.Inventory;
using SupermarketSystem.Domain.Purchasing;
using SupermarketSystem.Domain.Sales;

namespace SupermarketSystem.Application.Finance.GetMonthlyProfitStatement;

public sealed record GetMonthlyProfitStatementQuery(Guid BranchId, int Year, int Month);

public sealed record ExpenseByCategoryDto(ExpenseCategory Category, decimal Amount);

/// <summary>
/// كشف ربح شهري لفرع واحد - مستقل تمامًا عن رأس المال (CapitalTransaction):
/// هذا رقم إعلامي "كيف كان الشهر"، ما يغيّر أي رصيد تلقائيًا (طلب صاحب
/// المشروع صراحة 17/9/2026).
///
/// ═══════════════════════════════════════════════════════════════════
/// تعريف كل رقم (لتفادي أي لبس لاحق):
/// ═══════════════════════════════════════════════════════════════════
/// - TotalSales/TotalReturnedAmount/NetRevenue: بنفس تعريف GetSalesSummaryQuery
///   بالضبط (فواتير غير ملغاة أُنشئت خلال الشهر بهذا الفرع، ناقص كل
///   مرتجعاتها المسجَّلة على الفاتورة الأصلية - بغض النظر متى تم الإرجاع
///   فعليًا، نفس اتفاقية SaleInvoice.TotalReturnedAmount الموجودة أصلًا).
/// - CostOfGoodsSold: (Quantity - QuantityReturned) × UnitCostSnapshot لكل
///   سطر بيع بهالفواتير - يستثني الكمية المرتجعة فعليًا من كل سطر (نفس
///   منطق NetRevenue بالضبط، بس على مستوى السطر لا الفاتورة).
/// - ItemsExcludedNoCostHistory: عدد أسطر البيع بلا UnitCostSnapshot (منتج
///   بيع قبل أي فاتورة شراء مسجَّلة له) - **مُستبعدة كليًا** من
///   CostOfGoodsSold، لا مُحتسبة بتكلفة صفر (كان رح يضخّم GrossProfit
///   بشكل مضلِّل). أي رقم بهالحقل غير صفر = الربح المعروض هون أعلى من
///   الحقيقي فعليًا، ويحتاج مراجعة يدوية لتلك الأسطر تحديدًا.
/// - GrossProfit = NetRevenue - CostOfGoodsSold.
/// - TotalExpenses: مصاريف Expense بنفس (BranchId، PeriodYear، PeriodMonth)
///   المطلوبة - لا PaymentDateUtc (راجع تعليق Expense.cs بالـDomain).
/// - StocktakeSurplusValue/StocktakeShortageValue: قرار صاحب المشروع
///   الصريح (21/9/2026) - فروقات الجرد (`StockMovement` بنوع
///   `StocktakeCorrectionIncrease`/`StocktakeCorrectionDecrease`) بشهر
///   *اعتماد* الجرد نفسه (لا تاريخ دفع منفصل، الحدث لحظي). التقييم بنفس
///   منطق `UnitCostSnapshot` بالضبط (تكلفة الدفعة المباشرة للمتتبَّع،
///   متوسط مرجّح من فواتير الشراء المستلمة لغير المتتبَّع) - بس بفارق
///   جوهري واحد: هذا رقم **محسوب لحظيًا بكل استدعاء للتقرير، لا Snapshot
///   مجمَّد** (`StockMovement` ما بتحمل تكلفة مخزَّنة وقتها زي
///   `SaleInvoiceItem.UnitCostSnapshot`) - فلو انضافت فاتورة شراء بتاريخ
///   رجعي (Backdated) لنفس الشهر بعد ما اتشاف التقرير، الرقم ممكن يتغيّر
///   بمرّة تانية. المتوسط المرجّح هون محسوب حتى *نهاية* الشهر المطلوب
///   (`periodEndUtc`)، لا لحظة كل حركة بالضبط - تبسيط مقصود لتفادي استعلام
///   منفصل لكل حركة جرد، مقبول لأنه أصلًا رقم غير مجمَّد بطبيعته.
///   `StocktakeMovementsExcludedNoCostHistory`: نفس فلسفة
///   `ItemsExcludedNoCostHistory` بالضبط - حركة جرد لمنتج بلا تاريخ شراء
///   سابق تُستبعَد كليًا، لا تُحتسب بتكلفة صفر.
/// - WasteLossValue: قرار صاحب المشروع (24/9/2026) - التلف/الهلاك
///   (`MovementType.WasteOut`) خسارة بشهر حدوثه، **إلا** لو مستبدَل من
///   الشركة (`IsReplacedBySupplier` - الشركة عوّضت، فما في خسارة على المحل).
///   نفس تقييم فروقات الجرد بالضبط (تكلفة دفعة/متوسط مرجّح حتى نهاية الشهر،
///   محسوب لحظيًا لا مجمَّد)، و`WasteMovementsExcludedNoCostHistory` نفس
///   فلسفة الاستبعاد (بلا تاريخ شراء = مستبعد، لا تكلفة صفر).
/// - NetProfit = GrossProfit - TotalExpenses + StocktakeSurplusValue -
///   StocktakeShortageValue - WasteLossValue. هذا الرقم النهائي "ربح الشهر".
/// </summary>
public sealed class GetMonthlyProfitStatementHandler
{
    private readonly IApplicationDbContext _context;

    public GetMonthlyProfitStatementHandler(IApplicationDbContext context)
    {
        _context = context;
    }

    public async Task<Result<GetMonthlyProfitStatementResponse>> HandleAsync(
        GetMonthlyProfitStatementQuery query, CancellationToken cancellationToken)
    {
        if (query.Month is < 1 or > 12)
        {
            return Result.Failure<GetMonthlyProfitStatementResponse>(
                Error.Validation("ProfitStatement.InvalidMonth", "Month must be between 1 and 12."));
        }

        var branchExists = await _context.Branches.AsNoTracking().AnyAsync(b => b.Id == query.BranchId, cancellationToken);
        if (!branchExists)
        {
            return Result.Failure<GetMonthlyProfitStatementResponse>(
                Error.NotFound("ProfitStatement.BranchNotFound", $"Branch '{query.BranchId}' was not found."));
        }

        var periodStartUtc = new DateTime(query.Year, query.Month, 1, 0, 0, 0, DateTimeKind.Utc);
        var periodEndUtc = periodStartUtc.AddMonths(1);

        var invoices = _context.SaleInvoices.AsNoTracking()
            .Where(s => s.BranchId == query.BranchId && s.Status != SaleInvoiceStatus.Voided
                        && s.CreatedAtUtc >= periodStartUtc && s.CreatedAtUtc < periodEndUtc);

        var salesAggregate = await invoices
            .GroupBy(_ => 1)
            .Select(g => new { TotalSales = g.Sum(s => s.TotalAmount), TotalReturnedAmount = g.Sum(s => s.TotalReturnedAmount) })
            .FirstOrDefaultAsync(cancellationToken);

        var totalSales = salesAggregate?.TotalSales ?? 0m;
        var totalReturnedAmount = salesAggregate?.TotalReturnedAmount ?? 0m;
        var netRevenue = totalSales - totalReturnedAmount;

        var items = _context.SaleInvoiceItems.AsNoTracking()
            .Join(invoices, i => i.SaleInvoiceId, s => s.Id, (i, s) => i);

        var costAggregate = await items
            .Where(i => i.UnitCostSnapshot != null)
            .GroupBy(_ => 1)
            .Select(g => new { CostOfGoodsSold = g.Sum(i => (i.Quantity - i.QuantityReturned) * i.UnitCostSnapshot!.Value) })
            .FirstOrDefaultAsync(cancellationToken);

        var costOfGoodsSold = costAggregate?.CostOfGoodsSold ?? 0m;
        var itemsExcludedNoCostHistory = await items.CountAsync(i => i.UnitCostSnapshot == null, cancellationToken);

        var grossProfit = netRevenue - costOfGoodsSold;

        var expensesByCategory = await _context.Expenses.AsNoTracking()
            .Where(e => e.BranchId == query.BranchId && e.PeriodYear == query.Year && e.PeriodMonth == query.Month)
            .GroupBy(e => e.Category)
            .Select(g => new ExpenseByCategoryDto(g.Key, g.Sum(e => e.Amount)))
            .ToListAsync(cancellationToken);

        var totalExpenses = expensesByCategory.Sum(e => e.Amount);

        var inventory = await GetInventoryVarianceAndWasteAsync(query.BranchId, periodStartUtc, periodEndUtc, cancellationToken);

        var netProfit = grossProfit - totalExpenses
            + inventory.StocktakeSurplusValue - inventory.StocktakeShortageValue - inventory.WasteLossValue;

        return Result.Success(new GetMonthlyProfitStatementResponse(
            query.BranchId, query.Year, query.Month,
            totalSales, totalReturnedAmount, netRevenue,
            costOfGoodsSold, itemsExcludedNoCostHistory, grossProfit,
            totalExpenses, expensesByCategory,
            inventory.StocktakeSurplusValue, inventory.StocktakeShortageValue, inventory.StocktakeExcludedNoCostHistory,
            inventory.WasteLossValue, inventory.WasteExcludedNoCostHistory,
            netProfit));
    }

    private sealed record InventoryValuation(
        decimal StocktakeSurplusValue, decimal StocktakeShortageValue, int StocktakeExcludedNoCostHistory,
        decimal WasteLossValue, int WasteExcludedNoCostHistory);

    private async Task<InventoryValuation> GetInventoryVarianceAndWasteAsync(
        Guid branchId, DateTime periodStartUtc, DateTime periodEndUtc, CancellationToken cancellationToken)
    {
        var movements = await _context.StockMovements.AsNoTracking()
            .Where(m => m.BranchId == branchId
                        && (m.MovementType == MovementType.StocktakeCorrectionIncrease
                            || m.MovementType == MovementType.StocktakeCorrectionDecrease
                            || (m.MovementType == MovementType.WasteOut && !m.IsReplacedBySupplier))
                        && m.OccurredAtUtc >= periodStartUtc && m.OccurredAtUtc < periodEndUtc)
            .Select(m => new { m.MovementType, m.ProductId, m.ProductBatchId, m.QuantityBase })
            .ToListAsync(cancellationToken);

        if (movements.Count == 0)
        {
            return new InventoryValuation(0m, 0m, 0, 0m, 0);
        }

        var batchIds = movements.Where(m => m.ProductBatchId is not null)
            .Select(m => m.ProductBatchId!.Value).Distinct().ToList();
        var batchUnitCosts = batchIds.Count == 0
            ? new Dictionary<Guid, decimal>()
            : await _context.ProductBatches.AsNoTracking()
                .Where(b => batchIds.Contains(b.Id))
                .Select(b => new { b.Id, b.UnitCost })
                .ToDictionaryAsync(b => b.Id, b => b.UnitCost, cancellationToken);

        var nonBatchProductIds = movements.Where(m => m.ProductBatchId is null)
            .Select(m => m.ProductId).Distinct().ToList();
        var weightedAverageCosts = nonBatchProductIds.Count == 0
            ? new Dictionary<Guid, decimal>()
            : await _context.PurchaseInvoiceItems.AsNoTracking()
                .Where(i => nonBatchProductIds.Contains(i.ProductId))
                .Join(
                    _context.PurchaseInvoices.AsNoTracking()
                        .Where(pi => pi.Status == PurchaseInvoiceStatus.Received && pi.CreatedAtUtc < periodEndUtc),
                    i => i.PurchaseInvoiceId, pi => pi.Id,
                    (i, pi) => new { i.ProductId, i.Quantity, i.UnitCost })
                .GroupBy(x => x.ProductId)
                .Select(g => new { ProductId = g.Key, TotalQuantity = g.Sum(x => x.Quantity), TotalCost = g.Sum(x => x.Quantity * x.UnitCost) })
                .ToDictionaryAsync(x => x.ProductId, x => x.TotalCost / x.TotalQuantity, cancellationToken);

        var surplusValue = 0m;
        var shortageValue = 0m;
        var stocktakeExcludedCount = 0;
        var wasteLossValue = 0m;
        var wasteExcludedCount = 0;

        foreach (var movement in movements)
        {
            decimal? unitCost = movement.ProductBatchId is { } batchId
                ? (batchUnitCosts.TryGetValue(batchId, out var batchCost) ? batchCost : null)
                : (weightedAverageCosts.TryGetValue(movement.ProductId, out var avgCost) ? avgCost : null);

            var isWaste = movement.MovementType == MovementType.WasteOut;

            if (unitCost is null)
            {
                if (isWaste)
                {
                    wasteExcludedCount++;
                }
                else
                {
                    stocktakeExcludedCount++;
                }
                continue;
            }

            var value = movement.QuantityBase * unitCost.Value;
            if (isWaste)
            {
                wasteLossValue += value;
            }
            else if (movement.MovementType == MovementType.StocktakeCorrectionIncrease)
            {
                surplusValue += value;
            }
            else
            {
                shortageValue += value;
            }
        }

        return new InventoryValuation(surplusValue, shortageValue, stocktakeExcludedCount, wasteLossValue, wasteExcludedCount);
    }
}

public sealed record GetMonthlyProfitStatementResponse(
    Guid BranchId,
    int Year,
    int Month,
    decimal TotalSales,
    decimal TotalReturnedAmount,
    decimal NetRevenue,
    decimal CostOfGoodsSold,
    int ItemsExcludedNoCostHistory,
    decimal GrossProfit,
    decimal TotalExpenses,
    IReadOnlyList<ExpenseByCategoryDto> ExpensesByCategory,
    decimal StocktakeSurplusValue,
    decimal StocktakeShortageValue,
    int StocktakeMovementsExcludedNoCostHistory,
    decimal WasteLossValue,
    int WasteMovementsExcludedNoCostHistory,
    decimal NetProfit);
