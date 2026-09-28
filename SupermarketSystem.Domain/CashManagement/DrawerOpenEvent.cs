using SupermarketSystem.Domain.Common;

namespace SupermarketSystem.Domain.CashManagement;

/// <summary>
/// فتح درج الكاش بلا بيع (زر "فتح الصندوق" بالكاشير) - مين، إمتى، وليش. سجل تاريخي بحت، بلا أي
/// أثر على المبالغ أو تقفيل الصندوق (منفصل عمدًا عن CashDrawerLog، اللي كل حركة فيه إلها مبلغ موجب).
/// الهدف كشف نمط: درج بينفتح كتير بلا فواتير هو باب سرقة معروف.
/// حاليًا تسجيل بس - ما في ربط فعلي بدرج حقيقي (طلب صاحب المشروع 28/9/2026: "جهّزها، لا تشغّلها").
/// </summary>
public class DrawerOpenEvent : Entity, IBranchOwned
{
    public const int MaxReasonLength = 200;

    public Guid BranchId { get; private set; }
    public Guid UserId { get; private set; }
    public DateTime OccurredAtUtc { get; private set; }
    public string? Reason { get; private set; }

    /// <summary>
    /// بلا نت (28/9/2026): الكاشير بيحفظ الفتحة محليًا أول وبيبعتها لما يرجع الاتصال - ClientRequestId (فريد) بيمنع
    /// تسجيلها مرتين لو انبعتت مرتين، وOccurredAtUtc بيكون وقت الفتح الفعلي عند الكاشير. null = سجل قديم/من غير الكاشير.
    /// </summary>
    public Guid? ClientRequestId { get; private set; }

    /// <summary>وقت وصولها للسيرفر - بيختلف عن OccurredAtUtc لما تنبعت بعد انقطاع. null = سجل قديم (نفس OccurredAtUtc).</summary>
    public DateTime? RecordedAtUtc { get; private set; }

    private DrawerOpenEvent() { } // EF Core

    public DrawerOpenEvent(Guid branchId, Guid userId, DateTime occurredAtUtc, string? reason,
        Guid? clientRequestId = null, DateTime? recordedAtUtc = null)
    {
        BranchId = branchId;
        UserId = userId;
        OccurredAtUtc = occurredAtUtc;
        Reason = string.IsNullOrWhiteSpace(reason) ? null : reason.Trim();
        ClientRequestId = clientRequestId;
        RecordedAtUtc = recordedAtUtc;
    }
}
