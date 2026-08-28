using Microsoft.EntityFrameworkCore;
using SupermarketSystem.Application.Common.Interfaces;
using SupermarketSystem.Application.Common.Pagination;

namespace SupermarketSystem.Application.Purchasing.GetPurchaseInvoices;

public sealed record GetPurchaseInvoicesQuery(PagedRequest Paging, Guid? BranchId);

public sealed record PurchaseInvoiceListItemDto(
    Guid Id,
    string InvoiceNumber,
    string? SupplierInvoiceReference,
    string SupplierName,
    int Status,
    decimal TotalAmount,
    decimal TotalPaidAmount,
    DateTime CreatedAtUtc);

public sealed class GetPurchaseInvoicesHandler
{
    private readonly IApplicationDbContext _context;

    public GetPurchaseInvoicesHandler(IApplicationDbContext context)
    {
        _context = context;
    }

    public async Task<PagedResult<PurchaseInvoiceListItemDto>> HandleAsync(
        GetPurchaseInvoicesQuery query, CancellationToken cancellationToken)
    {
        var paging = query.Paging.Normalized();

        var invoices = _context.PurchaseInvoices.AsNoTracking().AsQueryable();

        if (query.BranchId is { } branchId)
        {
            invoices = invoices.Where(pi => pi.BranchId == branchId);
        }

        invoices = invoices.OrderByDescending(pi => pi.CreatedAtUtc).ThenByDescending(pi => pi.Id);

        var totalCount = await invoices.CountAsync(cancellationToken);

        var items = await invoices
            .Skip(paging.Skip)
            .Take(paging.PageSize)
            .Join(_context.Suppliers.AsNoTracking(), pi => pi.SupplierId, s => s.Id,
                (pi, s) => new PurchaseInvoiceListItemDto(
                    pi.Id, pi.InvoiceNumber, pi.SupplierInvoiceReference, s.Name,
                    (int)pi.Status, pi.TotalAmount, pi.TotalPaidAmount, pi.CreatedAtUtc))
            .ToListAsync(cancellationToken);

        return new PagedResult<PurchaseInvoiceListItemDto>(items, totalCount, paging.PageNumber, paging.PageSize);
    }
}
