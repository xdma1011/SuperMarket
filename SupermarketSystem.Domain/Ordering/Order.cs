using SupermarketSystem.Domain.Common;

namespace SupermarketSystem.Domain.Ordering;

public enum OrderStatus
{
    Pending = 1,
    Accepted = 2,
    Completed = 3,
    Rejected = 4
}

/// <summary>
/// أساس ميزة "التطبيق للزبائن" (نقاش صاحب المشروع، مش مبنية عليه أي
/// شاشة تطبيق لسا - هذا الأساس بالباك إند بس). دورة حياة الطلب مقصودة
/// أربع حالات، مش اتنين:
///
///   Pending → Accepted → Completed
///           ↘ Rejected (بأي لحظة قبل Completed، بسبب إلزامي)
///
/// "Accepted" منفصلة عن "Completed" عمدًا: CompleteSaleCommand (فاتورة
/// البيع الحقيقية) بيتطلب سداد كامل فوري - ما بيسمح بفاتورة "غير
/// مدفوعة بعد". طلبات التوصيل عادة Cash on Delivery (الدفع وقت
/// التسليم)، فمافي مبلغ فعلي نقدر نسجّله كفاتورة حقيقية لحظة "الموافقة"
/// - بس لحظة "الإكمال" (لما الكاش يترجع فعليًا) مننشئ SaleInvoice حقيقية
/// (بإعادة استخدام CompleteSaleHandler نفسه، صفر تكرار منطق مخزون/دفع).
/// "Accepted" لسا مفيدة: بتعلّم الكاشير إنه التزم يجهّز الطلب.
///
/// لا تعديل على الأصناف بعد الإنشاء (قرار صاحب المشروع - تعديل أصناف
/// بيبطّئ العملية، فرق السعر/التوفر البسيط واقع مقبول، حل الحالات
/// الاستثنائية = رفض الطلب كامل بسبب واضح).
/// </summary>
public class Order : AuditableEntity, IBranchOwned
{
    public Guid CustomerId { get; private set; }
    public Guid BranchId { get; private set; }
    public OrderStatus Status { get; private set; }

    public string? DeliveryNote { get; private set; }
    public decimal? DeliveryLatitude { get; private set; }
    public decimal? DeliveryLongitude { get; private set; }

    public Guid? DecidedByUserId { get; private set; }
    public DateTime? DecidedAtUtc { get; private set; }
    public string? RejectionReason { get; private set; }

    public Guid? ResultingSaleInvoiceId { get; private set; }

    public int? Rating { get; private set; }
    public string? RatingComment { get; private set; }

    private readonly List<OrderItem> _items = new();
    public IReadOnlyCollection<OrderItem> Items => _items.AsReadOnly();

    private Order() { } // EF Core

    public Order(Guid customerId, Guid branchId, string? deliveryNote, decimal? deliveryLatitude, decimal? deliveryLongitude)
    {
        CustomerId = customerId;
        BranchId = branchId;
        DeliveryNote = deliveryNote;
        DeliveryLatitude = deliveryLatitude;
        DeliveryLongitude = deliveryLongitude;
        Status = OrderStatus.Pending;
    }

    public OrderItem AddItem(Guid productId, Guid productUnitId, decimal quantity, decimal estimatedUnitPrice)
    {
        if (Status != OrderStatus.Pending)
        {
            throw new DomainException("Cannot add items to an order that is not Pending.");
        }

        var item = new OrderItem(Id, productId, productUnitId, quantity, estimatedUnitPrice);
        _items.Add(item);
        return item;
    }

    public void Accept(Guid decidedByUserId, DateTime decidedAtUtc)
    {
        if (Status != OrderStatus.Pending)
        {
            throw new DomainException($"Cannot accept an order with status '{Status}'.");
        }

        Status = OrderStatus.Accepted;
        DecidedByUserId = decidedByUserId;
        DecidedAtUtc = decidedAtUtc;
    }

    public void Reject(Guid decidedByUserId, DateTime decidedAtUtc, string reason)
    {
        if (Status == OrderStatus.Completed || Status == OrderStatus.Rejected)
        {
            throw new DomainException($"Cannot reject an order with status '{Status}'.");
        }

        if (string.IsNullOrWhiteSpace(reason))
        {
            throw new DomainException("A rejection reason is required.");
        }

        Status = OrderStatus.Rejected;
        DecidedByUserId = decidedByUserId;
        DecidedAtUtc = decidedAtUtc;
        RejectionReason = reason;
    }

    public void Complete(Guid resultingSaleInvoiceId)
    {
        if (Status != OrderStatus.Accepted)
        {
            throw new DomainException($"Cannot complete an order with status '{Status}'.");
        }

        Status = OrderStatus.Completed;
        ResultingSaleInvoiceId = resultingSaleInvoiceId;
    }

    public void Rate(int rating, string? comment)
    {
        if (Status != OrderStatus.Completed)
        {
            throw new DomainException("Only a Completed order can be rated.");
        }

        if (Rating is not null)
        {
            throw new DomainException("This order has already been rated.");
        }

        if (rating is < 1 or > 5)
        {
            throw new DomainException("Rating must be between 1 and 5.");
        }

        Rating = rating;
        RatingComment = comment;
    }
}

public class OrderItem : Entity
{
    public Guid OrderId { get; private set; }
    public Guid ProductId { get; private set; }
    public Guid ProductUnitId { get; private set; }
    public decimal Quantity { get; private set; }

    /// <summary>سعر تقديري لحظة الطلب - عرض للزبون بس، غير مُلزِم. السعر الحقيقي يُحسَم من ProductBranch.SellingPrice لحظة Complete (نفس مبدأ CompleteSaleCommand - لا سعر من طرف العميل أبدًا).</summary>
    public decimal EstimatedUnitPrice { get; private set; }

    private OrderItem() { } // EF Core

    internal OrderItem(Guid orderId, Guid productId, Guid productUnitId, decimal quantity, decimal estimatedUnitPrice)
    {
        if (quantity <= 0)
        {
            throw new DomainException("Order item quantity must be positive.");
        }

        OrderId = orderId;
        ProductId = productId;
        ProductUnitId = productUnitId;
        Quantity = quantity;
        EstimatedUnitPrice = estimatedUnitPrice;
    }
}
