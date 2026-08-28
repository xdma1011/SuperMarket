using SupermarketSystem.Domain.Common;

namespace SupermarketSystem.Domain.Inventory;

/// <summary>
/// Completed لا يعني إن التصحيحات لمست المخزون فعليًا — هو فقط "العدّ
/// انتهى وتجمّد، الفروقات صارت مرئية". Approved هي الخطوة المنفصلة
/// (طُلبت صراحةً: "الفروقات تظهر قبل الاعتماد النهائي") اللي فيها تُنشأ
/// StockMovement فعليًا وينعدّل رصيد Stock — لحد ما تصير Approved، لا شيء
/// بالمخزون الفعلي تأثّر بهذا الجرد إطلاقًا.
/// </summary>
public enum StocktakeStatus
{
    Draft = 1,
    InProgress = 2,
    Completed = 3,
    Approved = 4,
    Cancelled = 5
}

/// <summary>
/// Aggregate root, branch-owned. جلسة عدّ فعلي — إما على قائمة أصناف
/// مختارة (جرد جزئي، مثلًا جرد مفاجئ على أصناف محددة) أو على كل أصناف
/// الفرع (جرد شامل) — القرار بيصير بالـApplication layer وقت الإنشاء
/// (أي أصناف تُمرَّر لـAddItem)، بلا حاجة لنوعين منفصلين بالـDomain؛ نفس
/// الآلية بالضبط تخدم الحالتين.
///
/// StocktakeNumber: رقم تسلسلي لكل فرع، عبر BranchDocumentSequence —
/// نفس آلية أرقام فواتير البيع/الشراء/الإرجاع بالضبط (حجز ذري، لا
/// IDENTITY عام ولا MAX+1). يُحجز وقت الإنشاء (Draft)، لأنه الجرد جلسة
/// ممتدة زمنيًا (ممكن تاخد أيام)، والفريق محتاج يشير له بالاسم/الرقم من
/// أول لحظة، لا بس بعد الاعتماد النهائي.
/// </summary>
public class Stocktake : AuditableEntity, IBranchOwned, IHasRowVersion
{
    public Guid BranchId { get; private set; }
    public string StocktakeNumber { get; private set; } = null!;
    public StocktakeStatus Status { get; private set; }
    public DateTime? CompletedAtUtc { get; private set; }
    public DateTime? ApprovedAtUtc { get; private set; }
    public Guid? ApprovedByUserId { get; private set; }
    public byte[]? RowVersion { get; private set; }

    private readonly List<StocktakeItem> _items = new();
    public IReadOnlyCollection<StocktakeItem> Items => _items.AsReadOnly();

    private Stocktake() { } // EF Core

    public Stocktake(Guid branchId, string stocktakeNumber)
    {
        BranchId = branchId;
        StocktakeNumber = stocktakeNumber;
        Status = StocktakeStatus.Draft;
    }

    public StocktakeItem AddItem(Guid productId, Guid? productBatchId, decimal expectedQuantity)
    {
        if (Status != StocktakeStatus.Draft && Status != StocktakeStatus.InProgress)
        {
            throw new DomainException("Cannot add items to a stocktake that is not Draft or InProgress.");
        }

        var item = new StocktakeItem(Id, productId, productBatchId, expectedQuantity);
        _items.Add(item);
        return item;
    }

    public void Begin()
    {
        if (Status != StocktakeStatus.Draft)
        {
            throw new DomainException("Only a Draft stocktake can begin.");
        }

        Status = StocktakeStatus.InProgress;
    }

    /// <summary>
    /// يقفل مرحلة العدّ — لا يلمس المخزون. يرفض الإكمال لو في أي صنف
    /// بلا عدّ فعلي (CountedQuantity == null) — إكمال بعدّ ناقص بيخلي
    /// "صفر" و"ما اتعدّ أصلًا" غامضين بلا تمييز.
    /// </summary>
    public void Complete(DateTime utcNow)
    {
        if (Status != StocktakeStatus.InProgress)
        {
            throw new DomainException("Only an InProgress stocktake can be completed.");
        }

        if (_items.Any(i => i.CountedQuantity is null))
        {
            throw new DomainException("Cannot complete a stocktake while items remain uncounted.");
        }

        Status = StocktakeStatus.Completed;
        CompletedAtUtc = utcNow;
    }

    /// <summary>
    /// يعتمد الجرد نهائيًا — الخطوة الوحيدة اللي بعدها التصحيحات تُطبَّق
    /// فعليًا على Stock (بمسؤولية الـApplication layer، خارج هذا الـentity
    /// — نفس نمط PurchaseInvoice.MarkReceived اللي ما بتلمس Stock هي
    /// لحالها، بس بتأشّر إن التطبيق الفعلي مسموح يصير).
    /// </summary>
    public void Approve(Guid approvedByUserId, DateTime approvedAtUtc)
    {
        if (Status != StocktakeStatus.Completed)
        {
            throw new DomainException("Only a Completed stocktake can be approved.");
        }

        Status = StocktakeStatus.Approved;
        ApprovedByUserId = approvedByUserId;
        ApprovedAtUtc = approvedAtUtc;
    }

    public void Cancel()
    {
        if (Status is StocktakeStatus.Approved or StocktakeStatus.Cancelled)
        {
            throw new DomainException($"Cannot cancel a stocktake in status {Status}.");
        }

        Status = StocktakeStatus.Cancelled;
    }
}

/// <summary>
/// Child of the Stocktake aggregate. VarianceQuantity مشتق دائمًا
/// (CountedQuantity - ExpectedQuantity)، غير مخزَّن — بلا حاجة لحماية
/// ذرية عليه (بخلاف عدّادات الدفع/الإرجاع اللي هي فعلًا مخزَّنة لأنها
/// موضوع قيد تزامن حقيقي).
///
/// CountedByUserId/CountedAtUtc: يدعمان الجرد متعدد المستخدمين — كل صنف
/// إله مين عدّه ومتى بشكل مستقل، فمجموعة مستخدمين يقدروا يعدّوا أصناف
/// مختلفة بنفس الجرد بالتوازي، بلا تعارض (كل واحد بيعدّل سطره هو بس).
/// </summary>
public class StocktakeItem : Entity
{
    public Guid StocktakeId { get; private set; }
    public Guid ProductId { get; private set; }
    public Guid? ProductBatchId { get; private set; }
    public decimal ExpectedQuantity { get; private set; }
    public decimal? CountedQuantity { get; private set; }
    public Guid? CountedByUserId { get; private set; }
    public DateTime? CountedAtUtc { get; private set; }

    public decimal? VarianceQuantity => CountedQuantity.HasValue ? CountedQuantity.Value - ExpectedQuantity : null;

    private StocktakeItem() { } // EF Core

    internal StocktakeItem(Guid stocktakeId, Guid productId, Guid? productBatchId, decimal expectedQuantity)
    {
        StocktakeId = stocktakeId;
        ProductId = productId;
        ProductBatchId = productBatchId;
        ExpectedQuantity = expectedQuantity;
    }

    /// <summary>يُستدعى في أي وقت خلال InProgress — آخر استدعاء هو اللي يُعتمد (last write wins) لو صار عدّ مزدوج مصادفةً لنفس الصنف.</summary>
    public void RecordCount(decimal countedQuantity, Guid countedByUserId, DateTime countedAtUtc)
    {
        CountedQuantity = countedQuantity;
        CountedByUserId = countedByUserId;
        CountedAtUtc = countedAtUtc;
    }
}
