using Microsoft.EntityFrameworkCore;
using SupermarketSystem.Application.Common.Interfaces;
using SupermarketSystem.Application.Common.Pagination;
using SupermarketSystem.Domain.Purchasing;

namespace SupermarketSystem.Application.Reporting.GetSupplierPriceComparison;

public sealed record GetSupplierPriceComparisonQuery(PagedRequest Paging, Guid ProductId, DateTime? FromUtc, DateTime? ToUtc);

public sealed record SupplierPriceComparisonItemDto(
    Guid SupplierId,
    string SupplierName,
    decimal UnitCost,
    decimal Quantity,
    DateTime PurchasedAtUtc,
    string PurchaseInvoiceNumber);

/// <summary>
/// كل سطر شراء لمنتج معيّن عبر مختلف الموردين والفواتير — بلا حساب أو
/// استنتاج، بس عرض تاريخي مرتّب زمنيًا يخلّي المقارنة سهلة بالعين. مبني
/// فقط على PurchaseInvoiceItem (الأسعار الفعلية المدفوعة، لا افتراضية) —
/// فواتير Draft لسه ما اتأكدت (Received) لا تُحسب، لأنها لسه مش قرار
/// شراء نهائي.
/// </summary>
public sealed class GetSupplierPriceComparisonHandler
{
    private readonly IApplicationDbContext _context;

    public GetSupplierPriceComparisonHandler(IApplicationDbContext context)
    {
        _context = context;
    }

    public async Task<PagedResult<SupplierPriceComparisonItemDto>> HandleAsync(
        GetSupplierPriceComparisonQuery query, CancellationToken cancellationToken)
    {
        var paging = query.Paging.Normalized();

        var invoices = _context.PurchaseInvoices.AsNoTracking()
            .Where(pi => pi.Status == PurchaseInvoiceStatus.Received);

        if (query.FromUtc is { } fromUtc)
        {
            invoices = invoices.Where(pi => pi.CreatedAtUtc >= fromUtc);
        }

        if (query.ToUtc is { } toUtc)
        {
            invoices = invoices.Where(pi => pi.CreatedAtUtc <= toUtc);
        }

        var lines = _context.PurchaseInvoiceItems.AsNoTracking()
            .Where(i => i.ProductId == query.ProductId)
            .Join(invoices, i => i.PurchaseInvoiceId, pi => pi.Id, (i, pi) => new { i, pi });

        var totalCount = await lines.CountAsync(cancellationToken);

        var page = await lines
            .OrderByDescending(x => x.pi.CreatedAtUtc)
            .Skip(paging.Skip)
            .Take(paging.PageSize)
            .Join(_context.Suppliers.AsNoTracking(),
                x => x.pi.SupplierId, s => s.Id,
                (x, s) => new SupplierPriceComparisonItemDto(
                    s.Id, s.Name, x.i.UnitCost, x.i.Quantity, x.pi.CreatedAtUtc, x.pi.InvoiceNumber))
            .ToListAsync(cancellationToken);

        return new PagedResult<SupplierPriceComparisonItemDto>(page, totalCount, paging.PageNumber, paging.PageSize);
    }
}
