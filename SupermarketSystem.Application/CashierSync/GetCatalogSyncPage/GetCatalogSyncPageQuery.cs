using Microsoft.EntityFrameworkCore;
using SupermarketSystem.Application.Common.Interfaces;
using SupermarketSystem.Application.Common.Pagination;

namespace SupermarketSystem.Application.CashierSync.GetCatalogSyncPage;

public sealed record GetCatalogSyncPageQuery(Guid BranchId, int PageNumber, int PageSize);

public sealed record CatalogSyncUnitDto(
    Guid UnitId, string UnitName, decimal ConversionFactorToBase, bool IsBaseUnit, IReadOnlyList<string> Barcodes);

/// <summary>QuantityAvailable = مجموع Stock.QuantityOnHand لهذه الدفعة بهذا الفرع - دفعة نفدت كميتها ما بترجع أصلًا.</summary>
public sealed record CatalogSyncBatchDto(
    Guid BatchId, string BatchNumber, DateOnly? ExpiryDate, decimal QuantityAvailable);

/// <summary>عرض كمية شغّال الآن بهذا الفرع - يُطبَّق فقط لما البيع يصير بالوحدة الأساسية (نفس PROMOTION ASSUMPTION بـCompleteSaleCommand). للتقدير المحلي الأوفلاين فقط - الحساب النهائي الملزِم سيرفر-سايد دايمًا وقت CompleteSale.</summary>
public sealed record CatalogSyncPromotionDto(
    Guid PromotionId, string Title, int BundleQuantity, decimal BundlePrice, decimal? MaxQuantityPerInvoice);

public sealed record CatalogSyncProductDto(
    Guid ProductId,
    string Name,
    Guid CategoryId,
    string CategoryName,
    decimal SellingPrice,
    bool IsAvailableForSale,
    bool IsBatchTracked,
    IReadOnlyList<CatalogSyncUnitDto> Units,
    IReadOnlyList<CatalogSyncBatchDto> Batches,
    CatalogSyncPromotionDto? ActivePromotion);

/// <summary>
/// كل شي يحتاجه الكاشير الأوفلاين ليبيع صنف — الاسم، السعر بهذا الفرع،
/// كل وحداته وباركوداتها، **وكل دفعاته المتوفرة فعليًا** لو
/// IsBatchTracked=true (كانت ناقصة — البيع الأوفلاين لمنتجات الدفعات
/// كان مستحيلًا تقنيًا بلا هالمعلومة، لأن CompleteSaleCommand.Items
/// يتطلب ProductBatchId صريح).
///
/// مقسَّم بصفحات على مستوى المنتج. الترتيب بالدفعات (FIFO حسب تاريخ
/// الصلاحية الأقرب) يصير بالمتحكِّم بالكاشير، لا هون.
/// </summary>
public sealed class GetCatalogSyncPageHandler
{
    private readonly IApplicationDbContext _context;
    private readonly IDateTimeProvider _dateTimeProvider;

    public GetCatalogSyncPageHandler(IApplicationDbContext context, IDateTimeProvider dateTimeProvider)
    {
        _context = context;
        _dateTimeProvider = dateTimeProvider;
    }

    public async Task<PagedResult<CatalogSyncProductDto>> HandleAsync(
        GetCatalogSyncPageQuery query, CancellationToken cancellationToken)
    {
        var pageNumber = query.PageNumber < 1 ? 1 : query.PageNumber;
        var pageSize = query.PageSize is < 1 or > 500 ? 200 : query.PageSize;

        var branchProducts = _context.ProductBranches.AsNoTracking()
            .Where(pb => pb.BranchId == query.BranchId)
            .OrderBy(pb => pb.ProductId);

        var totalCount = await branchProducts.CountAsync(cancellationToken);

        var page = await branchProducts
            .Skip((pageNumber - 1) * pageSize)
            .Take(pageSize)
            .Join(_context.Products.AsNoTracking(), pb => pb.ProductId, p => p.Id,
                (pb, p) => new { pb, p })
            .Join(_context.ProductCategories.AsNoTracking(), x => x.p.CategoryId, c => c.Id,
                (x, c) => new { x.pb, x.p, CategoryName = c.Name })
            .ToListAsync(cancellationToken);

        var productIds = page.Select(x => x.p.Id).ToList();

        var units = await _context.ProductUnits.AsNoTracking()
            .Where(u => productIds.Contains(u.ProductId))
            .ToListAsync(cancellationToken);

        var unitIds = units.Select(u => u.Id).ToList();

        var barcodesByUnit = await _context.ProductBarcodes.AsNoTracking()
            .Where(b => unitIds.Contains(b.ProductUnitId))
            .GroupBy(b => b.ProductUnitId)
            .Select(g => new { UnitId = g.Key, Values = g.Select(b => b.BarcodeValue).ToList() })
            .ToDictionaryAsync(x => x.UnitId, x => x.Values, cancellationToken);

        // دفعات هذا الفرع بس، لهذه المنتجات بس، مع كميتها الفعلية —
        // دفعة نفدت كميتها (QuantityOnHand <= 0) ما بترجع، الكاشير ما
        // لازم يعرض دفعة فاضية للاختيار.
        var batchesRaw = await _context.ProductBatches.AsNoTracking()
            .Where(b => b.BranchId == query.BranchId && productIds.Contains(b.ProductId))
            .Join(_context.Stocks.AsNoTracking().Where(s => s.BranchId == query.BranchId),
                b => b.Id, s => s.ProductBatchId,
                (b, s) => new { b.Id, b.ProductId, b.BatchNumber, b.ExpiryDate, s.QuantityOnHand })
            .ToListAsync(cancellationToken);

        var batchesByProduct = batchesRaw
            .GroupBy(b => b.ProductId)
            .Select(g => new
            {
                ProductId = g.Key,
                Batches = g.GroupBy(b => new { b.Id, b.BatchNumber, b.ExpiryDate })
                    .Select(bg => new CatalogSyncBatchDto(bg.Key.Id, bg.Key.BatchNumber, bg.Key.ExpiryDate, bg.Sum(x => x.QuantityOnHand)))
                    .Where(dto => dto.QuantityAvailable > 0)
                    .ToList()
            })
            .ToDictionary(x => x.ProductId, x => (IReadOnlyList<CatalogSyncBatchDto>)x.Batches);

        // نفس منطق CompleteSaleCommand بالضبط (PROMOTION ASSUMPTION) - تقدير
        // محلي للكاشير الأوفلاين بس، الحساب الملزِم سيرفر-سايد وقت البيع الفعلي.
        var nowUtc = _dateTimeProvider.UtcNow;
        var activePromotionByProduct = await _context.Promotions.AsNoTracking()
            .Where(p => productIds.Contains(p.ProductId) && p.StartAtUtc <= nowUtc && p.EndAtUtc >= nowUtc)
            .Join(_context.PromotionBranches.AsNoTracking().Where(pb => pb.BranchId == query.BranchId && pb.IsActive),
                p => p.Id, pb => pb.PromotionId,
                (p, pb) => new { p.Id, p.ProductId, p.Title, p.BundleQuantity, p.BundlePrice, p.MaxQuantityPerInvoice, p.CreatedAtUtc })
            .ToListAsync(cancellationToken);

        var promotionByProduct = activePromotionByProduct
            .GroupBy(x => x.ProductId)
            .ToDictionary(g => g.Key, g => g.OrderBy(x => x.CreatedAtUtc).First());

        var items = page.Select(x =>
        {
            promotionByProduct.TryGetValue(x.p.Id, out var promo);
            return new CatalogSyncProductDto(
                x.p.Id, x.p.Name, x.p.CategoryId, x.CategoryName,
                x.pb.SellingPrice, x.pb.IsAvailableForSale, x.p.IsBatchTracked,
                units.Where(u => u.ProductId == x.p.Id)
                    .Select(u => new CatalogSyncUnitDto(
                        u.Id, u.UnitName, u.ConversionFactorToBase, u.IsBaseUnit,
                        barcodesByUnit.GetValueOrDefault(u.Id, new List<string>())))
                    .ToList(),
                batchesByProduct.GetValueOrDefault(x.p.Id, new List<CatalogSyncBatchDto>()),
                promo is null ? null : new CatalogSyncPromotionDto(promo.Id, promo.Title, promo.BundleQuantity, promo.BundlePrice, promo.MaxQuantityPerInvoice));
        })
            .ToList();

        return new PagedResult<CatalogSyncProductDto>(items, totalCount, pageNumber, pageSize);
    }
}
