using Microsoft.EntityFrameworkCore;
using SupermarketSystem.Application.Common.Costing;
using SupermarketSystem.Application.Common.Interfaces;
using SupermarketSystem.Application.Common.Results;
using SupermarketSystem.Application.Common.Time;
using SupermarketSystem.Domain.Inventory;

namespace SupermarketSystem.Application.Inventory.GetComplimentaryMonthlySummary;

public sealed record GetComplimentaryMonthlySummaryQuery(Guid BranchId, int Year, int Month);

/// <summary>CostValue = null لما المنتج بلا تاريخ شراء (مستبعد من المجموع)؛ SellingValue = null لما مش مربوط بالفرع.</summary>
public sealed record ComplimentaryProductTotalDto(Guid ProductId, string ProductName, decimal QuantityBase, int IssueCount, decimal? CostValue, decimal? SellingValue);

public sealed record ComplimentaryUserTotalDto(Guid UserId, string FullName, int IssueCount, decimal CostValue);

/// <summary>شهر بالاتجاه: كم انصرف ضيافة (بالتكلفة) وكم مرة.</summary>
public sealed record ComplimentaryMonthTotalDto(int Year, int Month, int IssueCount, decimal CostValue);

public sealed record ComplimentaryMonthlySummaryDto(
    Guid BranchId, int Year, int Month,
    int IssueCount, int NeedsReviewCount,
    decimal TotalCostValue, decimal TotalSellingValue,
    int ExcludedNoCostHistory, int ExcludedNoSellingPrice,
    decimal PreviousMonthCostValue,
    IReadOnlyList<ComplimentaryProductTotalDto> ByProduct,
    IReadOnlyList<ComplimentaryUserTotalDto> ByUser,
    IReadOnlyList<ComplimentaryMonthTotalDto> Last12Months);

/// <summary>
/// مجموع الضيافة الشهرية كمال (30/9/2026، طلب صاحب المشروع "مجموع الضيافة الشهرية ك مال"): لفرع وشهر (بتوقيت المحل)
/// - بالتكلفة (**نفس رقم "الضيافة" بكشف الربح بالضبط** - نفس الحركات ونفس MovementUnitCosts)، وبسعر البيع الحالي
/// (شو كان ممكن يدخل لو انباعت - إعلامي بس، مش بالكشف)، واتجاه آخر 12 شهر، ومفصّل لكل صنف ولكل شخص سجّل.
/// </summary>
public sealed class GetComplimentaryMonthlySummaryHandler
{
    private readonly IApplicationDbContext _context;

    public GetComplimentaryMonthlySummaryHandler(IApplicationDbContext context)
    {
        _context = context;
    }

    public async Task<Result<ComplimentaryMonthlySummaryDto>> HandleAsync(GetComplimentaryMonthlySummaryQuery query, CancellationToken cancellationToken)
    {
        if (query.Month is < 1 or > 12 || query.Year is < 2000 or > 2100)
        {
            return Result.Failure<ComplimentaryMonthlySummaryDto>(Error.Validation("ComplimentarySummary.InvalidMonth", "الشهر مش صحيح."));
        }

        var businessTime = await BusinessTime.LoadAsync(_context, cancellationToken);
        var (startUtc, endUtc) = businessTime.MonthRangeUtc(query.Year, query.Month);
        var movements = await LoadMovementsAsync(query.BranchId, startUtc, endUtc, cancellationToken);
        var unitCostOf = await MovementUnitCosts.LoadAsync(
            _context, movements.Select(m => (m.ProductId, m.ProductBatchId)).ToList(), endUtc, cancellationToken);

        // آخر 12 شهر لحد الشهر المختار (كل شهر بتقييمه لحد نهايته - نفس كشف ربح ذاك الشهر).
        var last12Months = new List<ComplimentaryMonthTotalDto>();
        for (var back = 11; back >= 0; back--)
        {
            var monthIndex = query.Year * 12 + (query.Month - 1) - back;
            var (y, mo) = (monthIndex / 12, monthIndex % 12 + 1);
            last12Months.Add(back == 0
                ? new ComplimentaryMonthTotalDto(y, mo, movements.Count,
                    movements.Sum(m => m.QuantityBase * (unitCostOf(m.ProductId, m.ProductBatchId) ?? 0m)))
                : await MonthTotalAsync(query.BranchId, businessTime, y, mo, cancellationToken));
        }

        var previousCost = last12Months[^2].CostValue;

        var productIds = movements.Select(m => m.ProductId).Distinct().ToList();
        var productNames = await _context.Products.IgnoreQueryFilters().AsNoTracking()
            .Where(p => productIds.Contains(p.Id))
            .ToDictionaryAsync(p => p.Id, p => p.Name, cancellationToken);
        var sellingPrices = await _context.ProductBranches.IgnoreQueryFilters().AsNoTracking()
            .Where(pb => pb.BranchId == query.BranchId && productIds.Contains(pb.ProductId))
            .ToDictionaryAsync(pb => pb.ProductId, pb => pb.SellingPrice, cancellationToken);
        var userIds = movements.Select(m => m.UserId).Distinct().ToList();
        var userNames = await _context.Users.IgnoreQueryFilters().AsNoTracking()
            .Where(u => userIds.Contains(u.Id))
            .ToDictionaryAsync(u => u.Id, u => u.FullName, cancellationToken);

        var valued = movements.Select(m => new
        {
            m.ProductId,
            m.UserId,
            m.QuantityBase,
            m.NeedsReview,
            Cost = unitCostOf(m.ProductId, m.ProductBatchId) is { } unitCost ? m.QuantityBase * unitCost : (decimal?)null,
            Selling = sellingPrices.TryGetValue(m.ProductId, out var price) ? m.QuantityBase * price : (decimal?)null
        }).ToList();

        var byProduct = valued
            .GroupBy(v => v.ProductId)
            .Select(g => new ComplimentaryProductTotalDto(
                g.Key, productNames.GetValueOrDefault(g.Key, "(منتج محذوف)"), g.Sum(v => v.QuantityBase), g.Count(),
                g.Any(v => v.Cost is not null) ? g.Sum(v => v.Cost ?? 0m) : null,
                g.All(v => v.Selling is not null) ? g.Sum(v => v.Selling!.Value) : null))
            .OrderByDescending(p => p.CostValue ?? 0m).ThenBy(p => p.ProductName)
            .ToList();

        var byUser = valued
            .GroupBy(v => v.UserId)
            .Select(g => new ComplimentaryUserTotalDto(g.Key, userNames.GetValueOrDefault(g.Key, "(غير معروف)"), g.Count(), g.Sum(v => v.Cost ?? 0m)))
            .OrderByDescending(u => u.CostValue)
            .ToList();

        return Result.Success(new ComplimentaryMonthlySummaryDto(
            query.BranchId, query.Year, query.Month,
            valued.Count, valued.Count(v => v.NeedsReview),
            valued.Sum(v => v.Cost ?? 0m), valued.Sum(v => v.Selling ?? 0m),
            valued.Count(v => v.Cost is null), valued.Count(v => v.Selling is null),
            previousCost, byProduct, byUser, last12Months));
    }

    private async Task<ComplimentaryMonthTotalDto> MonthTotalAsync(
        Guid branchId, BusinessTime businessTime, int year, int month, CancellationToken cancellationToken)
    {
        var (startUtc, endUtc) = businessTime.MonthRangeUtc(year, month);
        var rows = await LoadMovementsAsync(branchId, startUtc, endUtc, cancellationToken);
        if (rows.Count == 0)
        {
            return new ComplimentaryMonthTotalDto(year, month, 0, 0m);
        }

        var unitCostOf = await MovementUnitCosts.LoadAsync(
            _context, rows.Select(m => (m.ProductId, m.ProductBatchId)).ToList(), endUtc, cancellationToken);
        return new ComplimentaryMonthTotalDto(year, month, rows.Count,
            rows.Sum(m => m.QuantityBase * (unitCostOf(m.ProductId, m.ProductBatchId) ?? 0m)));
    }

    private sealed record MovementRow(Guid ProductId, Guid? ProductBatchId, decimal QuantityBase, Guid UserId, bool NeedsReview);

    // نفس فلتر كشف الربح بالضبط: الفرع، ComplimentaryOut، [بداية الشهر، بداية الشهر الجاي).
    private Task<List<MovementRow>> LoadMovementsAsync(Guid branchId, DateTime startUtc, DateTime endUtc, CancellationToken cancellationToken) =>
        _context.StockMovements.AsNoTracking()
            .Where(m => m.BranchId == branchId && m.MovementType == MovementType.ComplimentaryOut
                        && m.OccurredAtUtc >= startUtc && m.OccurredAtUtc < endUtc)
            .Select(m => new MovementRow(m.ProductId, m.ProductBatchId, m.QuantityBase, m.UserId, m.NeedsReview))
            .ToListAsync(cancellationToken);
}
