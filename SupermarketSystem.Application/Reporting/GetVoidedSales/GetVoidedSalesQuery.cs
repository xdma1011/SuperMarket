using Microsoft.EntityFrameworkCore;
using SupermarketSystem.Application.Common.Interfaces;
using SupermarketSystem.Application.Common.Pagination;
using SupermarketSystem.Domain.Sales;

namespace SupermarketSystem.Application.Reporting.GetVoidedSales;

public sealed record GetVoidedSalesQuery(PagedRequest Paging, Guid? BranchId, DateTime? FromUtc, DateTime? ToUtc);

public sealed record VoidedSaleItemDto(
    Guid SaleInvoiceId,
    string InvoiceNumber,
    Guid BranchId,
    decimal TotalAmount,
    Guid VoidedByUserId,
    string VoidedByUsername,
    VoidReason VoidReason,
    string? VoidNotes,
    DateTime VoidedAtUtc);

/// <summary>
/// Architecture Review §14: "Recently voided invoices". Filters on
/// Status == Voided, which the (BranchId, Status, CreatedAtUtc) index on
/// SaleInvoice exists specifically to make cheap — voids are the minority
/// status that index was built for.
/// </summary>
public sealed class GetVoidedSalesHandler
{
    private readonly IApplicationDbContext _context;

    public GetVoidedSalesHandler(IApplicationDbContext context)
    {
        _context = context;
    }

    public async Task<PagedResult<VoidedSaleItemDto>> HandleAsync(GetVoidedSalesQuery query, CancellationToken cancellationToken)
    {
        var paging = query.Paging.Normalized();

        var sales = _context.SaleInvoices.AsNoTracking().Where(s => s.Status == SaleInvoiceStatus.Voided);

        if (query.BranchId is { } branchId)
        {
            sales = sales.Where(s => s.BranchId == branchId);
        }

        if (query.FromUtc is { } fromUtc)
        {
            sales = sales.Where(s => s.VoidedAtUtc >= fromUtc);
        }

        if (query.ToUtc is { } toUtc)
        {
            sales = sales.Where(s => s.VoidedAtUtc <= toUtc);
        }

        sales = sales.OrderByDescending(s => s.VoidedAtUtc).ThenByDescending(s => s.Id);

        var totalCount = await sales.CountAsync(cancellationToken);

        var items = await sales
            .Skip(paging.Skip)
            .Take(paging.PageSize)
            .Join(_context.Users.AsNoTracking(),
                s => s.VoidedByUserId,
                u => (Guid?)u.Id,
                (s, u) => new VoidedSaleItemDto(
                    s.Id,
                    s.InvoiceNumber,
                    s.BranchId,
                    s.TotalAmount,
                    u.Id,
                    u.Username,
                    s.VoidReason!.Value,
                    s.VoidNotes,
                    s.VoidedAtUtc!.Value))
            .ToListAsync(cancellationToken);

        return new PagedResult<VoidedSaleItemDto>(items, totalCount, paging.PageNumber, paging.PageSize);
    }
}
