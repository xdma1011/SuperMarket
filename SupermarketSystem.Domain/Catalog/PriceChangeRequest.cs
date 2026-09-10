using SupermarketSystem.Domain.Common;

namespace SupermarketSystem.Domain.Catalog;

public enum PriceChangeRequestStatus
{
    Pending = 1,
    Approved = 2,
    Rejected = 3
}

/// <summary>
/// كانت مفقودة بالكامل - "سماح بثلاث مستويات" لتعديل سعر بيع منتج بفرع:
///
///   1) صلاحية مباشرة (Catalog.ChangePriceDirect) - السعر يتغيّر فورًا،
///      يُسجَّل الطلب هنا بحالة Approved تلقائيًا (سجل تدقيق فقط، لا يوقف شي).
///   2) صلاحية طلب فقط (Catalog.RequestSellingPriceChange، بلا Direct) -
///      السعر ما يتغيّر إطلاقًا لحد ما مستخدم عنده Direct يوافق عليه صراحة.
///   3) منع مطلق - بلا الصلاحيتين، الـendpoint نفسه يرفض الطلب من الأساس
///      (403)، هذا الكيان ولا الـHandler يوصلهم أصلًا.
///
/// القرار "مباشر أو طلب" يُحسم Runtime داخل RequestPriceChangeHandler عبر
/// IPermissionChecker - لا Endpoint منفصل لكل مستوى.
/// </summary>
public class PriceChangeRequest : AuditableEntity
{
    public Guid ProductBranchId { get; private set; }
    public decimal PreviousPrice { get; private set; }
    public decimal RequestedPrice { get; private set; }
    public PriceChangeRequestStatus Status { get; private set; }
    public Guid RequestedByUserId { get; private set; }
    public DateTime RequestedAtUtc { get; private set; }
    public Guid? DecidedByUserId { get; private set; }
    public DateTime? DecidedAtUtc { get; private set; }
    public string? DecisionNote { get; private set; }

    private PriceChangeRequest() { } // EF Core

    public PriceChangeRequest(
        Guid productBranchId, decimal previousPrice, decimal requestedPrice,
        Guid requestedByUserId, DateTime requestedAtUtc)
    {
        if (requestedPrice < 0)
        {
            throw new DomainException("Requested price cannot be negative.");
        }

        ProductBranchId = productBranchId;
        PreviousPrice = previousPrice;
        RequestedPrice = requestedPrice;
        Status = PriceChangeRequestStatus.Pending;
        RequestedByUserId = requestedByUserId;
        RequestedAtUtc = requestedAtUtc;
    }

    /// <summary>مستخدم عنده صلاحية مباشرة - السعر اتغيّر فعليًا فورًا خارج هذا الكيان، هذا مجرد سجل تدقيق بحالة معتمدة تلقائيًا.</summary>
    public void AutoApprove(Guid decidedByUserId, DateTime decidedAtUtc)
    {
        Status = PriceChangeRequestStatus.Approved;
        DecidedByUserId = decidedByUserId;
        DecidedAtUtc = decidedAtUtc;
    }

    public void Approve(Guid decidedByUserId, DateTime decidedAtUtc, string? note)
    {
        if (Status != PriceChangeRequestStatus.Pending)
        {
            throw new DomainException("Only a pending price change request can be approved.");
        }

        Status = PriceChangeRequestStatus.Approved;
        DecidedByUserId = decidedByUserId;
        DecidedAtUtc = decidedAtUtc;
        DecisionNote = note;
    }

    public void Reject(Guid decidedByUserId, DateTime decidedAtUtc, string? note)
    {
        if (Status != PriceChangeRequestStatus.Pending)
        {
            throw new DomainException("Only a pending price change request can be rejected.");
        }

        Status = PriceChangeRequestStatus.Rejected;
        DecidedByUserId = decidedByUserId;
        DecidedAtUtc = decidedAtUtc;
        DecisionNote = note;
    }
}
