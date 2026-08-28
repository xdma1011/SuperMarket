using Microsoft.EntityFrameworkCore;
using SupermarketSystem.Application.Common.Interfaces;
using SupermarketSystem.Application.Common.Results;

namespace SupermarketSystem.Application.Purchasing.RecordPurchaseInvoicePayment;

public sealed record RecordPurchaseInvoicePaymentCommand(
    Guid PurchaseInvoiceId,
    Guid PaymentMethodId,
    decimal Amount,
    string? ExternalReference,
    Guid ClientRequestId);

public sealed record RecordPurchaseInvoicePaymentResponse(
    Guid PaymentId, decimal NewTotalPaidAmount, decimal RemainingDebt);

/// <summary>
/// تسجيل دفعة يدوية للمورد — نسخة مبسَّطة من نمط SaleInvoicePayment
/// (بلا آلية عكس، راجع تعليق الكيان نفسه). القيد الوحيد: مجموع
/// المدفوعات ما يتجاوز إجمالي الفاتورة. بخلاف مسارات البيع عالية
/// التزامن، هذا المسار عادة إداري لمستخدم واحد بلحظة معيّنة — ضمان
/// EF Core العادي داخل معاملة كافٍ هون.
/// </summary>
public sealed class RecordPurchaseInvoicePaymentHandler
{
    private readonly IApplicationDbContext _context;
    private readonly ICurrentUserContext _currentUser;

    public RecordPurchaseInvoicePaymentHandler(IApplicationDbContext context, ICurrentUserContext currentUser)
    {
        _context = context;
        _currentUser = currentUser;
    }

    public async Task<Result<RecordPurchaseInvoicePaymentResponse>> HandleAsync(
        RecordPurchaseInvoicePaymentCommand command, CancellationToken cancellationToken)
    {
        if (command.Amount <= 0)
        {
            return Result.Failure<RecordPurchaseInvoicePaymentResponse>(
                Error.Validation("Payment.AmountMustBePositive", "قيمة الدفعة يجب أن تكون موجبة."));
        }

        var alreadyExists = await _context.PurchaseInvoicePayments.AsNoTracking()
            .AnyAsync(p => p.ClientRequestId == command.ClientRequestId, cancellationToken);
        if (alreadyExists)
        {
            return Result.Failure<RecordPurchaseInvoicePaymentResponse>(
                Error.Conflict("Payment.DuplicateRequest", "هذه الدفعة مسجَّلة مسبقًا بنفس الطلب."));
        }

        var invoice = await _context.PurchaseInvoices
            .Include(pi => pi.Payments)
            .FirstOrDefaultAsync(pi => pi.Id == command.PurchaseInvoiceId, cancellationToken);

        if (invoice is null)
        {
            return Result.Failure<RecordPurchaseInvoicePaymentResponse>(
                Error.NotFound("Payment.InvoiceNotFound", $"فاتورة الشراء '{command.PurchaseInvoiceId}' غير موجودة."));
        }

        var methodExists = await _context.PaymentMethods.AsNoTracking()
            .AnyAsync(pm => pm.Id == command.PaymentMethodId && pm.IsActive, cancellationToken);
        if (!methodExists)
        {
            return Result.Failure<RecordPurchaseInvoicePaymentResponse>(
                Error.NotFound("Payment.MethodNotFound", $"طريقة الدفع '{command.PaymentMethodId}' غير موجودة أو غير فعّالة."));
        }

        var userId = _currentUser.UserId
            ?? throw new InvalidOperationException("لا يمكن تسجيل دفعة بلا هوية مستخدم مصادَق عليها.");

        try
        {
            invoice.AddPayment(
                command.PaymentMethodId, command.Amount, userId, invoice.BranchId,
                command.ExternalReference, command.ClientRequestId);
        }
        catch (Domain.Common.DomainException ex)
        {
            return Result.Failure<RecordPurchaseInvoicePaymentResponse>(
                Error.Validation("Payment.ExceedsInvoiceTotal", ex.Message));
        }

        await _context.SaveChangesAsync(cancellationToken);

        var newPayment = invoice.Payments.Last();

        return Result.Success(new RecordPurchaseInvoicePaymentResponse(
            newPayment.Id, invoice.TotalPaidAmount, invoice.TotalAmount - invoice.TotalPaidAmount));
    }
}
