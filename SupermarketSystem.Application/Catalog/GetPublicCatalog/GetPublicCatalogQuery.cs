using Microsoft.EntityFrameworkCore;
using SupermarketSystem.Application.Common.Interfaces;
using SupermarketSystem.Application.Common.Pagination;
using SupermarketSystem.Application.Common.Policies;
using SupermarketSystem.Domain.Catalog;

namespace SupermarketSystem.Application.Catalog.GetPublicCatalog;

public sealed record GetPublicCatalogQuery(Guid BranchId, Guid? CategoryId, PagedRequest Paging);

public sealed record PublicCatalogItemDto(
    Guid ProductId, string Name, string? Description, string CategoryName,
    decimal Price, string? PrimaryImageUrl, Guid BaseUnitId, string BaseUnitName);

/// <summary>
/// كتالوج تصفّح عام لتطبيق الزبائن - بلا تحقق هوية (تصفّح لا يحتاج
/// تسجيل دخول، راجع GetPublicBranchesHandler لنفس المبدأ). يطبّق قاعدة
/// "إخفاء المخزون القليل" (راجع نقاش صاحب المشروع وOrderingPolicyKeys):
/// منتج بمخزون أقل من الحد الأدنى المناسب لفئته السعرية ما يظهر أصلًا،
/// لتفادي زبون يطلب صنف والكمية الفعلية تخلص قبل ما توصل الفاتورة
/// الحقيقية للكاشير.
/// </summary>
public sealed class GetPublicCatalogHandler
{
    private readonly IApplicationDbContext _context;
    private readonly ISettingsProvider _settingsProvider;

    public GetPublicCatalogHandler(IApplicationDbContext context, ISettingsProvider settingsProvider)
    {
        _context = context;
        _settingsProvider = settingsProvider;
    }

    public async Task<PagedResult<PublicCatalogItemDto>> HandleAsync(GetPublicCatalogQuery query, CancellationToken cancellationToken)
    {
        var paging = query.Paging.Normalized();

        var highPriceThreshold = await _settingsProvider.GetDecimalAsync(OrderingPolicyKeys.VisibilityHighPriceThreshold, 1.0m, cancellationToken);
        var lowPriceThreshold = await _settingsProvider.GetDecimalAsync(OrderingPolicyKeys.VisibilityLowPriceThreshold, 0.5m, cancellationToken);
        var minStockHigh = await _settingsProvider.GetDecimalAsync(OrderingPolicyKeys.MinVisibleStockHighPrice, 7m, cancellationToken);
        var minStockMid = await _settingsProvider.GetDecimalAsync(OrderingPolicyKeys.MinVisibleStockMidPrice, 5m, cancellationToken);
        var minStockLow = await _settingsProvider.GetDecimalAsync(OrderingPolicyKeys.MinVisibleStockLowPrice, 30m, cancellationToken);

        var productsQuery =
            from product in _context.Products.AsNoTracking()
            join branch in _context.ProductBranches.AsNoTracking() on product.Id equals branch.ProductId
            join category in _context.ProductCategories.AsNoTracking() on product.CategoryId equals category.Id
            where !product.IsDeleted && product.Status == ProductStatus.Active
                  && branch.BranchId == query.BranchId && branch.IsAvailableForSale
            select new { Product = product, Branch = branch, CategoryName = category.Name };

        if (query.CategoryId is { } categoryId)
        {
            productsQuery = productsQuery.Where(x => x.Product.CategoryId == categoryId);
        }

        if (!string.IsNullOrWhiteSpace(paging.Search))
        {
            var pattern = $"%{paging.Search.Trim()}%";
            productsQuery = productsQuery.Where(x => EF.Functions.Like(x.Product.Name, pattern));
        }

        var withStock =
            from x in productsQuery
            let stockOnHand = _context.Stocks.AsNoTracking()
                .Where(s => s.ProductId == x.Product.Id && s.BranchId == query.BranchId)
                .Sum(s => (decimal?)s.QuantityOnHand) ?? 0
            select new { x.Product, x.Branch, x.CategoryName, StockOnHand = stockOnHand };

        var visible = withStock.Where(x =>
            x.Branch.SellingPrice > highPriceThreshold ? x.StockOnHand >= minStockHigh :
            x.Branch.SellingPrice >= lowPriceThreshold ? x.StockOnHand >= minStockMid :
            x.StockOnHand >= minStockLow);

        var totalCount = await visible.CountAsync(cancellationToken);

        var page = paging.IsDescending
            ? visible.OrderByDescending(x => x.Product.Name)
            : visible.OrderBy(x => x.Product.Name);

        var pageResults = await page
            .Skip(paging.Skip)
            .Take(paging.PageSize)
            .Select(x => new
            {
                x.Product.Id,
                x.Product.Name,
                x.Product.Description,
                x.CategoryName,
                x.Branch.SellingPrice,
                PrimaryImageUrl = x.Product.Images.Where(i => i.IsPrimary).Select(i => i.Url).FirstOrDefault(),
                BaseUnit = x.Product.Units.Where(u => u.IsBaseUnit).Select(u => new { u.Id, u.UnitName }).FirstOrDefault()
            })
            .ToListAsync(cancellationToken);

        var items = pageResults
            .Select(x => new PublicCatalogItemDto(
                x.Id, x.Name, x.Description, x.CategoryName, x.SellingPrice, x.PrimaryImageUrl,
                x.BaseUnit?.Id ?? Guid.Empty, x.BaseUnit?.UnitName ?? ""))
            .ToList();

        return new PagedResult<PublicCatalogItemDto>(items, totalCount, paging.PageNumber, paging.PageSize);
    }
}
