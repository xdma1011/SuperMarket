using SupermarketSystem.Domain.Common;

namespace SupermarketSystem.Domain.Customers;

public enum LoyaltyPointsReason
{
    EarnedFromOrder = 1,
    Redeemed = 2
}

/// <summary>
/// سطر دفتر نقاط ولاء واحد - append-only، نفس فلسفة CashDrawerLog بالضبط:
/// الرصيد الحالي *محسوب* (مجموع Points لكل سطور الزبون)، لا عمود مخزَّن
/// قابل للتعديل المباشر. هذا يخلي كل حركة نقاط قابلة للتدقيق الكامل
/// بلا استثناء، ويمنع تلاعب صامت برصيد.
/// </summary>
public class CustomerLoyaltyPointsEntry : Entity
{
    public Guid CustomerId { get; private set; }

    /// <summary>موجب = اكتساب، سالب = استبدال (راجع LoyaltyPointsReason).</summary>
    public int Points { get; private set; }

    public LoyaltyPointsReason Reason { get; private set; }
    public Guid? OrderId { get; private set; }
    public DateTime CreatedAtUtc { get; private set; }

    private CustomerLoyaltyPointsEntry() { } // EF Core

    public CustomerLoyaltyPointsEntry(Guid customerId, int points, LoyaltyPointsReason reason, Guid? orderId, DateTime createdAtUtc)
    {
        CustomerId = customerId;
        Points = points;
        Reason = reason;
        OrderId = orderId;
        CreatedAtUtc = createdAtUtc;
    }
}
