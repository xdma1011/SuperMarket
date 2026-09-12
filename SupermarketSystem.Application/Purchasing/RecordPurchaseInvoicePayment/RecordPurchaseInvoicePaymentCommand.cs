using Microsoft.EntityFrameworkCore;
using SupermarketSystem.Application.Common.Interfaces;
using SupermarketSystem.Application.Common.Results;
using SupermarketSystem.Domain.CashManagement;

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
/// المدفوعات ما يتجاوز إجمالي الفاتورة.
///
/// ═══════════════════════════════════════════════════════════════════
/// خطأ إنتاج حقيقي كان هون، اكتُشف عبر اختبارات integration (راجع تقرير
/// الجلسة) - **كل استدعاء لهذا الـEndpoint كان يفشل حتمًا بـ409
/// Concurrency.Conflict**، حتى عبر HTTP حقيقي بطلبين منفصلين تمامًا
/// (إنشاء فاتورة شراء ثم تسجيل دفعة عليها فورًا - أبسط سيناريو استخدام
/// ممكن):
///
/// السبب الجذري (تأكَّد بتفعيل EF Core SQL logging مؤقتًا للتشخيص):
/// `invoice.AddPayment(...)` بيضيف الدفعة الجديدة لمجموعة `_payments`
/// الخاصة بـPurchaseInvoice - بس `invoice` هون كيان **موجود مسبقًا**
/// (Unchanged، جاي من استعلام FirstOrDefaultAsync)، لا كيان جديد
/// (Added) زي حالة CompleteSaleHandler/ProcessReturnHandler (وين
/// AddPayment بتنضاف لفاتورة جديدة بالكامل، فكل العناصر بالرسم البياني
/// بتاخد Added تلقائيًا). لما تنضاف دفعة جديدة لمجموعة كيان أب *موجود
/// أصلًا*، EF Core ما بيستنتج دايمًا Added للطفل الجديد تلقائيًا من
/// تغيّر المجموعة وحده - النتيجة الفعلية كانت EF يبني **UPDATE**
/// لـPurchaseInvoicePayments (`WHERE RowVersion IS NULL`) بدل **INSERT**،
/// وبما إن الصف أصلًا مش موجود (دفعة جديدة لسه ما انحفظت)، الـUPDATE
/// بيأثر على صفر صفوف، فتفشل الدفعة كاملة بـDbUpdateConcurrencyException
/// (تترجم لـ409 بـExceptionHandlingMiddleware) - رغم إن كل القيم (بما
/// فيها RowVersion لفاتورة الشراء نفسها) كانت صحيحة تمامًا.
///
/// الإصلاح: `_context.PurchaseInvoicePayments.Add(newPayment)` صراحة -
/// يجبر EF يتعامل معها كـAdded من البداية، بدل الاعتماد فقط على تتبّع
/// تغيّر مجموعة الكيان الأب الموجود مسبقًا. أضفنا أيضًا لف الحفظ
/// بـITransactionalExecutor (كان يستدعي SaveChangesAsync مباشرة، خلافًا
/// لكل الـHandlers الشقيقة الحسّاسة) - تحسين اتساق إضافي، لا الإصلاح
/// الجذري نفسه.
/// </summary>
public sealed class RecordPurchaseInvoicePaymentHandler
{
    private readonly IApplicationDbContext _context;
    private readonly ICurrentUserContext _currentUser;
    private readonly IDateTimeProvider _dateTimeProvider;
    private readonly ITransactionalExecutor _transactionalExecutor;

    public RecordPurchaseInvoicePaymentHandler(
        IApplicationDbContext context, ICurrentUserContext currentUser, IDateTimeProvider dateTimeProvider,
        ITransactionalExecutor transactionalExecutor)
    {
        _context = context;
        _currentUser = currentUser;
        _dateTimeProvider = dateTimeProvider;
        _transactionalExecutor = transactionalExecutor;
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

        var paymentMethod = await _context.PaymentMethods.AsNoTracking()
            .FirstOrDefaultAsync(pm => pm.Id == command.PaymentMethodId && pm.IsActive, cancellationToken);
        if (paymentMethod is null)
        {
            return Result.Failure<RecordPurchaseInvoicePaymentResponse>(
                Error.NotFound("Payment.MethodNotFound", $"طريقة الدفع '{command.PaymentMethodId}' غير موجودة أو غير فعّالة."));
        }

        var userId = _currentUser.UserId
            ?? throw new InvalidOperationException("لا يمكن تسجيل دفعة بلا هوية مستخدم مصادَق عليها.");

        Domain.Purchasing.PurchaseInvoicePayment newPayment;
        try
        {
            newPayment = invoice.AddPayment(
                command.PaymentMethodId, command.Amount, userId, invoice.BranchId,
                command.ExternalReference, command.ClientRequestId);
        }
        catch (Domain.Common.DomainException ex)
        {
            return Result.Failure<RecordPurchaseInvoicePaymentResponse>(
                Error.Validation("Payment.ExceedsInvoiceTotal", ex.Message));
        }

        // إضافة صريحة لازمة (راجع تعليق الصنف بالأعلى) - invoice كيان
        // موجود مسبقًا (Unchanged)، فـEF ما بيضمن استنتاج Added تلقائيًا
        // للطفل الجديد بمجرد إضافته لمجموعة Payments بالذاكرة.
        _context.PurchaseInvoicePayments.Add(newPayment);

        // نفس مبدأ CompleteSaleCommand حرفيًا: الاعتماد على سلوك طريقة
        // الدفع (AffectsCashDrawer)، لا اسمها أو كودها (§16.2).
        if (paymentMethod.AffectsCashDrawer)
        {
            _context.CashDrawerLogs.Add(new CashDrawerLog(
                invoice.BranchId,
                CashDrawerMovementType.PurchasePaymentCashOut,
                command.Amount,
                CashDrawerReferenceType.PurchaseInvoicePayment,
                newPayment.Id,
                userId,
                _dateTimeProvider.UtcNow));
        }

        return await _transactionalExecutor.ExecuteAsync<RecordPurchaseInvoicePaymentResponse>(async ct =>
        {
            await _context.SaveChangesAsync(ct);

            return Result.Success(new RecordPurchaseInvoicePaymentResponse(
                newPayment.Id, invoice.TotalPaidAmount, invoice.TotalAmount - invoice.TotalPaidAmount));
        }, cancellationToken);
    }
}
