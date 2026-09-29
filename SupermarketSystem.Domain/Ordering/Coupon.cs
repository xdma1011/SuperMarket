using SupermarketSystem.Domain.Common;

namespace SupermarketSystem.Domain.Ordering;

public enum CouponDiscountType
{
    /// <summary>مبلغ ثابت (مثلًا 1.000 د.أ).</summary>
    FixedAmount = 1,

    /// <summary>نسبة من الطلب (مع سقف اختياري).</summary>
    Percent = 2
}

/// <summary>
/// كوبون خصم لتطبيق الزبائن (29/9/2026، "ضيفها عادي وانا اللي ببعثها يدوي او للكل"): صاحب المحل بيعمل كود - لزبون
/// معيّن (CustomerId) أو للكل (null) - وبيبعته إشعار يدويًا. الزبون بيكتب الكود وقت الطلب؛ الخصم بينحجز مع الطلب،
/// بيرجع لو الطلب انرفض، وبيتثبّت وقت التسليم كخصم على الفاتورة (نفس خانة خصم الفاتورة - فكل التقارير والربح
/// بتحسبه صح بلا أي منطق إضافي). على مستوى الشركة (مش لكل فرع).
/// </summary>
public class Coupon : AuditableEntity
{
    public const int MinCodeLength = 4;
    public const int MaxCodeLength = 20;
    public const int MaxTitleLength = 200;

    public string Code { get; private set; } = null!;
    public string Title { get; private set; } = null!;
    public CouponDiscountType DiscountType { get; private set; }

    /// <summary>المبلغ (د.أ) أو النسبة (1-100) حسب DiscountType.</summary>
    public decimal Value { get; private set; }

    /// <summary>سقف الخصم للنسبة (اختياري).</summary>
    public decimal? MaxDiscountAmount { get; private set; }

    /// <summary>أقل مبلغ طلب (تقديري وقت الطلب) - 0 = بلا حد.</summary>
    public decimal MinOrderAmount { get; private set; }

    public DateTime StartAtUtc { get; private set; }
    public DateTime EndAtUtc { get; private set; }

    /// <summary>null = للكل؛ غير هيك الكوبون لهالزبون بس.</summary>
    public Guid? CustomerId { get; private set; }

    public int MaxUsesPerCustomer { get; private set; }

    /// <summary>حد الاستعمال الكلي (اختياري) - مثلًا أول 50 زبون.</summary>
    public int? MaxTotalUses { get; private set; }

    public bool IsActive { get; private set; }

    public DateTime? LastSentAtUtc { get; private set; }

    private Coupon() { } // EF Core

    public Coupon(
        string code, string title, CouponDiscountType discountType, decimal value, decimal? maxDiscountAmount, decimal minOrderAmount,
        DateTime startAtUtc, DateTime endAtUtc, Guid? customerId, int maxUsesPerCustomer, int? maxTotalUses)
    {
        Code = NormalizeCode(code) ?? throw new DomainException("Coupon code is invalid.");
        CustomerId = customerId;
        IsActive = true;
        Update(title, discountType, value, maxDiscountAmount, minOrderAmount, startAtUtc, endAtUtc, maxUsesPerCustomer, maxTotalUses);
    }

    /// <summary>الكود بحروف كبيرة، حروف إنجليزية وأرقام وشرطة بس - null = غير صالح.</summary>
    public static string? NormalizeCode(string? raw)
    {
        if (string.IsNullOrWhiteSpace(raw))
        {
            return null;
        }

        var code = raw.Trim().ToUpperInvariant();
        return code.Length is >= MinCodeLength and <= MaxCodeLength && code.All(ch => ch is (>= 'A' and <= 'Z') or (>= '0' and <= '9') or '-')
            ? code
            : null;
    }

    public void Update(
        string title, CouponDiscountType discountType, decimal value, decimal? maxDiscountAmount, decimal minOrderAmount,
        DateTime startAtUtc, DateTime endAtUtc, int maxUsesPerCustomer, int? maxTotalUses)
    {
        if (string.IsNullOrWhiteSpace(title) || title.Trim().Length > MaxTitleLength)
        {
            throw new DomainException("Coupon title is required.");
        }

        if (!Enum.IsDefined(discountType))
        {
            throw new DomainException("Coupon discount type is invalid.");
        }

        if (value <= 0 || (discountType == CouponDiscountType.Percent && value > 100))
        {
            throw new DomainException("Coupon value is invalid.");
        }

        if (maxDiscountAmount is <= 0 || minOrderAmount < 0 || endAtUtc <= startAtUtc || maxUsesPerCustomer < 1 || maxTotalUses is < 1)
        {
            throw new DomainException("Coupon limits are invalid.");
        }

        Title = title.Trim();
        DiscountType = discountType;
        Value = value;
        MaxDiscountAmount = discountType == CouponDiscountType.Percent ? maxDiscountAmount : null;
        MinOrderAmount = minOrderAmount;
        StartAtUtc = startAtUtc;
        EndAtUtc = endAtUtc;
        MaxUsesPerCustomer = maxUsesPerCustomer;
        MaxTotalUses = maxTotalUses;
    }

    public void SetActive(bool isActive) => IsActive = isActive;

    public void MarkSent(DateTime sentAtUtc) => LastSentAtUtc = sentAtUtc;

    public bool IsWithinPeriod(DateTime utcNow) => utcNow >= StartAtUtc && utcNow < EndAtUtc;

    /// <summary>خصم هالكوبون على مبلغ (مقرّب لفلس) - ما بيتجاوز المبلغ نفسه أبدًا.</summary>
    public decimal DiscountFor(decimal amount)
    {
        if (amount <= 0)
        {
            return 0m;
        }

        var discount = DiscountType == CouponDiscountType.FixedAmount ? Value : amount * Value / 100m;
        if (MaxDiscountAmount is { } cap)
        {
            discount = Math.Min(discount, cap);
        }

        return Math.Round(Math.Min(discount, amount), 3, MidpointRounding.AwayFromZero);
    }
}

public enum CouponRedemptionStatus
{
    /// <summary>انحجز مع طلب لسا ما انسلّم.</summary>
    Reserved = 1,

    /// <summary>انطبق على فاتورة التسليم.</summary>
    Redeemed = 2,

    /// <summary>الطلب انرفض - الاستعمال رجع للزبون.</summary>
    Released = 3
}

public class CouponRedemption : Entity
{
    public Guid CouponId { get; private set; }
    public Guid CustomerId { get; private set; }
    public Guid OrderId { get; private set; }
    public CouponRedemptionStatus Status { get; private set; }

    /// <summary>الخصم التقديري وقت الطلب (على الأسعار وقتها).</summary>
    public decimal EstimatedDiscountAmount { get; private set; }

    /// <summary>الخصم الفعلي على فاتورة التسليم (بأسعار لحظة التسليم).</summary>
    public decimal? DiscountAmount { get; private set; }

    public Guid? SaleInvoiceId { get; private set; }
    public DateTime ReservedAtUtc { get; private set; }
    public DateTime? ClosedAtUtc { get; private set; }

    private CouponRedemption() { } // EF Core

    public CouponRedemption(Guid couponId, Guid customerId, Guid orderId, decimal estimatedDiscountAmount, DateTime reservedAtUtc)
    {
        CouponId = couponId;
        CustomerId = customerId;
        OrderId = orderId;
        EstimatedDiscountAmount = estimatedDiscountAmount;
        ReservedAtUtc = reservedAtUtc;
        Status = CouponRedemptionStatus.Reserved;
    }

    public void Redeem(Guid saleInvoiceId, decimal discountAmount, DateTime atUtc)
    {
        if (Status != CouponRedemptionStatus.Reserved)
        {
            throw new DomainException("Only a reserved coupon can be redeemed.");
        }

        Status = CouponRedemptionStatus.Redeemed;
        SaleInvoiceId = saleInvoiceId;
        DiscountAmount = discountAmount;
        ClosedAtUtc = atUtc;
    }

    public void Release(DateTime atUtc)
    {
        if (Status == CouponRedemptionStatus.Reserved)
        {
            Status = CouponRedemptionStatus.Released;
            ClosedAtUtc = atUtc;
        }
    }
}
