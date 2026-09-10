using SupermarketSystem.Domain.Common;

namespace SupermarketSystem.Domain.Inventory;

public enum StockTransferStatus
{
    Dispatched = 1,
    Received = 2
}

/// <summary>
/// Aggregate root - نقل بضاعة بين فرعين، على خطوتين حقيقيتين منفصلتين
/// بالوقت (كانت مفقودة كليًا، مع إنه MovementType.TransferIn/TransferOut
/// موجودتان بالـenum من زمان بلا أي كود يستخدمهما):
///
///   1) الإرسال (الإنشاء) - البضاعة طلعت فعليًا من الفرع المصدر، تُخصَم
///      فورًا من مخزونه. الحالة Dispatched.
///   2) الاستلام - البضاعة وصلت فعليًا للفرع الوجهة، تُضاف فورًا لمخزونه.
///      الحالة Received.
///
/// الفجوة الزمنية بين الخطوتين مقصودة ومُتعمَّدة - البضاعة "بالطريق"،
/// مش موجودة بمخزون أي فرع لحظيًا (نفس واقع النقل الفعلي بالشاحنة). هذا
/// بالضبط سبب وجود خطوتين لا خطوة وحدة "نقل مباشر".
/// </summary>
public class StockTransfer : AuditableEntity, IHasRowVersion
{
    public Guid SourceBranchId { get; private set; }
    public Guid DestinationBranchId { get; private set; }
    public string TransferNumber { get; private set; } = null!;
    public StockTransferStatus Status { get; private set; }
    public Guid DispatchedByUserId { get; private set; }
    public DateTime DispatchedAtUtc { get; private set; }
    public Guid? ReceivedByUserId { get; private set; }
    public DateTime? ReceivedAtUtc { get; private set; }
    public byte[]? RowVersion { get; private set; }

    private readonly List<StockTransferItem> _items = new();
    public IReadOnlyCollection<StockTransferItem> Items => _items.AsReadOnly();

    private StockTransfer() { } // EF Core

    public StockTransfer(
        Guid sourceBranchId, Guid destinationBranchId, string transferNumber,
        Guid dispatchedByUserId, DateTime dispatchedAtUtc)
    {
        if (sourceBranchId == destinationBranchId)
        {
            throw new DomainException("Source and destination branches must be different.");
        }

        SourceBranchId = sourceBranchId;
        DestinationBranchId = destinationBranchId;
        TransferNumber = transferNumber;
        Status = StockTransferStatus.Dispatched;
        DispatchedByUserId = dispatchedByUserId;
        DispatchedAtUtc = dispatchedAtUtc;
    }

    /// <summary>
    /// BatchNumber/ExpiryDate صورة (snapshot) وقت الإرسال - الفرع الوجهة
    /// ممكن ما يكون عنده أصلًا دفعة بنفس الرقم لحد الاستلام، فلازم المعلومة
    /// تترافق بالسطر نفسه لحتى نقدر ننشئها هناك وقتها (راجع ReceiveStockTransferCommand).
    /// </summary>
    public StockTransferItem AddItem(
        Guid productId, Guid productUnitId, decimal quantityBase,
        Guid? sourceProductBatchId, string? batchNumber, DateOnly? batchExpiryDate)
    {
        var item = new StockTransferItem(Id, productId, productUnitId, quantityBase, sourceProductBatchId, batchNumber, batchExpiryDate);
        _items.Add(item);
        return item;
    }

    public void MarkReceived(Guid receivedByUserId, DateTime receivedAtUtc)
    {
        if (Status != StockTransferStatus.Dispatched)
        {
            throw new DomainException("Only a dispatched transfer can be received.");
        }

        Status = StockTransferStatus.Received;
        ReceivedByUserId = receivedByUserId;
        ReceivedAtUtc = receivedAtUtc;
    }
}

/// <summary>Child of the StockTransfer aggregate - سطر واحد لكل (منتج، دفعة اختيارية) منقول.</summary>
public class StockTransferItem : Entity
{
    public Guid StockTransferId { get; private set; }
    public Guid ProductId { get; private set; }
    public Guid ProductUnitId { get; private set; }
    public decimal QuantityBase { get; private set; }
    public Guid? SourceProductBatchId { get; private set; }
    public string? BatchNumber { get; private set; }
    public DateOnly? BatchExpiryDate { get; private set; }

    /// <summary>يُعبَّى وقت الاستلام فقط - الدفعة المطابقة (موجودة أو جديدة) بالفرع الوجهة.</summary>
    public Guid? DestinationProductBatchId { get; private set; }

    private StockTransferItem() { } // EF Core

    internal StockTransferItem(
        Guid stockTransferId, Guid productId, Guid productUnitId, decimal quantityBase,
        Guid? sourceProductBatchId, string? batchNumber, DateOnly? batchExpiryDate)
    {
        if (quantityBase <= 0)
        {
            throw new DomainException("Transfer quantity must be positive.");
        }

        StockTransferId = stockTransferId;
        ProductId = productId;
        ProductUnitId = productUnitId;
        QuantityBase = quantityBase;
        SourceProductBatchId = sourceProductBatchId;
        BatchNumber = batchNumber;
        BatchExpiryDate = batchExpiryDate;
    }

    public void SetDestinationBatch(Guid destinationProductBatchId) => DestinationProductBatchId = destinationProductBatchId;
}
