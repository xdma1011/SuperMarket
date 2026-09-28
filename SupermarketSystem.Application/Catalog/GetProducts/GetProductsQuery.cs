using Microsoft.EntityFrameworkCore;
using SupermarketSystem.Application.Common.Interfaces;
using SupermarketSystem.Application.Common.Pagination;
using SupermarketSystem.Domain.Catalog;

namespace SupermarketSystem.Application.Catalog.GetProducts;

/// <summary>BranchId (اختياري، 28/9/2026): بيعبّي سعر الفرع الفعلي والرصيد لكل منتج بقائمة الكتالوج.</summary>
public sealed record GetProductsQuery(PagedRequest Paging, Guid? CategoryId, Guid? BranchId = null);

public sealed record ProductListItemDto(
    Guid Id,
    string Name,
    Guid CategoryId,
    ProductStatus Status,
    bool IsBatchTracked,
    decimal? SuggestedRetailPrice,
    int? ExpectedShelfLifeDays,
    bool IsComplimentaryAllowed,
    DateTime CreatedAtUtc)
{
    /// <summary>أول باركود (للعرض)، وكم باركود إله كامل.</summary>
    public string? PrimaryBarcode { get; init; }
    public int BarcodeCount { get; init; }

    /// <summary>بالفرع المطلوب (BranchId): null = المنتج مش مربوط بالفرع (ما بيبين بالكاشير - §1.8).</summary>
    public decimal? BranchSellingPrice { get; init; }
    public bool? IsAvailableAtBranch { get; init; }

    /// <summary>الرصيد بالوحدة الأساسية بالفرع (مجموع الدفعات) - null = بلا فرع مطلوب.</summary>
    public decimal? StockOnHand { get; init; }
}

/// <summary>
/// Read-only. AsNoTracking + database-side filter/sort/page, per brief §17 —
/// never loaded into memory and paged there. Ordering is always deterministic
/// (Name then Id as a tiebreaker) so paging never skips or repeats a row when
/// two products share a sort value, per brief §18.
/// </summary>
public sealed class GetProductsHandler
{
    private readonly IApplicationDbContext _context;

    public GetProductsHandler(IApplicationDbContext context)
    {
        _context = context;
    }

    public async Task<PagedResult<ProductListItemDto>> HandleAsync(GetProductsQuery query, CancellationToken cancellationToken)
    {
        var paging = query.Paging.Normalized();

        var products = _context.Products.AsNoTracking().AsQueryable();

        if (query.CategoryId is { } categoryId)
        {
            products = products.Where(p => p.CategoryId == categoryId);
        }

        if (!string.IsNullOrWhiteSpace(paging.Search))
        {
            // بالاسم أو الباركود (بحث الكتالوج، 28/9/2026 - كان بالاسم بس).
            var pattern = $"%{paging.Search.Trim()}%";
            products = products.Where(p => EF.Functions.Like(p.Name, pattern)
                || _context.ProductBarcodes.Any(b => b.ProductId == p.Id && EF.Functions.Like(b.BarcodeValue, pattern)));
        }

        products = ApplySort(products, paging);

        var totalCount = await products.CountAsync(cancellationToken);

        var items = await products
            .Skip(paging.Skip)
            .Take(paging.PageSize)
            .Select(p => new ProductListItemDto(
                p.Id,
                p.Name,
                p.CategoryId,
                p.Status,
                p.IsBatchTracked,
                p.SuggestedRetailPrice,
                p.ExpectedShelfLifeDays,
                p.IsComplimentaryAllowed,
                p.CreatedAtUtc))
            .ToListAsync(cancellationToken);

        items = await EnrichAsync(items, query.BranchId, cancellationToken);
        return new PagedResult<ProductListItemDto>(items, totalCount, paging.PageNumber, paging.PageSize);
    }

    /// <summary>باركود + سعر الفرع + الرصيد لمنتجات الصفحة بس (استعلام لكل نوع، مش لكل منتج).</summary>
    private async Task<List<ProductListItemDto>> EnrichAsync(List<ProductListItemDto> items, Guid? branchId, CancellationToken cancellationToken)
    {
        if (items.Count == 0)
        {
            return items;
        }

        var ids = items.Select(i => i.Id).ToList();
        var barcodes = (await _context.ProductBarcodes.AsNoTracking()
                .Where(b => ids.Contains(b.ProductId))
                .Select(b => new { b.ProductId, b.BarcodeValue })
                .ToListAsync(cancellationToken))
            .GroupBy(b => b.ProductId)
            .ToDictionary(g => g.Key, g => (First: g.Select(b => b.BarcodeValue).OrderBy(v => v).First(), Count: g.Count()));

        Dictionary<Guid, (decimal Price, bool Available)> branchPrices = new();
        Dictionary<Guid, decimal> stock = new();
        if (branchId is { } branch)
        {
            branchPrices = await _context.ProductBranches.AsNoTracking()
                .Where(pb => pb.BranchId == branch && ids.Contains(pb.ProductId))
                .ToDictionaryAsync(pb => pb.ProductId, pb => (pb.SellingPrice, pb.IsAvailableForSale), cancellationToken);
            stock = await _context.Stocks.AsNoTracking()
                .Where(s => s.BranchId == branch && ids.Contains(s.ProductId))
                .GroupBy(s => s.ProductId)
                .Select(g => new { ProductId = g.Key, Quantity = g.Sum(s => s.QuantityOnHand) })
                .ToDictionaryAsync(x => x.ProductId, x => x.Quantity, cancellationToken);
        }

        return items.Select(i => i with
        {
            PrimaryBarcode = barcodes.TryGetValue(i.Id, out var b) ? b.First : null,
            BarcodeCount = barcodes.TryGetValue(i.Id, out var bc) ? bc.Count : 0,
            BranchSellingPrice = branchPrices.TryGetValue(i.Id, out var price) ? price.Price : null,
            IsAvailableAtBranch = branchId is null ? null : branchPrices.TryGetValue(i.Id, out var avail) && avail.Available,
            StockOnHand = branchId is null ? null : stock.GetValueOrDefault(i.Id)
        }).ToList();
    }

    private static IQueryable<Product> ApplySort(IQueryable<Product> products, PagedRequest paging)
    {
        return paging.SortBy?.ToLowerInvariant() switch
        {
            "createdatutc" => paging.IsDescending
                ? products.OrderByDescending(p => p.CreatedAtUtc).ThenByDescending(p => p.Id)
                : products.OrderBy(p => p.CreatedAtUtc).ThenBy(p => p.Id),
            _ => paging.IsDescending
                ? products.OrderByDescending(p => p.Name).ThenByDescending(p => p.Id)
                : products.OrderBy(p => p.Name).ThenBy(p => p.Id)
        };
    }
}
