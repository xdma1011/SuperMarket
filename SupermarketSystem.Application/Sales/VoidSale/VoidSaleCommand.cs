using Microsoft.EntityFrameworkCore;
using SupermarketSystem.Application.Common.Interfaces;
using SupermarketSystem.Application.Common.Policies;
using SupermarketSystem.Application.Common.Results;
using SupermarketSystem.Domain.CashManagement;
using SupermarketSystem.Domain.Identity;
using SupermarketSystem.Domain.Inventory;
using SupermarketSystem.Domain.Payments;
using SupermarketSystem.Domain.Sales;

namespace SupermarketSystem.Application.Sales.VoidSale;

public sealed record VoidSaleCommand(Guid SaleInvoiceId, VoidReason Reason, string? Notes);

public sealed record VoidSaleResponse(
    Guid SaleInvoiceId,
    string InvoiceNumber,
    int StockMovementsReversed,
    int PaymentsReversed,
    decimal CashReturnedToDrawer);

/// <summary>
/// إلغاء فاتورة بيع مكتملة — الفاتورة *لا تُحذف أبدًا*، بتضل موجودة
/// بحالة Voided مع سبب ومنفِّذ ووقت (§13.1)، وبيتم عكس آثارها الثلاثة:
/// المخزون، الدفعات، والدرج.
///
/// ═══════════════════════════════════════════════════════════════════
/// قرار تصميم مهم: نعكس حركات المخزون *الفعلية المسجَّلة*، لا نعيد
/// حسابها من أسطر الفاتورة.
/// ═══════════════════════════════════════════════════════════════════
/// السبب مش أسلوبي، هو ضرورة: SaleInvoiceItem ما بتخزّن ProductBatchId
/// إطلاقًا (تحققت من الـDomain) — الدفعة اللي انخصمت فعليًا مسجَّلة
/// بـStockMovement بس. فلو حاولنا نعيد الحساب من أسطر الفاتورة، لمنتج
/// متتبَّع بالدفعات، ما رح نعرف لأي دفعة نرجّع الكمية، ورح نرجّعها لدفعة
/// غلط أو بلا دفعة إطلاقًا — فساد صامت بالمخزون.
///
/// قراءة StockMovement الأصلية بتعطينا بالضبط: نفس المنتج، نفس الوحدة،
/// نفس الدفعة، نفس الكمية بالوحدة الأساسية — نعكسها حرفيًا. وهذا كمان
/// بيتفادى إعادة حساب تحويل الوحدات (احتمال اختلاف لو تغيّر
/// ConversionFactorToBase بعد البيعة).
///
/// اتجاه العملية زيادة دائمًا (رجوع بضاعة للرف) — فبتستخدم
/// Stock.Increase() العادي، لا الخصم الذري الشرطي؛ ما في خطر "بيع
/// مضاعف" لما تكون بتزيد.
/// </summary>
public sealed class VoidSaleHandler
{
    private readonly IApplicationDbContext _context;
    private readonly ITransactionalExecutor _transactionalExecutor;
    private readonly IPosPolicyService _posPolicyService;
    private readonly INotificationDispatcher _notificationDispatcher;
    private readonly ICurrentUserContext _currentUser;
    private readonly IDateTimeProvider _dateTimeProvider;

    public VoidSaleHandler(
        IApplicationDbContext context,
        ITransactionalExecutor transactionalExecutor,
        IPosPolicyService posPolicyService,
        INotificationDispatcher notificationDispatcher,
        ICurrentUserContext currentUser,
        IDateTimeProvider dateTimeProvider)
    {
        _context = context;
        _transactionalExecutor = transactionalExecutor;
        _posPolicyService = posPolicyService;
        _notificationDispatcher = notificationDispatcher;
        _currentUser = currentUser;
        _dateTimeProvider = dateTimeProvider;
    }

    public async Task<Result<VoidSaleResponse>> HandleAsync(VoidSaleCommand command, CancellationToken cancellationToken)
    {
        // فحص السياسة أولًا — قرار فوري من إعدادات مخزّنة بالكاش، لا انتظار
        // موافقة مدير إطلاقًا (§13/§16.7). الرفض هون جواب نهائي فوري.
        var decision = await _posPolicyService.EvaluateAsync(PosOperation.VoidSale, cancellationToken);
        if (!decision.IsAllowed)
        {
            return Result.Failure<VoidSaleResponse>(
                Error.Forbidden("Sale.VoidDenied", decision.Reason ?? "إلغاء الفواتير غير مسموح حاليًا."));
        }

        var invoice = await _context.SaleInvoices
            .Include(s => s.Items)
            .Include(s => s.Payments)
            .FirstOrDefaultAsync(s => s.Id == command.SaleInvoiceId, cancellationToken);

        if (invoice is null)
        {
            return Result.Failure<VoidSaleResponse>(
                Error.NotFound("Sale.NotFound", $"الفاتورة '{command.SaleInvoiceId}' غير موجودة."));
        }

        // فحص مبكر بنفس شروط الـDomain — عشان نرجّع خطأ واضح قبل ما نبدأ
        // أي شغل، بدل ما نكتشفها باستثناء بنص المعاملة. الـDomain بيضل
        // هو الحارس النهائي (invoice.Void() بترفض برضو).
        if (invoice.Status != SaleInvoiceStatus.Completed)
        {
            return Result.Failure<VoidSaleResponse>(Error.BusinessRule(
                "Sale.NotVoidable", $"لا يمكن إلغاء فاتورة بحالة {invoice.Status}؛ الإلغاء متاح فقط لفاتورة مكتملة."));
        }

        if (invoice.TotalReturnedAmount > 0)
        {
            return Result.Failure<VoidSaleResponse>(Error.BusinessRule(
                "Sale.HasReturns", "لا يمكن إلغاء فاتورة سُجِّل عليها إرجاع؛ يمكن فقط إكمال إرجاعها."));
        }

        var itemIds = invoice.Items.Select(i => i.Id).ToList();

        // حركات المخزون الأصلية لهذه الفاتورة — مصدر الحقيقة للعكس.
        var originalMovements = await _context.StockMovements.AsNoTracking()
            .Where(m => m.ReferenceType == StockMovementReferenceType.SaleInvoiceItem
                        && itemIds.Contains(m.ReferenceId)
                        && m.MovementType == MovementType.SaleOut)
            .ToListAsync(cancellationToken);

        // طرق الدفع — لمعرفة أي دفعة تؤثر على الدرج فعليًا (بالسلوك، لا بالاسم).
        var completedPayments = invoice.Payments.Where(p => p.Status == PaymentStatus.Completed).ToList();
        var paymentMethodIds = completedPayments.Select(p => p.PaymentMethodId).Distinct().ToList();
        var cashAffectingMethodIds = await _context.PaymentMethods.AsNoTracking()
            .Where(pm => paymentMethodIds.Contains(pm.Id) && pm.AffectsCashDrawer)
            .Select(pm => pm.Id)
            .ToListAsync(cancellationToken);

        var actorUserId = _currentUser.UserId ?? User.SystemUserId;
        var occurredAtUtc = _dateTimeProvider.UtcNow;
        var voidReasonText = $"إلغاء فاتورة {invoice.InvoiceNumber}";

        var result = await _transactionalExecutor.ExecuteAsync<VoidSaleResponse>(async ct =>
        {
            var reversalMovements = new List<StockMovement>();

            // === 1. عكس المخزون ===
            foreach (var original in originalMovements)
            {
                var stock = await GetOrCreateTrackedStockAsync(
                    original.ProductId, original.BranchId, original.ProductBatchId, ct);

                stock.Increase(original.QuantityBase);

                // VoidReversal لا ReturnIn — الاثنين بيرجّعوا بضاعة للرف، بس
                // التمييز بينهم مقصود (§13.4): "الزبون رجّع بضاعة" شي،
                // و"البيعة ما كان لازم تصير أصلًا" شي تاني تمامًا بالتقارير.
                reversalMovements.Add(new StockMovement(
                    original.ProductId,
                    original.BranchId,
                    original.ProductUnitId,
                    original.ProductBatchId,
                    original.QuantityBase,
                    MovementType.VoidReversal,
                    voidReasonText,
                    occurredAtUtc,
                    actorUserId,
                    StockMovementReferenceType.SaleInvoiceItem,
                    original.ReferenceId));
            }

            // === 2. عكس الدفعات + حركات الدرج التعويضية ===
            var cashMovements = new List<CashDrawerLog>();
            var cashReturned = 0m;

            foreach (var payment in completedPayments)
            {
                // عبر الـaggregate لا الدفعة مباشرة — يضمن تحديث
                // TotalPaidAmount بنفس العملية (راجع SaleInvoice.ReversePayment).
                invoice.ReversePayment(payment.Id, actorUserId, occurredAtUtc, voidReasonText);

                if (cashAffectingMethodIds.Contains(payment.PaymentMethodId))
                {
                    cashReturned += payment.Amount;

                    cashMovements.Add(new CashDrawerLog(
                        payment.BranchId,
                        CashDrawerMovementType.PaymentReversalCashOut,
                        payment.Amount,
                        // يشير للدفعة نفسها لا للفاتورة — يحافظ على سلسلة
                        // التدقيق الدقيقة: فاتورة ← دفعة ← حركة درج (§16.6).
                        CashDrawerReferenceType.SaleInvoicePayment,
                        payment.Id,
                        actorUserId,
                        occurredAtUtc));
                }
            }

            // === 3. تحويل حالة الفاتورة (الحارس النهائي بالـDomain) ===
            invoice.Void(actorUserId, occurredAtUtc, command.Reason, command.Notes);

            _context.StockMovements.AddRange(reversalMovements);
            _context.CashDrawerLogs.AddRange(cashMovements);

            await _context.SaveChangesAsync(ct);

            return Result.Success(new VoidSaleResponse(
                invoice.Id, invoice.InvoiceNumber, reversalMovements.Count, completedPayments.Count, cashReturned));
        }, cancellationToken);

        // التنبيه بعد التزام المعاملة فعليًا، لا أثناءها — نفس التحذير
        // الموثّق بـNotificationDispatcher (تفاديًا لتنبيه عن شي انعكس).
        // الإلغاء عملية حساسة دائمًا، فبينبعث تنبيه بلا شرط قيمة.
        if (result.IsSuccess)
        {
            await _notificationDispatcher.NotifyAsync(
                $"إلغاء فاتورة — {result.Value.InvoiceNumber}",
                $"السبب: {command.Reason}\nحركات مخزون معكوسة: {result.Value.StockMovementsReversed}\n" +
                $"دفعات معكوسة: {result.Value.PaymentsReversed}\nكاش أُعيد للدرج: {result.Value.CashReturnedToDrawer:F2}",
                cancellationToken);
        }

        return result;
    }

    private async Task<Stock> GetOrCreateTrackedStockAsync(
        Guid productId, Guid branchId, Guid? productBatchId, CancellationToken cancellationToken)
    {
        var existing = await _context.Stocks.FirstOrDefaultAsync(
            s => s.ProductId == productId && s.BranchId == branchId && s.ProductBatchId == productBatchId,
            cancellationToken);

        if (existing is not null)
        {
            return existing;
        }

        // ممكن الصف يكون انحذف/ما وُجد أصلًا (حالة نادرة، مثلًا بيعة صارت
        // برصيد سالب لمنتج ما إله صف Stock). ننشئه بصفر ثم نزيد — البضاعة
        // رجعت فعليًا للرف، لازم تنعكس بالمخزون بغض النظر.
        var created = new Stock(productId, branchId, productBatchId);
        _context.Stocks.Add(created);
        return created;
    }
}
