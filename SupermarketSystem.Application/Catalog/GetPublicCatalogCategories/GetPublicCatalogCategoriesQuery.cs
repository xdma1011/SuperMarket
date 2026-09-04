using Microsoft.EntityFrameworkCore;
using SupermarketSystem.Application.Common.Interfaces;
using SupermarketSystem.Domain.Catalog;

namespace SupermarketSystem.Application.Catalog.GetPublicCatalogCategories;

public sealed record GetPublicCatalogCategoriesQuery(Guid BranchId);

public sealed record PublicCategoryDto(Guid Id, string Name);

/// <summary>يرجّع بس التصنيفات اللي فيها منتج واحد على الأقل متاح للبيع بهاي الفرع - لا كل التصنيفات المعرَّفة إداريًا (بعضها ممكن يكون فاضي تمامًا).</summary>
public sealed class GetPublicCatalogCategoriesHandler
{
    private readonly IApplicationDbContext _context;

    public GetPublicCatalogCategoriesHandler(IApplicationDbContext context)
    {
        _context = context;
    }

    public async Task<IReadOnlyList<PublicCategoryDto>> HandleAsync(GetPublicCatalogCategoriesQuery query, CancellationToken cancellationToken)
    {
        return await (
            from category in _context.ProductCategories.AsNoTracking()
            where !category.IsDeleted
            where _context.Products.AsNoTracking()
                .Join(_context.ProductBranches.AsNoTracking(), p => p.Id, b => b.ProductId, (p, b) => new { p, b })
                .Any(x => x.p.CategoryId == category.Id && !x.p.IsDeleted && x.p.Status == ProductStatus.Active
                          && x.b.BranchId == query.BranchId && x.b.IsAvailableForSale)
            orderby category.Name
            select new PublicCategoryDto(category.Id, category.Name))
            .ToListAsync(cancellationToken);
    }
}
