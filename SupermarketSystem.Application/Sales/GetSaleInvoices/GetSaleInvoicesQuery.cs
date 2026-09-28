using Microsoft.EntityFrameworkCore;
using SupermarketSystem.Application.Common.Interfaces;
using SupermarketSystem.Application.Common.Pagination;
using SupermarketSystem.Domain.Sales;

namespace SupermarketSystem.Application.Sales.GetSaleInvoices;

// FromUtc/ToUtc/ProductSearch (28/9/2026): بحث الكاشير بالتاريخ والساعة والدقيقة - صاحب المحل بيرجع
// لتسجيل الكاميرا وبدو يتأكد هل صنف معيّن فات بفاتورة بهديك الدقيقة ولا ما انضرب أصلًا.
// CashierUserId/PaymentMethodId (28/9/2026): فلاتر صفحة المبيعات (مراجعة UI/UX 24/9).
public sealed record GetSaleInvoicesQuery(
    PagedRequest Paging, Guid? BranchId, DateTime? FromUtc = null, DateTime? ToUtc = null, string? ProductSearch = null,
    Guid? CashierUserId = null, Guid? PaymentMethodId = null);

public sealed record SaleFilterOptionDto(Guid Id, string Name);

public sealed record SaleFilterOptionsDto(IReadOnlyList<SaleFilterOptionDto> Cashiers, IReadOnlyList<SaleFilterOptionDto> PaymentMethods);

/// <summary>
/// خيارات فلاتر صفحة المبيعات: الكاشيرية اللي إلهم فواتير (مش كل المستخدمين - وما بتحتاج صلاحية Users.Manage)،
/// وطرق الدفع. بنفس صلاحية قائمة المبيعات.
/// </summary>
public sealed class GetSaleFilterOptionsHandler
{
    private readonly IApplicationDbContext _context;

    public GetSaleFilterOptionsHandler(IApplicationDbContext context)
    {
        _context = context;
    }

    public async Task<SaleFilterOptionsDto> HandleAsync(Guid? branchId, CancellationToken cancellationToken)
    {
        var creatorIds = await _context.SaleInvoices.AsNoTracking()
            .Where(s => (branchId == null || s.BranchId == branchId) && s.CreatedByUserId != null)
            .Select(s => s.CreatedByUserId!.Value)
            .Distinct()
            .ToListAsync(cancellationToken);

        var cashiers = await _context.Users.IgnoreQueryFilters().AsNoTracking()
            .Where(u => creatorIds.Contains(u.Id))
            .OrderBy(u => u.FullName)
            .Select(u => new SaleFilterOptionDto(u.Id, u.FullName))
            .ToListAsync(cancellationToken);

        var paymentMethods = await _context.PaymentMethods.AsNoTracking()
            .OrderBy(m => m.SortOrder).ThenBy(m => m.Name)
            .Select(m => new SaleFilterOptionDto(m.Id, m.Name))
            .ToListAsync(cancellationToken);

        return new SaleFilterOptionsDto(cashiers, paymentMethods);
    }
}

public sealed record SaleInvoiceListItemDto(
    Guid Id,
    string InvoiceNumber,
    int StatusCode,
    string StatusTitle,
    decimal TotalAmount,
    decimal TotalPaidAmount,
    decimal TotalReturnedAmount,
    DateTime CreatedAtUtc,
    string? CustomerName,
    string? CustomerPhone,
    // مين عمل الفاتورة (الكاشير) - إضافة بلا كسر، تطبيق الكاشير القديم بيتجاهلها.
    string? CashierName = null,
    // سحب شريك بسعر التكلفة (28/9/2026) - بيبين كعلامة بقائمة المبيعات.
    bool IsAtCostWithdrawal = false);

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

        if (query.FromUtc is { } fromUtc)
        {
            invoices = invoices.Where(x => x.Invoice.CreatedAtUtc >= fromUtc);
        }

        if (query.ToUtc is { } toUtc)
        {
            invoices = invoices.Where(x => x.Invoice.CreatedAtUtc <= toUtc);
        }

        if (query.CashierUserId is { } cashierUserId)
        {
            invoices = invoices.Where(x => x.Invoice.CreatedByUserId == cashierUserId);
        }

        if (query.PaymentMethodId is { } paymentMethodId)
        {
            invoices = invoices.Where(x => x.Invoice.Payments.Any(p => p.PaymentMethodId == paymentMethodId));
        }

        // فواتير فيها صنف اسمه أو باركوده بيطابق - "هل الحليب فات بفاتورة بين 14:25 و14:40؟"
        if (!string.IsNullOrWhiteSpace(query.ProductSearch))
        {
            var productPattern = $"%{query.ProductSearch.Trim()}%";
            var matchingProductIds = _context.Products.IgnoreQueryFilters().AsNoTracking()
                .Where(p => EF.Functions.Like(p.Name, productPattern))
                .Select(p => p.Id)
                .Union(_context.ProductBarcodes.AsNoTracking()
                    .Where(b => EF.Functions.Like(b.BarcodeValue, productPattern))
                    .Select(b => b.ProductId));
            invoices = invoices.Where(x => x.Invoice.Items.Any(i => matchingProductIds.Contains(i.ProductId)));
        }

        if (!string.IsNullOrWhiteSpace(paging.Search))
        {
            var pattern = $"%{paging.Search.Trim()}%";
            invoices = invoices.Where(x =>
                EF.Functions.Like(x.Invoice.InvoiceNumber, pattern) ||
                (x.CustomerPhone != null && EF.Functions.Like(x.CustomerPhone, pattern)) ||
                // رقم انكتب عند الكاشير بلا زبون مسجّل (28/9/2026) - محفوظ على الفاتورة نفسها.
                (x.Invoice.CustomerPhoneSnapshot != null && EF.Functions.Like(x.Invoice.CustomerPhoneSnapshot, pattern)) ||
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
                x.Invoice.TotalAmount, x.Invoice.TotalPaidAmount, x.Invoice.TotalReturnedAmount, x.Invoice.CreatedAtUtc,
                x.CustomerName, x.CustomerPhone, x.Invoice.CreatedByUserId, x.Invoice.IsAtCostWithdrawal,
                x.Invoice.CustomerNameSnapshot, x.Invoice.CustomerPhoneSnapshot
            })
            .ToListAsync(cancellationToken);

        var creatorIds = rawItems.Where(s => s.CreatedByUserId != null).Select(s => s.CreatedByUserId!.Value).Distinct().ToList();
        var cashierNames = creatorIds.Count == 0
            ? new Dictionary<Guid, string>()
            : await _context.Users.IgnoreQueryFilters().AsNoTracking()
                .Where(u => creatorIds.Contains(u.Id))
                .ToDictionaryAsync(u => u.Id, u => u.FullName, cancellationToken);

        var items = rawItems.Select(s => new SaleInvoiceListItemDto(
            s.Id, s.InvoiceNumber, (int)s.Status, StatusTitle(s.Status),
            s.TotalAmount, s.TotalPaidAmount, s.TotalReturnedAmount, s.CreatedAtUtc,
            s.CustomerName ?? s.CustomerNameSnapshot, s.CustomerPhone ?? s.CustomerPhoneSnapshot,
            s.CreatedByUserId is { } creatorId && cashierNames.TryGetValue(creatorId, out var cashierName) ? cashierName : null,
            s.IsAtCostWithdrawal))
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
