using Microsoft.EntityFrameworkCore;
using SupermarketSystem.Application.Common.Interfaces;
using SupermarketSystem.Application.Common.Pagination;
using SupermarketSystem.Domain.Sales;

namespace SupermarketSystem.Application.Sales.GetSaleInvoices;

public sealed record GetSaleInvoicesQuery(PagedRequest Paging, Guid? BranchId);

public sealed record SaleInvoiceListItemDto(
    Guid Id,
    string InvoiceNumber,
    int StatusCode,
    string StatusTitle,
    decimal TotalAmount,
    decimal TotalReturnedAmount,
    DateTime CreatedAtUtc);

/// <summary>
/// كانت ناقصة بالكامل — SalesEndpoints قبل هذا كانت POST فقط (إتمام
/// بيع، إلغاء). أساس أي عملية إرجاع: الكاشير لازم يدوّر عن الفاتورة
/// الأصلية بالرقم قبل ما يقدر يحدد شو يرجّع بالضبط.
///
/// StatusTitle مبني بـswitch صريح لا s.Status.ToString() — الأخيرة ما
/// بتنترجم بشكل موثوق لـSQL جوّا Select بمشاريع EF Core الحديثة (نفس
/// الفخ اللي انكشف بمشكلة حالة النسخ الاحتياطي سابقًا).
/// </summary>
public sealed class GetSaleInvoicesHandler
{
    private readonly IApplicationDbContext _context;

    public GetSaleInvoicesHandler(IApplicationDbContext context)
    {
        _context = context;
    }

    public async Task<PagedResult<SaleInvoiceListItemDto>> HandleAsync(GetSaleInvoicesQuery query, CancellationToken cancellationToken)
    {
        var paging = query.Paging.Normalized();

        var invoices = _context.SaleInvoices.AsNoTracking().AsQueryable();

        if (query.BranchId is { } branchId)
        {
            invoices = invoices.Where(s => s.BranchId == branchId);
        }

        if (!string.IsNullOrWhiteSpace(paging.Search))
        {
            var pattern = $"%{paging.Search.Trim()}%";
            invoices = invoices.Where(s => EF.Functions.Like(s.InvoiceNumber, pattern));
        }

        invoices = invoices.OrderByDescending(s => s.CreatedAtUtc).ThenByDescending(s => s.Id);

        var totalCount = await invoices.CountAsync(cancellationToken);

        var rawItems = await invoices
            .Skip(paging.Skip)
            .Take(paging.PageSize)
            .Select(s => new
            {
                s.Id, s.InvoiceNumber, s.Status, s.TotalAmount, s.TotalReturnedAmount, s.CreatedAtUtc
            })
            .ToListAsync(cancellationToken);

        var items = rawItems.Select(s => new SaleInvoiceListItemDto(
            s.Id, s.InvoiceNumber, (int)s.Status, StatusTitle(s.Status),
            s.TotalAmount, s.TotalReturnedAmount, s.CreatedAtUtc))
            .ToList();

        return new PagedResult<SaleInvoiceListItemDto>(items, totalCount, paging.PageNumber, paging.PageSize);
    }

    private static string StatusTitle(SaleInvoiceStatus status) => status switch
    {
        SaleInvoiceStatus.Completed => "مكتملة",
        SaleInvoiceStatus.Voided => "ملغاة",
        SaleInvoiceStatus.PartiallyReturned => "إرجاع جزئي",
        SaleInvoiceStatus.FullyReturned => "إرجاع كامل",
        _ => status.ToString()
    };
}
