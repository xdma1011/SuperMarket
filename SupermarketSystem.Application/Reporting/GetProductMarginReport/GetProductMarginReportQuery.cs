using Microsoft.EntityFrameworkCore;
using SupermarketSystem.Application.Common.Interfaces;
using SupermarketSystem.Application.Common.Pagination;
using SupermarketSystem.Domain.Sales;

namespace SupermarketSystem.Application.Reporting.GetProductMarginReport;

public sealed record GetProductMarginReportQuery(PagedRequest Paging, Guid? BranchId, DateTime FromUtc, DateTime ToUtc);

public sealed record ProductMarginItemDto(
    Guid ProductId,
    string ProductName,
    decimal QuantitySold,
    decimal NetRevenue,
    decimal Cost,
    decimal Margin,
    decimal? MarginPercent,
    int LinesExcludedNoCostHistory);

public sealed record GetProductMarginReportResponse(
    PagedResult<ProductMarginItemDto> Items,
    decimal TotalNetRevenue,
    decimal TotalCost,
    decimal TotalMargin);

/// <summary>
/// هامش الربح لكل منتج بفترة معيّنة - أول استخدام لـUnitCostSnapshot
/// (17/9/2026) على مستوى المنتج، لا الفرع/الشهر الإجمالي بس (راجع
/// GetMonthlyProfitStatementQuery). طلب صاحب المشروع 21/9/2026.
///
/// ═══════════════════════════════════════════════════════════════════
/// تعريف كل رقم:
/// ═══════════════════════════════════════════════════════════════════
/// - QuantitySold: صافي الكمية المباعة (Quantity - QuantityReturned) لكل سطر.
/// - NetRevenue لكل سطر = LineTotal - (QuantityReturned × UnitPriceSnapshot) -
///   بالضبط نفس صيغة حساب مبلغ الاسترجاع الفعلية بـProcessReturnCommand
///   (`returnTotal = Quantity × UnitPriceSnapshot`)، لا تناسب تقريبي مخترَع.
/// - Cost لكل سطر = (Quantity - QuantityReturned) × UnitCostSnapshot - نفس
///   صيغة CostOfGoodsSold بـGetMonthlyProfitStatementQuery بالضبط. سطر بلا
///   UnitCostSnapshot **يُستبعَد كليًا** من Cost/Margin لهالمنتج (لا صفر) -
///   LinesExcludedNoCostHistory بيعلّم أي منتج هامشه هون غير مكتمل
///   (الهامش الحقيقي أقل من المعروض).
/// - Margin = NetRevenue - Cost. MarginPercent = Margin / NetRevenue × 100
///   (null لو NetRevenue صفر - تفاديًا للقسمة على صفر).
/// - الترتيب الافتراضي: الأعلى إيرادًا أولًا (نفس نمط GetCurrentCapitalValueQuery).
/// </summary>
public sealed class GetProductMarginReportHandler
{
    private readonly IApplicationDbContext _context;

    public GetProductMarginReportHandler(IApplicationDbContext context)
    {
        _context = context;
    }

    public async Task<GetProductMarginReportResponse> HandleAsync(
        GetProductMarginReportQuery query, CancellationToken cancellationToken)
    {
        var paging = query.Paging.Normalized();

        var invoices = _context.SaleInvoices.AsNoTracking()
            .Where(s => s.Status != SaleInvoiceStatus.Voided && s.CreatedAtUtc >= query.FromUtc && s.CreatedAtUtc < query.ToUtc);

        if (query.BranchId is { } branchId)
        {
            invoices = invoices.Where(s => s.BranchId == branchId);
        }

        var lines = _context.SaleInvoiceItems.AsNoTracking()
            .Join(invoices, i => i.SaleInvoiceId, s => s.Id, (i, s) => i);

        // الكمية بالوحدة الأساسية (29/9/2026): كرتونة (×10) + 5 حبات كانت تنعدّ "6" بدل 15.
        var baseQuantities = lines
            .Join(_context.ProductUnits.AsNoTracking(), i => i.ProductUnitId, u => u.Id,
                (i, u) => new { i.ProductId, BaseQuantity = (i.Quantity - i.QuantityReturned) * u.ConversionFactorToBase })
            .GroupBy(x => x.ProductId)
            .Select(g => new { ProductId = g.Key, QuantitySold = g.Sum(x => x.BaseQuantity) });

        var grouped = lines
            .GroupBy(i => i.ProductId)
            .Select(g => new
            {
                ProductId = g.Key,
                NetRevenue = g.Sum(i => i.LineTotal - (i.QuantityReturned * i.UnitPriceSnapshot)),
                Cost = g.Sum(i => i.UnitCostSnapshot == null ? 0m : (i.Quantity - i.QuantityReturned) * i.UnitCostSnapshot!.Value),
                LinesExcludedNoCostHistory = g.Sum(i => i.UnitCostSnapshot == null ? 1 : 0)
            });

        var totalCount = await grouped.CountAsync(cancellationToken);

        var totals = await grouped
            .GroupBy(_ => 1)
            .Select(g => new { TotalNetRevenue = g.Sum(x => x.NetRevenue), TotalCost = g.Sum(x => x.Cost) })
            .FirstOrDefaultAsync(cancellationToken);

        var pageRows = await grouped
            .OrderByDescending(x => x.NetRevenue)
            .Skip(paging.Skip)
            .Take(paging.PageSize)
            .Join(_context.Products.AsNoTracking(), x => x.ProductId, p => p.Id,
                (x, p) => new { x.ProductId, p.Name, x.NetRevenue, x.Cost, x.LinesExcludedNoCostHistory })
            .ToListAsync(cancellationToken);

        var pageProductIds = pageRows.Select(r => r.ProductId).ToList();
        var quantities = await baseQuantities
            .Where(q => pageProductIds.Contains(q.ProductId))
            .ToDictionaryAsync(q => q.ProductId, q => q.QuantitySold, cancellationToken);

        var page = pageRows.Select(r => new ProductMarginItemDto(
                r.ProductId,
                r.Name,
                quantities.GetValueOrDefault(r.ProductId),
                r.NetRevenue,
                r.Cost,
                r.NetRevenue - r.Cost,
                r.NetRevenue == 0 ? null : (r.NetRevenue - r.Cost) / r.NetRevenue * 100m,
                r.LinesExcludedNoCostHistory))
            .ToList();

        var pagedItems = new PagedResult<ProductMarginItemDto>(page, totalCount, paging.PageNumber, paging.PageSize);

        var totalNetRevenue = totals?.TotalNetRevenue ?? 0m;
        var totalCost = totals?.TotalCost ?? 0m;

        return new GetProductMarginReportResponse(pagedItems, totalNetRevenue, totalCost, totalNetRevenue - totalCost);
    }
}
