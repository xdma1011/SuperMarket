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
    DateTime CreatedAtUtc,
    bool IsOpeningBalance = false);

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

        // Left join: فاتورة الرصيد الافتتاحي بلا مورد (SupplierId = null) وما لازم تختفي من القائمة.
        var items = await (
            from pi in invoices.Skip(paging.Skip).Take(paging.PageSize)
            join s in _context.Suppliers.AsNoTracking() on pi.SupplierId equals (Guid?)s.Id into suppliers
            from s in suppliers.DefaultIfEmpty()
            orderby pi.CreatedAtUtc descending, pi.Id descending
            select new PurchaseInvoiceListItemDto(
                pi.Id, pi.InvoiceNumber, pi.SupplierInvoiceReference,
                pi.IsOpeningBalance ? "رصيد افتتاحي" : (s == null ? "(غير معروف)" : s.Name),
                (int)pi.Status, pi.TotalAmount, pi.TotalPaidAmount, pi.CreatedAtUtc, pi.IsOpeningBalance))
            .ToListAsync(cancellationToken);

        return new PagedResult<PurchaseInvoiceListItemDto>(items, totalCount, paging.PageNumber, paging.PageSize);
    }
}
