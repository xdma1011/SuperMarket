using Microsoft.EntityFrameworkCore;
using SupermarketSystem.Application.Common.Interfaces;
using SupermarketSystem.Application.Common.Results;
using SupermarketSystem.Domain.Finance;
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
/// - NetProfit = GrossProfit - TotalExpenses. هذا الرقم النهائي "ربح الشهر".
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
        var netProfit = grossProfit - totalExpenses;

        return Result.Success(new GetMonthlyProfitStatementResponse(
            query.BranchId, query.Year, query.Month,
            totalSales, totalReturnedAmount, netRevenue,
            costOfGoodsSold, itemsExcludedNoCostHistory, grossProfit,
            totalExpenses, expensesByCategory, netProfit));
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
    decimal NetProfit);
