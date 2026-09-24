using Microsoft.EntityFrameworkCore;
using SupermarketSystem.Domain.Sales;
using SupermarketSystem.Application.Common.Interfaces;
using SupermarketSystem.Application.Common.Pagination;

namespace SupermarketSystem.Application.Reporting.GetStagnantProducts;

// FromUtc/ToUtc بدل SinceUtc - نفس سبب GetProductConsumptionLevelsQuery (الواجهة بتبعت فترة).
public sealed record GetStagnantProductsQuery(PagedRequest Paging, Guid BranchId, DateTime FromUtc, DateTime? ToUtc = null);

public sealed record StagnantProductItemDto(
    Guid ProductId,
    string ProductName,
    decimal SellingPrice,
    decimal? CurrentStock);

/// <summary>
/// منتجات متوفرة للبيع بهذا الفرع (IsAvailableForSale) لكن بلا أي سطر
/// بيع واحد منذ التاريخ المحدد. BranchId إلزامي هون (لا اختياري) — "راكد"
/// مفهوم مربوط بفرع محدد بطبيعته (منتج راكد بفرع ممكن يكون نشط بفرع تاني).
///
/// التنفيذ: كل المنتجات المتوفرة بالفرع ناقص المنتجات اللي فعلًا انباعت
/// بالفترة — عبر !Contains(...) بدل تحميل كل شي للذاكرة ومقارنته يدويًا.
/// </summary>
public sealed class GetStagnantProductsHandler
{
    private readonly IApplicationDbContext _context;

    public GetStagnantProductsHandler(IApplicationDbContext context)
    {
        _context = context;
    }

    public async Task<PagedResult<StagnantProductItemDto>> HandleAsync(
        GetStagnantProductsQuery query, CancellationToken cancellationToken)
    {
        var paging = query.Paging.Normalized();

        // "انباع" = بفاتورة مش ملغاة وما رجع كله - بيعة ملغاة أو مرتجعة بالكامل ما بتخلّي
        // الصنف "مش راكد" (كانت بتخلّيه).
        var toUtc = query.ToUtc ?? DateTime.MaxValue;
        var soldProductIds = _context.SaleInvoiceItems.AsNoTracking()
            .Join(_context.SaleInvoices.AsNoTracking(),
                i => i.SaleInvoiceId, s => s.Id, (i, s) => new { i.ProductId, i.Quantity, i.QuantityReturned, s.BranchId, s.Status, s.CreatedAtUtc })
            .Where(x => x.BranchId == query.BranchId && x.Status != SaleInvoiceStatus.Voided
                        && x.Quantity > x.QuantityReturned
                        && x.CreatedAtUtc >= query.FromUtc && x.CreatedAtUtc <= toUtc)
            .Select(x => x.ProductId);

        var stagnant = _context.ProductBranches.AsNoTracking()
            .Where(pb => pb.BranchId == query.BranchId && pb.IsAvailableForSale)
            .Where(pb => !soldProductIds.Contains(pb.ProductId));

        var totalCount = await stagnant.CountAsync(cancellationToken);

        var page = await stagnant
            .Join(_context.Products.AsNoTracking(),
                pb => pb.ProductId, p => p.Id,
                (pb, p) => new { pb, p })
            // كان الترتيب بـProductId (عشوائي فعليًا، بلا معنى للمستخدم) —
            // الآن أبجديًا بالاسم، قابل للتصفّح فعليًا.
            .OrderBy(x => x.p.Name)
            .Skip(paging.Skip)
            .Take(paging.PageSize)
            // GroupJoin مع Stock — منتج ممكن ما يكون إله صف Stock أصلًا
            // (لسه ما اتشرى قط)، فرصيده الحالي null، لا صفر مضلَّل.
            .GroupJoin(_context.Stocks.AsNoTracking().Where(s => s.BranchId == query.BranchId),
                x => x.pb.ProductId, s => s.ProductId,
                (x, stocks) => new { x.pb, x.p, stocks })
            .SelectMany(
                x => x.stocks.DefaultIfEmpty(),
                (x, s) => new StagnantProductItemDto(x.p.Id, x.p.Name, x.pb.SellingPrice, s != null ? s.QuantityOnHand : (decimal?)null))
            .ToListAsync(cancellationToken);

        return new PagedResult<StagnantProductItemDto>(page, totalCount, paging.PageNumber, paging.PageSize);
    }
}
