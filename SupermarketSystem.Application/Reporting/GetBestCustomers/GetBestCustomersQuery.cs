using Microsoft.EntityFrameworkCore;
using SupermarketSystem.Application.Common.Interfaces;
using SupermarketSystem.Application.Common.Pagination;
using SupermarketSystem.Domain.Sales;

namespace SupermarketSystem.Application.Reporting.GetBestCustomers;

public sealed record GetBestCustomersQuery(PagedRequest Paging, Guid? BranchId, DateTime FromUtc, DateTime ToUtc);

public sealed record BestCustomerItemDto(
    Guid CustomerId,
    string FullName,
    string? Phone,
    int InvoiceCount,
    decimal TotalPurchases);

/// <summary>
/// أفضل زبون حسب إجمالي المشتريات — بيشمل فقط فواتير فيها CustomerId فعلي
/// (زبون مسجَّل). فواتير الزبائن العابرين (CustomerId = null) مستثناة
/// طبيعيًا — ما في هوية نجمّعها تحتها.
///
/// هذا نفس فكرة CustomerPurchaseHistory اللي قررنا من البداية إنها "تُشتق
/// من المعاملات لا تُخزَّن" (Architecture Review §31) — هذا التقرير تطبيق
/// حي لهاد المبدأ، بلا أي جدول تاريخي جديد.
/// </summary>
public sealed class GetBestCustomersHandler
{
    private readonly IApplicationDbContext _context;

    public GetBestCustomersHandler(IApplicationDbContext context)
    {
        _context = context;
    }

    public async Task<PagedResult<BestCustomerItemDto>> HandleAsync(GetBestCustomersQuery query, CancellationToken cancellationToken)
    {
        var paging = query.Paging.Normalized();

        var invoices = _context.SaleInvoices.AsNoTracking()
            .Where(s => s.Status != SaleInvoiceStatus.Voided && s.CustomerId != null
                        && s.CreatedAtUtc >= query.FromUtc && s.CreatedAtUtc <= query.ToUtc);

        if (query.BranchId is { } branchId)
        {
            invoices = invoices.Where(s => s.BranchId == branchId);
        }

        var grouped = invoices
            .GroupBy(s => s.CustomerId!.Value)
            .Select(g => new
            {
                CustomerId = g.Key,
                InvoiceCount = g.Count(),
                TotalPurchases = g.Sum(s => s.TotalAmount)
            });

        var totalCount = await grouped.CountAsync(cancellationToken);

        var page = await grouped
            .OrderByDescending(g => g.TotalPurchases)
            .Skip(paging.Skip)
            .Take(paging.PageSize)
            .Join(_context.Customers.AsNoTracking(),
                g => g.CustomerId,
                c => c.Id,
                (g, c) => new BestCustomerItemDto(c.Id, c.FullName, c.Phone, g.InvoiceCount, g.TotalPurchases))
            .ToListAsync(cancellationToken);

        return new PagedResult<BestCustomerItemDto>(page, totalCount, paging.PageNumber, paging.PageSize);
    }
}
