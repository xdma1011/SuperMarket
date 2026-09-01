using SupermarketSystem.Domain.Common;

namespace SupermarketSystem.Domain.Purchasing;

public enum PurchaseInvoiceDraftStatus
{
    PendingReview = 1,
    Completed = 2,
    Discarded = 3
}

/// <summary>
/// نتيجة قراءة فاتورة شراء بالذكاء الاصطناعي، بانتظار مراجعة بشرية قبل
/// ما تصير فاتورة شراء فعلية. عمدًا كيان منفصل تمامًا عن PurchaseInvoice -
/// ما بيلمس المخزون ولا التكلفة المرجّحة إطلاقًا لحد ما يوافق مراجع
/// ويكتمل (CompletePurchaseInvoiceDraftHandler بيستخدم نفس محرك
/// CompletePurchaseInvoiceHandler وقتها). الأسطر مخزَّنة كـJSON (ItemsJson)
/// لا جدول فرعي منفصل - بيانات مرحلية للمراجعة بس، مش سجل مالي ثابت
/// يحتاج بنية علائقية صارمة متل PurchaseInvoiceItem.
/// </summary>
public class PurchaseInvoiceDraft : AuditableEntity, IBranchOwned
{
    public Guid BranchId { get; private set; }
    public string ImageReference { get; private set; } = null!;
    public string? ProviderName { get; private set; }

    public string? RawSupplierName { get; private set; }
    public Guid? MatchedSupplierId { get; private set; }
    public string? SupplierInvoiceReference { get; private set; }
    public DateOnly? InvoiceDate { get; private set; }
    public string? Currency { get; private set; }
    public decimal? ExtractedInvoiceTotal { get; private set; }
    public string? ExtractionConfidence { get; private set; }
    public string? WarningsText { get; private set; }

    /// <summary>مصفوفة JSON من عناصر الفاتورة المستخرَجة (وحالة مطابقتها بمنتج فعلي) - راجع DraftItemDto بطبقة Application لشكلها الدقيق.</summary>
    public string ItemsJson { get; private set; } = "[]";

    public PurchaseInvoiceDraftStatus Status { get; private set; }
    public Guid? ResultingPurchaseInvoiceId { get; private set; }

    /// <summary>
    /// كاش (أو أي طريقة دفع تؤثر على الدرج) دُفع فعليًا للمورد لحظة
    /// استلام البضاعة، قبل أي مراجعة - غير قابل للتعديل بعد الإنشاء
    /// عمدًا (لو تغيّر فعليًا، الكاش أصلًا طلع من الدرج، ما في "تراجع").
    /// حركة CashDrawerLog تُكتب فورًا لحظة الرفع (راجع
    /// CreatePurchaseInvoiceDraftFromImageCommand) - هذا الحقلان هون بس
    /// أرشيف يخلي CompletePurchaseInvoiceDraftHandler يعرف يربط دفعة
    /// PurchaseInvoicePayment رسمية بالفاتورة النهائية بلا ما يكرّر
    /// حركة الدرج (مسجَّلة أصلًا).
    /// </summary>
    public decimal? PaidNowAmount { get; private set; }
    public Guid? PaidNowPaymentMethodId { get; private set; }

    public byte[]? RowVersion { get; private set; }

    private PurchaseInvoiceDraft() { } // EF Core

    public PurchaseInvoiceDraft(
        Guid branchId,
        string imageReference,
        string? providerName,
        string? rawSupplierName,
        Guid? matchedSupplierId,
        string? supplierInvoiceReference,
        DateOnly? invoiceDate,
        string? currency,
        decimal? extractedInvoiceTotal,
        string? extractionConfidence,
        string? warningsText,
        string itemsJson,
        decimal? paidNowAmount,
        Guid? paidNowPaymentMethodId)
    {
        BranchId = branchId;
        ImageReference = imageReference;
        ProviderName = providerName;
        RawSupplierName = rawSupplierName;
        MatchedSupplierId = matchedSupplierId;
        SupplierInvoiceReference = supplierInvoiceReference;
        InvoiceDate = invoiceDate;
        Currency = currency;
        ExtractedInvoiceTotal = extractedInvoiceTotal;
        ExtractionConfidence = extractionConfidence;
        WarningsText = warningsText;
        ItemsJson = itemsJson;
        PaidNowAmount = paidNowAmount;
        PaidNowPaymentMethodId = paidNowPaymentMethodId;
        Status = PurchaseInvoiceDraftStatus.PendingReview;
    }

    public void UpdateForReview(Guid? matchedSupplierId, string? supplierInvoiceReference, string itemsJson)
    {
        EnsurePendingReview();
        MatchedSupplierId = matchedSupplierId;
        SupplierInvoiceReference = supplierInvoiceReference;
        ItemsJson = itemsJson;
    }

    public void MarkCompleted(Guid resultingPurchaseInvoiceId)
    {
        EnsurePendingReview();
        Status = PurchaseInvoiceDraftStatus.Completed;
        ResultingPurchaseInvoiceId = resultingPurchaseInvoiceId;
    }

    public void Discard()
    {
        EnsurePendingReview();
        Status = PurchaseInvoiceDraftStatus.Discarded;
    }

    private void EnsurePendingReview()
    {
        if (Status != PurchaseInvoiceDraftStatus.PendingReview)
        {
            throw new DomainException($"Cannot modify a purchase invoice draft with status '{Status}'.");
        }
    }
}
