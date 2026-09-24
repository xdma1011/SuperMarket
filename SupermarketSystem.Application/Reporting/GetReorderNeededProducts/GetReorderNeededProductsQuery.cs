using Microsoft.EntityFrameworkCore;
using SupermarketSystem.Application.Common.Interfaces;
using SupermarketSystem.Application.Common.Pagination;

namespace SupermarketSystem.Application.Reporting.GetReorderNeededProducts;

// BranchId اختياري: الرئيسية بتطلب العدد لكل الفروع (كانت بترجع 500 لأنها ما بتبعت فرع).
public sealed record GetReorderNeededProductsQuery(PagedRequest Paging, Guid? BranchId);

public sealed record ReorderNeededItemDto(
    Guid ProductId,
    string ProductName,
    decimal CurrentStock,
    decimal MinimumStock,
    decimal? MaximumStock);

/// <summary>
/// منتجات رصيدها الحالي (Stock.QuantityOnHand) أقل من أو يساوي الحد
/// الأدنى المحدَّد بـProductBranch.MinimumStock — الحقل موجود أصلًا من
/// Phase C، هذا التقرير أول استخدام فعلي له. منتج بلا MinimumStock محدَّد
/// (null) مستثنى تلقائيًا — ما في حد نقيس عليه.
/// </summary>
public sealed class GetReorderNeededProductsHandler
{
    private readonly IApplicationDbContext _context;

    public GetReorderNeededProductsHandler(IApplicationDbContext context)
    {
        _context = context;
    }

    public async Task<PagedResult<ReorderNeededItemDto>> HandleAsync(
        GetReorderNeededProductsQuery query, CancellationToken cancellationToken)
    {
        var paging = query.Paging.Normalized();

        // الرصيد مجموع كل صفوف Stock للمنتج بالفرع - منتج متتبَّع دفعات إله صف لكل دفعة، وكانت
        // كل دفعة بتنقارن بالحد الأدنى لحالها (دفعتين 5 + 5 وحد 8 = يطلع مرتين "بدو طلب" وهو 10).
        var stockTotals = _context.Stocks.AsNoTracking()
            .Where(s => query.BranchId == null || s.BranchId == query.BranchId)
            .GroupBy(s => new { s.ProductId, s.BranchId })
            .Select(g => new { g.Key.ProductId, g.Key.BranchId, QuantityOnHand = g.Sum(s => s.QuantityOnHand) });

        var candidates = _context.ProductBranches.AsNoTracking()
            .Where(pb => pb.MinimumStock != null && (query.BranchId == null || pb.BranchId == query.BranchId))
            .Join(stockTotals,
                pb => new { pb.ProductId, pb.BranchId }, s => new { s.ProductId, s.BranchId },
                (pb, s) => new { pb.ProductId, MinimumStock = pb.MinimumStock!.Value, pb.MaximumStock, s.QuantityOnHand })
            .Where(x => x.QuantityOnHand <= x.MinimumStock);

        var totalCount = await candidates.CountAsync(cancellationToken);

        var page = await candidates
            .OrderBy(x => x.QuantityOnHand)
            .Skip(paging.Skip)
            .Take(paging.PageSize)
            .Join(_context.Products.AsNoTracking(),
                x => x.ProductId, p => p.Id,
                (x, p) => new ReorderNeededItemDto(p.Id, p.Name, x.QuantityOnHand, x.MinimumStock, x.MaximumStock))
            .ToListAsync(cancellationToken);

        return new PagedResult<ReorderNeededItemDto>(page, totalCount, paging.PageNumber, paging.PageSize);
    }
}
