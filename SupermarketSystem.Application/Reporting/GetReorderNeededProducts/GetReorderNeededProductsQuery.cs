using Microsoft.EntityFrameworkCore;
using SupermarketSystem.Application.Common.Interfaces;
using SupermarketSystem.Application.Common.Pagination;

namespace SupermarketSystem.Application.Reporting.GetReorderNeededProducts;

public sealed record GetReorderNeededProductsQuery(PagedRequest Paging, Guid BranchId);

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

        var candidates = _context.ProductBranches.AsNoTracking()
            .Where(pb => pb.BranchId == query.BranchId && pb.MinimumStock != null)
            .Join(_context.Stocks.AsNoTracking().Where(s => s.BranchId == query.BranchId),
                pb => pb.ProductId, s => s.ProductId,
                (pb, s) => new { pb, s })
            .Where(x => x.s.QuantityOnHand <= x.pb.MinimumStock!.Value);

        var totalCount = await candidates.CountAsync(cancellationToken);

        var page = await candidates
            .OrderBy(x => x.s.QuantityOnHand)
            .Skip(paging.Skip)
            .Take(paging.PageSize)
            .Join(_context.Products.AsNoTracking(),
                x => x.pb.ProductId, p => p.Id,
                (x, p) => new ReorderNeededItemDto(p.Id, p.Name, x.s.QuantityOnHand, x.pb.MinimumStock!.Value, x.pb.MaximumStock))
            .ToListAsync(cancellationToken);

        return new PagedResult<ReorderNeededItemDto>(page, totalCount, paging.PageNumber, paging.PageSize);
    }
}
