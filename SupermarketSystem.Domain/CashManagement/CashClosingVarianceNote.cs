using SupermarketSystem.Domain.Common;

namespace SupermarketSystem.Domain.CashManagement;

/// <summary>سبب الفرق بتقفيل الصندوق، كما بيحكيه الكاشير لصاحب المحل (بند 23).</summary>
public enum VarianceExplanationReason
{
    /// <summary>بيعات لسه بطابور الكاشير لحظة التقفيل (بتوصل بعده).</summary>
    LateSales = 1,

    /// <summary>دفع فاتورة مورد/مصروف من الدرج وما انسجّل بالنظام.</summary>
    ForgottenDrawerPayment = 2,

    /// <summary>خطأ بعدّ الكاش.</summary>
    CountError = 3,

    /// <summary>سألنا وما في تفسير.</summary>
    Unknown = 4,

    /// <summary>سبب تاني (الملاحظة إلزامية).</summary>
    Other = 5
}

/// <summary>
/// تفسير فرق تقفيل الصندوق (بند 23، قرار صاحب المشروع 6/10/2026): "نسمح وننبّه (§1.6) - صاحب المحل بيسجّل تفسير للفرق
/// بعد ما الكاشير يحكيله". سجل تاريخي بحت مربوط بالتقفيل - ما بنعدّل التقفيل نفسه (CashClosing محكوم بعدم التعديل) ولا بنكتب
/// حركة صندوق (CashDrawerLog) بوقت التسجيل: وإلا بيخرّب "المتوقع" للوردية الجاية. مصروف/دفعة منسيّة بتتسجّل كمصروف عادي
/// (بلا PayOut) وبتنربط هون عبر RelatedExpenseId (مرجع فضفاض، بلا FK - مثل CashDrawerLog.ReferenceId).
/// ExplainedAmount مقدار موجب من |الفرق| اللي هالسبب بيفسّره؛ مجموعها لكل تقفيل ما بيتجاوز |الفرق|.
/// الفرق غير المفسَّر = |الفرق| − مجموع المفسَّر، وبيظهر بتقرير فروقات الكاشير.
/// </summary>
public class CashClosingVarianceNote : Entity, IBranchOwned
{
    public const int MaxNoteLength = 500;

    public Guid CashClosingId { get; private set; }
    public Guid BranchId { get; private set; }
    public VarianceExplanationReason Reason { get; private set; }
    public decimal ExplainedAmount { get; private set; }
    public string? Note { get; private set; }
    public Guid? RelatedExpenseId { get; private set; }
    public Guid RecordedByUserId { get; private set; }
    public DateTime RecordedAtUtc { get; private set; }

    private CashClosingVarianceNote() { } // EF Core

    public CashClosingVarianceNote(
        Guid cashClosingId, Guid branchId, VarianceExplanationReason reason, decimal explainedAmount, string? note,
        Guid? relatedExpenseId, Guid recordedByUserId, DateTime recordedAtUtc)
    {
        if (explainedAmount < 0)
        {
            throw new DomainException("Explained amount cannot be negative.");
        }

        CashClosingId = cashClosingId;
        BranchId = branchId;
        Reason = reason;
        ExplainedAmount = explainedAmount;
        Note = string.IsNullOrWhiteSpace(note) ? null : note.Trim();
        RelatedExpenseId = relatedExpenseId;
        RecordedByUserId = recordedByUserId;
        RecordedAtUtc = recordedAtUtc;
    }
}
