using Microsoft.EntityFrameworkCore;
using SupermarketSystem.Application.Common.Interfaces;
using SupermarketSystem.Application.Common.Pagination;
using SupermarketSystem.Domain.Sales;

namespace SupermarketSystem.Application.Reporting.GetRecentReturnedItems;

public sealed record GetRecentReturnedItemsQuery(PagedRequest Paging, Guid? BranchId, DateTime? FromUtc, DateTime? ToUtc);

public sealed record RecentReturnedItemDto(
    Guid ReturnInvoiceItemId,
    Guid ReturnInvoiceId,
    string ReturnInvoiceNumber,
    Guid ProductId,
    string ProductName,
    decimal Quantity,
    decimal LineTotal,
    ReturnReason Reason,
    Guid BranchId,
    DateTime ReturnedAtUtc);

/// <summary>
/// مختلف عن GetRecentReturns (اللي بيرجّع فواتير) — هذا بيرجّع أصناف مفردة
/// عبر كل الفواتير، مرتّبة زمنيًا بلا اعتبار لأي فاتورة تتبع. الغرض: "شو
/// آخر شي رجع، بغض النظر عن الفاتورة" — أساس مباشر لفكرة "جرد مفاجئ على
/// آخر المرتجعات" اللي ذُكرت عند تصميم D8 (لسه ما اكتملت D8 نفسها، بس
/// هذا الاستعلام جاهز يستقبلها فور اكتمالها بلا أي تعديل إضافي).
/// </summary>
public sealed class GetRecentReturnedItemsHandler
{
    private readonly IApplicationDbContext _context;

    public GetRecentReturnedItemsHandler(IApplicationDbContext context)
    {
        _context = context;
    }

    public async Task<PagedResult<RecentReturnedItemDto>> HandleAsync(
        GetRecentReturnedItemsQuery query, CancellationToken cancellationToken)
    {
        var paging = query.Paging.Normalized();

        var headers = _context.ReturnInvoices.AsNoTracking().AsQueryable();

        if (query.BranchId is { } branchId)
        {
            headers = headers.Where(r => r.BranchId == branchId);
        }

        if (query.FromUtc is { } fromUtc)
        {
            headers = headers.Where(r => r.CreatedAtUtc >= fromUtc);
        }

        if (query.ToUtc is { } toUtc)
        {
            headers = headers.Where(r => r.CreatedAtUtc <= toUtc);
        }

        var lines = _context.ReturnInvoiceItems.AsNoTracking()
            .Join(headers, i => i.ReturnInvoiceId, r => r.Id, (i, r) => new { i, r });

        var totalCount = await lines.CountAsync(cancellationToken);

        var page = await lines
            .OrderByDescending(x => x.r.CreatedAtUtc)
            .ThenByDescending(x => x.i.Id)
            .Skip(paging.Skip)
            .Take(paging.PageSize)
            .Join(_context.Products.AsNoTracking(),
                x => x.i.ProductId, p => p.Id,
                (x, p) => new RecentReturnedItemDto(
                    x.i.Id, x.r.Id, x.r.InvoiceNumber, p.Id, p.Name,
                    x.i.Quantity, x.i.LineTotal, x.r.Reason, x.r.BranchId, x.r.CreatedAtUtc))
            .ToListAsync(cancellationToken);

        return new PagedResult<RecentReturnedItemDto>(page, totalCount, paging.PageNumber, paging.PageSize);
    }
}
