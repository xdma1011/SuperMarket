using Microsoft.EntityFrameworkCore;
using SupermarketSystem.Application.Common.Interfaces;
using SupermarketSystem.Application.Common.Results;
using SupermarketSystem.Application.Sales.Common;
using SupermarketSystem.Domain.Sales;

namespace SupermarketSystem.Application.Sales.CreditCustomer;

/// <summary>
/// "بيع بالدين" من الكاشير (29/9/2026): قبل البيع الكاشير بيتأكد مين الزبون برقمه (نفس مطابقة آخر 9 أرقام تبع
/// SaleCustomerPhone) وشو دينه الحالي - وبعدين بيبعت البيعة بـCustomerId صريح + AllowCreditSale (القاعدة بالسيرفر
/// بتطلب زبون معروف صراحة). بدّه نت لحظتها عمدًا: بيع بالدين لزبون مش مؤكَّد كان رح يعلق بالطابور للأبد.
/// </summary>
public sealed record LookupCreditCustomerQuery(string Phone);

public sealed record CreditCustomerDto(Guid CustomerId, string FullName, string? Phone, decimal CurrentDebt, int UnpaidInvoiceCount);

public sealed class LookupCreditCustomerHandler
{
    private readonly IApplicationDbContext _context;

    public LookupCreditCustomerHandler(IApplicationDbContext context)
    {
        _context = context;
    }

    public async Task<Result<CreditCustomerDto>> HandleAsync(LookupCreditCustomerQuery query, CancellationToken cancellationToken)
    {
        if (SaleCustomerPhone.Normalize(query.Phone) is not { } phone)
        {
            return Result.Failure<CreditCustomerDto>(Error.Validation("CreditCustomer.InvalidPhone", "اكتب رقم الزبون صح (أرقام بس)."));
        }

        var key = SaleCustomerPhone.MatchKey(phone);
        var matches = await _context.Customers.AsNoTracking()
            .Where(c => c.Phone != null && EF.Functions.Like(c.Phone, "%" + key))
            .Select(c => new { c.Id, c.FullName, c.Phone })
            .Take(2)
            .ToListAsync(cancellationToken);

        if (matches.Count == 0)
        {
            return Result.Failure<CreditCustomerDto>(Error.NotFound("CreditCustomer.NotFound",
                "ما في زبون مسجّل بهالرقم - البيع بالدين بدّه زبون مسجّل (من لوحة الإدارة ← الزبائن)."));
        }

        if (matches.Count > 1)
        {
            return Result.Failure<CreditCustomerDto>(Error.Conflict("CreditCustomer.Ambiguous",
                "في أكتر من زبون بنفس الرقم - صلّحها من لوحة الإدارة قبل البيع بالدين."));
        }

        var customer = matches[0];
        // نفس تعريف GetCustomerDebts بالزبط: فواتير مش ملغاة، المتبقي = الإجمالي − المدفوع.
        var invoices = await _context.SaleInvoices.AsNoTracking()
            .Where(s => s.CustomerId == customer.Id && s.Status != SaleInvoiceStatus.Voided && s.TotalAmount > s.TotalPaidAmount)
            .Select(s => s.TotalAmount - s.TotalPaidAmount)
            .ToListAsync(cancellationToken);

        return Result.Success(new CreditCustomerDto(customer.Id, customer.FullName, customer.Phone, invoices.Sum(), invoices.Count));
    }
}
