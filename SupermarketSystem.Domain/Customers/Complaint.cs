using SupermarketSystem.Domain.Common;

namespace SupermarketSystem.Domain.Customers;

/// <summary>
/// شكوى زبون - مصدر رابع لقائمة "المراجعات الموحَّدة" (راجع
/// GetPendingReviewsQuery.cs). OrderId اختياري عمدًا - شكوى ممكن تكون
/// عامة، مش بالضرورة مرتبطة بطلب محدَّد.
/// </summary>
public class Complaint : AuditableEntity
{
    public Guid CustomerId { get; private set; }
    public Guid? OrderId { get; private set; }
    public string Text { get; private set; } = null!;
    public bool IsResolved { get; private set; }
    public DateTime? ResolvedAtUtc { get; private set; }
    public Guid? ResolvedByUserId { get; private set; }

    private Complaint() { } // EF Core

    public Complaint(Guid customerId, Guid? orderId, string text)
    {
        if (string.IsNullOrWhiteSpace(text))
        {
            throw new DomainException("Complaint text is required.");
        }

        CustomerId = customerId;
        OrderId = orderId;
        Text = text;
    }

    public void MarkResolved(Guid resolvedByUserId, DateTime resolvedAtUtc)
    {
        if (IsResolved)
        {
            throw new DomainException("This complaint has already been resolved.");
        }

        IsResolved = true;
        ResolvedByUserId = resolvedByUserId;
        ResolvedAtUtc = resolvedAtUtc;
    }
}
