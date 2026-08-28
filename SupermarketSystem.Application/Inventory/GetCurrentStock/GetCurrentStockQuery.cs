using Microsoft.EntityFrameworkCore;
using SupermarketSystem.Application.Common.Interfaces;
using SupermarketSystem.Application.Common.Pagination;

namespace SupermarketSystem.Application.Inventory.GetCurrentStock;

public sealed record GetCurrentStockQuery(PagedRequest Paging, Guid? BranchId);

public sealed record CurrentStockItemDto(
    Guid ProductId,
    string ProductName,
    string CategoryName,
    Guid BranchId,
    string BranchName,
    decimal QuantityOnHand,
    string BaseUnitName);

/// <summary>
/// كانت ناقصة بالكامل — صفر شاشة تعرض "كم عندي من كل صنف". Stock
/// بيتجمّع هون على مستوى (منتج، فرع) — أي منتج مقسَّم لدفعات متعددة
/// بيظهر كصف واحد بمجموع كمياته.
/// </summary>
public sealed class GetCurrentStockHandler
{
    private readonly IApplicationDbContext _context;

    public GetCurrentStockHandler(IApplicationDbContext context)
    {
        _context = context;
    }

    public async Task<PagedResult<CurrentStockItemDto>> HandleAsync(GetCurrentStockQuery query, CancellationToken cancellationToken)
    {
        var paging = query.Paging.Normalized();

        var grouped = _context.Stocks.AsNoTracking()
            .Where(s => query.BranchId == null || s.BranchId == query.BranchId)
            .GroupBy(s => new { s.ProductId, s.BranchId })
            .Select(g => new
            {
                g.Key.ProductId,
                g.Key.BranchId,
                QuantityOnHand = g.Sum(s => s.QuantityOnHand)
            });

        var joined = grouped
            .Join(_context.Products.AsNoTracking(), s => s.ProductId, p => p.Id,
                (s, p) => new { s.ProductId, s.BranchId, s.QuantityOnHand, p.Name, p.CategoryId })
            .Join(_context.ProductCategories.AsNoTracking(), x => x.CategoryId, c => c.Id,
                (x, c) => new { x.ProductId, x.BranchId, x.QuantityOnHand, ProductName = x.Name, CategoryName = c.Name })
            .Join(_context.Branches.AsNoTracking(), x => x.BranchId, b => b.Id,
                (x, b) => new { x.ProductId, x.BranchId, x.QuantityOnHand, x.ProductName, x.CategoryName, BranchName = b.Name });

        if (!string.IsNullOrWhiteSpace(paging.Search))
        {
            var pattern = $"%{paging.Search.Trim()}%";
            joined = joined.Where(x => EF.Functions.Like(x.ProductName, pattern));
        }

        joined = paging.IsDescending
            ? joined.OrderByDescending(x => x.ProductName).ThenByDescending(x => x.BranchName)
            : joined.OrderBy(x => x.ProductName).ThenBy(x => x.BranchName);

        var totalCount = await joined.CountAsync(cancellationToken);

        var page = await joined
            .Skip(paging.Skip)
            .Take(paging.PageSize)
            .ToListAsync(cancellationToken);

        var productIds = page.Select(x => x.ProductId).Distinct().ToList();

        var baseUnitByProduct = await _context.ProductUnits.AsNoTracking()
            .Where(u => productIds.Contains(u.ProductId) && u.IsBaseUnit)
            .ToDictionaryAsync(u => u.ProductId, u => u.UnitName, cancellationToken);

        var items = page.Select(x => new CurrentStockItemDto(
            x.ProductId, x.ProductName, x.CategoryName, x.BranchId, x.BranchName,
            x.QuantityOnHand, baseUnitByProduct.GetValueOrDefault(x.ProductId, "—")))
            .ToList();

        return new PagedResult<CurrentStockItemDto>(items, totalCount, paging.PageNumber, paging.PageSize);
    }
}
