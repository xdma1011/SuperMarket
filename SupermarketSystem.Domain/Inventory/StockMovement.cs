using SupermarketSystem.Domain.Common;

namespace SupermarketSystem.Domain.Inventory;

public enum MovementType
{
    PurchaseIn = 1,
    SaleOut = 2,
    ReturnIn = 3,
    /// <summary>Void of a completed sale — reverses stock, distinct from a customer ReturnIn. See Architecture Review §13.4.</summary>
    VoidReversal = 4,
    AdjustmentIn = 5,
    AdjustmentOut = 6,
    TransferIn = 7,
    TransferOut = 8,
    /// <summary>الجرد لقى كمية أكتر من المتوقع.</summary>
    StocktakeCorrectionIncrease = 9,
    /// <summary>الجرد لقى كمية أقل من المتوقع.</summary>
    StocktakeCorrectionDecrease = 10,
    /// <summary>ضيافة أو استهلاك داخلي — بضاعة خرجت من المخزون بلا أي قيد مالي كإيراد (بخلاف SaleOut). سبب اختياري يوضّح لمين/ليش.</summary>
    ComplimentaryOut = 11,
    /// <summary>تلف/هلاك — بضاعة خرجت من المخزون بسبب تلف فعلي (انتهاء صلاحية، كسر، تلف تخزين...)، منفصل تصنيفًا عن الضيافة (ComplimentaryOut) رغم تشابه الآلية. سبب إلزامي (WasteReason) يوضّح السبب.</summary>
    WasteOut = 12
}

/// <summary>
/// Which kind of document a StockMovement's loose ReferenceId points at.
/// See the class remarks on StockMovement for why this is a loose reference
/// rather than four/five separate nullable FK columns.
/// </summary>
public enum StockMovementReferenceType
{
    SaleInvoiceItem = 1,
    PurchaseInvoiceItem = 2,
    ReturnInvoiceItem = 3,
    StocktakeItem = 4,
    ManualAdjustment = 5,
    StockTransferItem = 6
}

/// <summary>سبب التلف/الهلاك - إلزامي على كل حركة WasteOut (بخلاف Reason الحر بـStockMovement، اختياري لتفاصيل إضافية).</summary>
public enum WasteReason
{
    Expired = 1,
    Broken = 2,
    StorageDamage = 3,
    Other = 4
}

/// <summary>
/// Aggregate root. The append-only historical ledger — source of truth for
/// "what happened" to stock. Never updated or deleted after creation.
///
/// ReferenceType + ReferenceId is a deliberate loose reference rather than
/// four/five separate nullable FK columns: SQL Server cannot express "FK to
/// one of several different tables" as a single constraint, and five
/// mostly-null FK columns would be worse. The accepted trade-off: the
/// database cannot verify ReferenceId actually exists in the table implied
/// by ReferenceType, or that the pairing is consistent with MovementType.
/// Both are validated in application-service code before the row is
/// written. This is safe because StockMovement has no generic/public write
/// path — only trusted internal application services ever insert rows here
/// (Architecture Review §8).
///
/// QuantityBase is always normalized to the product's base unit via
/// ProductUnit.ConversionFactorToBase, so aggregation across movements
/// recorded in different units stays consistent (Architecture Review §12).
/// </summary>
public class StockMovement : Entity, IBranchOwned
{
    public Guid ProductId { get; private set; }
    public Guid BranchId { get; private set; }
    public Guid ProductUnitId { get; private set; }
    public Guid? ProductBatchId { get; private set; }
    public decimal QuantityBase { get; private set; }
    public MovementType MovementType { get; private set; }
    public string? Reason { get; private set; }
    public DateTime OccurredAtUtc { get; private set; }
    public Guid UserId { get; private set; }
    public StockMovementReferenceType ReferenceType { get; private set; }
    public Guid ReferenceId { get; private set; }

    /// <summary>
    /// "سماح مع مراجعة" — نفس فلسفة النظام كله (لا توقيف عملية، بس
    /// تعليمها للمراجعة اللاحقة). حاليًا يُستخدم بس لـComplimentaryOut
    /// اللي تجاوز حد الكمية اليومي (راجع RecordComplimentaryIssueHandler)،
    /// بس عام عمدًا — أي نوع حركة لاحق يحتاج نفس النمط ما بيحتاج تغيير
    /// هيكلي، بس تفعيل NeedsReview بمكانه.
    /// </summary>
    public bool NeedsReview { get; private set; }
    public DateTime? ReviewedAtUtc { get; private set; }
    public Guid? ReviewedByUserId { get; private set; }

    /// <summary>
    /// ذو معنى فقط لـMovementType.WasteOut - null لكل نوع حركة تاني. تصنيف
    /// إلزامي منفصل عن Reason الحر (اللي يبقى اختياريًا لتفاصيل إضافية)،
    /// عشان يصير ممكن تجميع/تقرير أسباب التلف (منتهي، مكسور، تلف تخزين...)
    /// بلا الاعتماد على parsing نص حر.
    /// </summary>
    public WasteReason? WasteReason { get; private set; }

    private StockMovement() { } // EF Core

    public StockMovement(
        Guid productId,
        Guid branchId,
        Guid productUnitId,
        Guid? productBatchId,
        decimal quantityBase,
        MovementType movementType,
        string? reason,
        DateTime occurredAtUtc,
        Guid userId,
        StockMovementReferenceType referenceType,
        Guid referenceId,
        bool needsReview = false,
        WasteReason? wasteReason = null)
    {
        if (quantityBase <= 0)
        {
            throw new DomainException("StockMovement quantity must be positive; direction is expressed by MovementType, not sign.");
        }

        if (movementType == MovementType.WasteOut && wasteReason is null)
        {
            throw new DomainException("WasteOut requires a WasteReason.");
        }

        NeedsReview = needsReview;
        ProductId = productId;
        BranchId = branchId;
        ProductUnitId = productUnitId;
        ProductBatchId = productBatchId;
        QuantityBase = quantityBase;
        MovementType = movementType;
        Reason = reason;
        OccurredAtUtc = occurredAtUtc;
        UserId = userId;
        ReferenceType = referenceType;
        ReferenceId = referenceId;
        WasteReason = wasteReason;
    }

    public void MarkReviewed(Guid reviewedByUserId, DateTime reviewedAtUtc)
    {
        if (ReviewedAtUtc is not null)
        {
            throw new DomainException("This movement has already been reviewed.");
        }

        NeedsReview = false;
        ReviewedByUserId = reviewedByUserId;
        ReviewedAtUtc = reviewedAtUtc;
    }
}
