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
    DateTime CreatedAtUtc,
    string? CustomerName,
    string? CustomerPhone);

/// <summary>
/// كانت ناقصة بالكامل — SalesEndpoints قبل هذا كانت POST فقط (إتمام
/// بيع، إلغاء). أساس أي عملية إرجاع: الكاشير لازم يدوّر عن الفاتورة
/// الأصلية بالرقم قبل ما يقدر يحدد شو يرجّع بالضبط.
///
/// البحث كمان بيغطّي رقم هاتف/اسم الزبون (لا رقم الفاتورة بس) - يخلي
/// هاي الصفحة نفسها تصلح "سجل فواتير الزبون" بحث برقم الهاتف مباشرة،
/// بلا صفحة منفصلة (راجع نقاش صاحب المشروع).
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

        var invoices =
            from s in _context.SaleInvoices.AsNoTracking()
            join c in _context.Customers.AsNoTracking() on s.CustomerId equals c.Id into customerJoin
            from c in customerJoin.DefaultIfEmpty()
            select new { Invoice = s, CustomerName = (string?)c.FullName, CustomerPhone = c.Phone };

        if (query.BranchId is { } branchId)
        {
            invoices = invoices.Where(x => x.Invoice.BranchId == branchId);
        }

        if (!string.IsNullOrWhiteSpace(paging.Search))
        {
            var pattern = $"%{paging.Search.Trim()}%";
            invoices = invoices.Where(x =>
                EF.Functions.Like(x.Invoice.InvoiceNumber, pattern) ||
                (x.CustomerPhone != null && EF.Functions.Like(x.CustomerPhone, pattern)) ||
                (x.CustomerName != null && EF.Functions.Like(x.CustomerName, pattern)));
        }

        invoices = invoices.OrderByDescending(x => x.Invoice.CreatedAtUtc).ThenByDescending(x => x.Invoice.Id);

        var totalCount = await invoices.CountAsync(cancellationToken);

        var rawItems = await invoices
            .Skip(paging.Skip)
            .Take(paging.PageSize)
            .Select(x => new
            {
                x.Invoice.Id, x.Invoice.InvoiceNumber, x.Invoice.Status,
                x.Invoice.TotalAmount, x.Invoice.TotalReturnedAmount, x.Invoice.CreatedAtUtc,
                x.CustomerName, x.CustomerPhone
            })
            .ToListAsync(cancellationToken);

        var items = rawItems.Select(s => new SaleInvoiceListItemDto(
            s.Id, s.InvoiceNumber, (int)s.Status, StatusTitle(s.Status),
            s.TotalAmount, s.TotalReturnedAmount, s.CreatedAtUtc, s.CustomerName, s.CustomerPhone))
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
