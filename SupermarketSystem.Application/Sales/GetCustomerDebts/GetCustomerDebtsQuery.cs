using Microsoft.EntityFrameworkCore;
using SupermarketSystem.Application.Common.Interfaces;
using SupermarketSystem.Domain.Sales;

namespace SupermarketSystem.Application.Sales.GetCustomerDebts;

public sealed record CustomerDebtDto(
    Guid CustomerId,
    string CustomerName,
    decimal TotalInvoiced,
    decimal TotalPaid,
    decimal RemainingDebt,
    int UnpaidInvoiceCount);

public sealed record GetCustomerDebtsResponse(
    IReadOnlyList<CustomerDebtDto> Customers, decimal GrandTotalDebt);

/// <summary>
/// نسخة طبق الأصل من GetSupplierDebtsQuery بالاتجاه المعاكس (الزبون
/// يدين لنا، لا نحن للمورد) - "بيع بالدين" (CLAUDE.md، طلب صاحب المشروع
/// الصريح 22/9/2026). بس فواتير بيع Completed/PartiallyReturned/
/// FullyReturned (كل الحالات ما عدا Voided - فاتورة ملغاة ما فيها دين
/// أصلًا) لزبون معروف (CustomerId != null - بيع لزبون walk-in ما بيقدر
/// يكون بالدين أصلًا، راجع CompleteSaleCommand). RemainingDebt =
/// TotalAmount - TotalPaidAmount لكل فاتورة، مجمّعة على مستوى الزبون -
/// بلا Ledger مخزَّن، نفس فلسفة Customer.cs الموثَّقة (CustomerPurchaseHistory
/// عمدًا مو كيان مخزَّن).
///
/// نقطة عمياء موثَّقة (لم تُطلَب، لم تُلمَس): SaleInvoice.RegisterReturn
/// (استدعاها ProcessReturnHandler) بتحدّث TotalReturnedAmount بس، بلا أي
/// أثر على TotalPaidAmount أو TotalAmount - يعني إرجاع جزئي/كامل على بيع
/// بالدين غير مسدَّد بالكامل **ما بينقص الدين المحسوب هون تلقائيًا**
/// (نفس فجوة GetSupplierDebtsQuery المكافئة تمامًا، غير مُصلَحة هناك
/// أيضًا). يحتاج تصميم منفصل (هل الإرجاع ينقص الدين المتبقي مباشرة، أو
/// يحتاج تسوية يدوية؟) لو صار سيناريو فعلي واجهه صاحب المشروع.
/// </summary>
public sealed class GetCustomerDebtsHandler
{
    private readonly IApplicationDbContext _context;

    public GetCustomerDebtsHandler(IApplicationDbContext context)
    {
        _context = context;
    }

    public async Task<GetCustomerDebtsResponse> HandleAsync(CancellationToken cancellationToken)
    {
        var invoices = await _context.SaleInvoices.AsNoTracking()
            .Where(s => s.Status != SaleInvoiceStatus.Voided && s.CustomerId != null)
            .Select(s => new { CustomerId = s.CustomerId!.Value, s.TotalAmount, s.TotalPaidAmount })
            .ToListAsync(cancellationToken);

        var customerNames = await _context.Customers.AsNoTracking()
            .ToDictionaryAsync(c => c.Id, c => c.FullName, cancellationToken);

        var grouped = invoices
            .GroupBy(i => i.CustomerId)
            .Select(g => new CustomerDebtDto(
                g.Key,
                customerNames.GetValueOrDefault(g.Key, "(غير معروف)"),
                g.Sum(i => i.TotalAmount),
                g.Sum(i => i.TotalPaidAmount),
                g.Sum(i => i.TotalAmount - i.TotalPaidAmount),
                g.Count(i => i.TotalAmount > i.TotalPaidAmount)))
            .Where(c => c.RemainingDebt > 0)
            .OrderByDescending(c => c.RemainingDebt)
            .ToList();

        return new GetCustomerDebtsResponse(grouped, grouped.Sum(c => c.RemainingDebt));
    }
}
