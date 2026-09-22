using Microsoft.EntityFrameworkCore;
using SupermarketSystem.Application.Common.Interfaces;
using SupermarketSystem.Application.Common.Pagination;
using SupermarketSystem.Domain.Purchasing;

namespace SupermarketSystem.Application.Reporting.GetSupplierPaymentDue;

public sealed record GetSupplierPaymentDueQuery(PagedRequest Paging, Guid? BranchId);

public sealed record SupplierPaymentDueItemDto(
    Guid PurchaseInvoiceId,
    string InvoiceNumber,
    string SupplierName,
    decimal RemainingDebt,
    DateOnly DueDate,
    int DaysRemaining);

/// <summary>
/// فواتير شراء Received، بـDueDate محدَّد، وعليها دين متبقٍّ فعلي
/// (TotalAmount - TotalPaidAmount &gt; 0) - فاتورة بلا DueDate (الحالة
/// الشائعة، دفع فوري بلا مهلة) ببساطة غايبة عن هالتقرير، لا تُعتبر خطأ.
/// DaysRemaining سالب = متجاوزة الاستحقاق فعلًا (لا فلترة - نفس فلسفة
/// GetExpiringBatchesQuery.DaysRemaining بالضبط). مرتَّب تصاعديًا حسب
/// DueDate (الأقرب استحقاقًا/الأكتر تجاوزًا أولًا).
/// </summary>
public sealed class GetSupplierPaymentDueHandler
{
    private readonly IApplicationDbContext _context;
    private readonly IDateTimeProvider _dateTimeProvider;

    public GetSupplierPaymentDueHandler(IApplicationDbContext context, IDateTimeProvider dateTimeProvider)
    {
        _context = context;
        _dateTimeProvider = dateTimeProvider;
    }

    public async Task<PagedResult<SupplierPaymentDueItemDto>> HandleAsync(
        GetSupplierPaymentDueQuery query, CancellationToken cancellationToken)
    {
        var paging = query.Paging.Normalized();
        var today = DateOnly.FromDateTime(_dateTimeProvider.UtcNow);

        var invoices = _context.PurchaseInvoices.AsNoTracking()
            .Where(pi => pi.Status == PurchaseInvoiceStatus.Received
                && pi.DueDate != null
                && pi.TotalAmount > pi.TotalPaidAmount);

        if (query.BranchId is { } branchId)
        {
            invoices = invoices.Where(pi => pi.BranchId == branchId);
        }

        var totalCount = await invoices.CountAsync(cancellationToken);

        var rows = await invoices
            .OrderBy(pi => pi.DueDate)
            .Skip(paging.Skip)
            .Take(paging.PageSize)
            .Join(_context.Suppliers.AsNoTracking(), pi => pi.SupplierId, s => s.Id,
                (pi, s) => new
                {
                    pi.Id,
                    pi.InvoiceNumber,
                    SupplierName = s.Name,
                    RemainingDebt = pi.TotalAmount - pi.TotalPaidAmount,
                    // DueDate غير null دائمًا هون (الفلتر فوق يستثني null) - .Value آمن.
                    DueDate = pi.DueDate!.Value
                })
            .ToListAsync(cancellationToken);

        var items = rows.Select(x => new SupplierPaymentDueItemDto(
            x.Id, x.InvoiceNumber, x.SupplierName, x.RemainingDebt, x.DueDate,
            x.DueDate.DayNumber - today.DayNumber)).ToList();

        return new PagedResult<SupplierPaymentDueItemDto>(items, totalCount, paging.PageNumber, paging.PageSize);
    }
}
