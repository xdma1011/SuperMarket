using Microsoft.EntityFrameworkCore;
using SupermarketSystem.Application.Common.Interfaces;
using SupermarketSystem.Application.Common.Results;
using SupermarketSystem.Domain.CashManagement;

namespace SupermarketSystem.Application.Sales.RecordSaleInvoicePayment;

public sealed record RecordSaleInvoicePaymentCommand(
    Guid SaleInvoiceId,
    Guid PaymentMethodId,
    decimal Amount,
    string? ExternalReference,
    Guid ClientRequestId);

public sealed record RecordSaleInvoicePaymentResponse(
    Guid PaymentId, decimal NewTotalPaidAmount, decimal RemainingDebt);

/// <summary>
/// تسجيل دفعة لاحقة من زبون على فاتورة بيع بالدين (راجع تعليق PROFIT
/// ASSUMPTION الكامل بـCompleteSaleCommand.cs - "بيع بالدين" مسموح الآن
/// لزبون معروف فقط، Payments أقل من الإجمالي). نسخة طبق الأصل من
/// RecordPurchaseInvoicePaymentHandler بالاتجاه المعاكس (نحن نقبض من
/// الزبون، لا ندفع لمورد) - **بما فيها نفس إصلاح الـEF Core gotcha
/// الموثَّق هناك بالتفصيل**: `invoice` هون كيان موجود مسبقًا (Unchanged)
/// لا كيان جديد بالكامل، فلازم `_context.SaleInvoicePayments.Add(newPayment)`
/// صراحة بعد `AddPayment` - وإلا EF ممكن يبني UPDATE بدل INSERT للدفعة
/// الجديدة ويفشل بـ409 Concurrency.Conflict كاذب.
/// </summary>
public sealed class RecordSaleInvoicePaymentHandler
{
    private readonly IApplicationDbContext _context;
    private readonly ICurrentUserContext _currentUser;
    private readonly IDateTimeProvider _dateTimeProvider;
    private readonly ITransactionalExecutor _transactionalExecutor;

    public RecordSaleInvoicePaymentHandler(
        IApplicationDbContext context, ICurrentUserContext currentUser, IDateTimeProvider dateTimeProvider,
        ITransactionalExecutor transactionalExecutor)
    {
        _context = context;
        _currentUser = currentUser;
        _dateTimeProvider = dateTimeProvider;
        _transactionalExecutor = transactionalExecutor;
    }

    public async Task<Result<RecordSaleInvoicePaymentResponse>> HandleAsync(
        RecordSaleInvoicePaymentCommand command, CancellationToken cancellationToken)
    {
        if (command.Amount <= 0)
        {
            return Result.Failure<RecordSaleInvoicePaymentResponse>(
                Error.Validation("Payment.AmountMustBePositive", "قيمة الدفعة يجب أن تكون موجبة."));
        }

        var alreadyExists = await _context.SaleInvoicePayments.AsNoTracking()
            .AnyAsync(p => p.ClientRequestId == command.ClientRequestId, cancellationToken);
        if (alreadyExists)
        {
            return Result.Failure<RecordSaleInvoicePaymentResponse>(
                Error.Conflict("Payment.DuplicateRequest", "هذه الدفعة مسجَّلة مسبقًا بنفس الطلب."));
        }

        var invoice = await _context.SaleInvoices
            .Include(s => s.Payments)
            .FirstOrDefaultAsync(s => s.Id == command.SaleInvoiceId, cancellationToken);

        if (invoice is null)
        {
            return Result.Failure<RecordSaleInvoicePaymentResponse>(
                Error.NotFound("Payment.InvoiceNotFound", $"فاتورة البيع '{command.SaleInvoiceId}' غير موجودة."));
        }

        // الإلغاء بيعكس كل الدفعات (TotalPaidAmount → 0) بس بيخلي TotalAmount -
        // بلا هالفحص، الفاتورة الملغاة بتبين "دين" كامل وبتقبل دفعة عليها.
        if (invoice.Status == Domain.Sales.SaleInvoiceStatus.Voided)
        {
            return Result.Failure<RecordSaleInvoicePaymentResponse>(
                Error.BusinessRule("Payment.InvoiceVoided", "لا يمكن تسجيل دفعة على فاتورة ملغاة."));
        }

        var paymentMethod = await _context.PaymentMethods.AsNoTracking()
            .FirstOrDefaultAsync(pm => pm.Id == command.PaymentMethodId && pm.IsActive, cancellationToken);
        if (paymentMethod is null)
        {
            return Result.Failure<RecordSaleInvoicePaymentResponse>(
                Error.NotFound("Payment.MethodNotFound", $"طريقة الدفع '{command.PaymentMethodId}' غير موجودة أو غير فعّالة."));
        }

        var userId = _currentUser.UserId
            ?? throw new InvalidOperationException("لا يمكن تسجيل دفعة بلا هوية مستخدم مصادَق عليها.");

        Domain.Sales.SaleInvoicePayment newPayment;
        try
        {
            newPayment = invoice.AddPayment(
                command.PaymentMethodId, command.Amount, userId, invoice.BranchId,
                command.ExternalReference, command.ClientRequestId);
        }
        catch (Domain.Common.DomainException ex)
        {
            return Result.Failure<RecordSaleInvoicePaymentResponse>(
                Error.Validation("Payment.ExceedsInvoiceTotal", ex.Message));
        }

        // إضافة صريحة لازمة - راجع تعليق الصنف بالأعلى وتعليق
        // RecordPurchaseInvoicePaymentHandler الأصلي للتفصيل الكامل.
        _context.SaleInvoicePayments.Add(newPayment);

        if (paymentMethod.AffectsCashDrawer)
        {
            _context.CashDrawerLogs.Add(new CashDrawerLog(
                invoice.BranchId,
                CashDrawerMovementType.SaleCashIn,
                command.Amount,
                CashDrawerReferenceType.SaleInvoicePayment,
                newPayment.Id,
                userId,
                _dateTimeProvider.UtcNow));
        }

        return await _transactionalExecutor.ExecuteAsync<RecordSaleInvoicePaymentResponse>(async ct =>
        {
            await _context.SaveChangesAsync(ct);

            return Result.Success(new RecordSaleInvoicePaymentResponse(
                newPayment.Id, invoice.TotalPaidAmount, invoice.TotalAmount - invoice.TotalPaidAmount));
        }, cancellationToken);
    }
}
