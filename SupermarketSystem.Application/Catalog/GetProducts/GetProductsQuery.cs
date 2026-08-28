using Microsoft.EntityFrameworkCore;
using SupermarketSystem.Application.Common.Interfaces;
using SupermarketSystem.Application.Common.Pagination;
using SupermarketSystem.Domain.Catalog;

namespace SupermarketSystem.Application.Catalog.GetProducts;

public sealed record GetProductsQuery(PagedRequest Paging, Guid? CategoryId);

public sealed record ProductListItemDto(
    Guid Id,
    string Name,
    Guid CategoryId,
    ProductStatus Status,
    bool IsBatchTracked,
    decimal? SuggestedRetailPrice,
    int? ExpectedShelfLifeDays,
    bool IsComplimentaryAllowed,
    DateTime CreatedAtUtc);

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
            var pattern = $"%{paging.Search.Trim()}%";
            products = products.Where(p => EF.Functions.Like(p.Name, pattern));
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

        return new PagedResult<ProductListItemDto>(items, totalCount, paging.PageNumber, paging.PageSize);
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
