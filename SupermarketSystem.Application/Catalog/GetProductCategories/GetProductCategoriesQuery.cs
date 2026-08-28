using Microsoft.EntityFrameworkCore;
using SupermarketSystem.Application.Common.Interfaces;
using SupermarketSystem.Application.Common.Pagination;

namespace SupermarketSystem.Application.Catalog.GetProductCategories;

public sealed record GetProductCategoriesQuery(PagedRequest Paging);

public sealed record ProductCategoryListItemDto(Guid Id, string Name, Guid? ParentCategoryId);

public sealed class GetProductCategoriesHandler
{
    private readonly IApplicationDbContext _context;

    public GetProductCategoriesHandler(IApplicationDbContext context)
    {
        _context = context;
    }

    public async Task<PagedResult<ProductCategoryListItemDto>> HandleAsync(
        GetProductCategoriesQuery query,
        CancellationToken cancellationToken)
    {
        var paging = query.Paging.Normalized();

        var categories = _context.ProductCategories.AsNoTracking().AsQueryable();

        if (!string.IsNullOrWhiteSpace(paging.Search))
        {
            var pattern = $"%{paging.Search.Trim()}%";
            categories = categories.Where(c => EF.Functions.Like(c.Name, pattern));
        }

        categories = paging.IsDescending
            ? categories.OrderByDescending(c => c.Name).ThenByDescending(c => c.Id)
            : categories.OrderBy(c => c.Name).ThenBy(c => c.Id);

        var totalCount = await categories.CountAsync(cancellationToken);

        var items = await categories
            .Skip(paging.Skip)
            .Take(paging.PageSize)
            .Select(c => new ProductCategoryListItemDto(c.Id, c.Name, c.ParentCategoryId))
            .ToListAsync(cancellationToken);

        return new PagedResult<ProductCategoryListItemDto>(items, totalCount, paging.PageNumber, paging.PageSize);
    }
}
