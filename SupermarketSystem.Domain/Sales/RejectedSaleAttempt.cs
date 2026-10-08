using SupermarketSystem.Domain.Common;

namespace SupermarketSystem.Domain.Sales;

/// <summary>
/// بيعة رفضها السيرفر نهائيًا (400/409/422) - بند 24 بالمراجعة النقدية (8/10/2026).
/// المشكلة: الكاشير (أوفلاين) بيحفظ البيعة محليًا ويعيد إرسالها كل ~60 ثانية؛ لو السيرفر رفضها رفض نهائي
/// بتضل بالطابور للأبد، والسيرفر ما كان يسجّل إشي - يعني مصاري بالدرج بلا فاتورة وبلا خصم مخزون، والإدارة ما بتعرف.
///
/// سجل واحد لكل ClientRequestId: إعادة المحاولة بتحدّث نفس الصف (AttemptCount / LastAttemptAtUtc / سبب الرفض
/// الأخير)، ما بتعمل صف جديد. المحتوى كامل (JSON) محفوظ عشان الإدارة تدخل البيعة يدويًا أو تعرف بالضبط شو انباع.
/// "تمت المعالجة" علامة بشرية + ملاحظة، بلا إعادة معالجة تلقائية (الأسعار/الوقت/المخزون ممكن تكون تغيّرت).
/// لو نفس الـClientRequestId انقبل لاحقًا (السبب انحل والكاشير أعاد الإرسال) بيتعلّم تلقائيًا "انقبلت لاحقًا".
/// </summary>
public class RejectedSaleAttempt : Entity, IBranchOwned
{
    public const int MaxErrorCodeLength = 100;
    public const int MaxErrorMessageLength = 1000;
    public const int MaxNoteLength = 500;

    public Guid BranchId { get; private set; }

    /// <summary>مفتاح الـIdempotency تبع البيعة (فريد) - بيربط كل محاولات نفس البيعة بصف واحد.</summary>
    public Guid ClientRequestId { get; private set; }

    /// <summary>مين بعتها (الكاشير) - null لو ما انعرف.</summary>
    public Guid? CashierUserId { get; private set; }

    public string ErrorCode { get; private set; } = string.Empty;
    public string ErrorMessage { get; private set; } = string.Empty;

    /// <summary>محتوى طلب البيع كامل كما وصل (JSON).</summary>
    public string PayloadJson { get; private set; } = string.Empty;

    /// <summary>مجموع الدفعات بالطلب - تقدير سريع "كم مصاري انقبضت بالدرج" بلا ما نفتح الـJSON.</summary>
    public decimal PaidAmountHint { get; private set; }

    public int ItemCount { get; private set; }
    public int AttemptCount { get; private set; }
    public DateTime FirstAttemptAtUtc { get; private set; }
    public DateTime LastAttemptAtUtc { get; private set; }

    public DateTime? ResolvedAtUtc { get; private set; }
    public Guid? ResolvedByUserId { get; private set; }
    public string? ResolutionNote { get; private set; }

    /// <summary>true = انقبلت لاحقًا لحالها (نفس الطلب نجح بعد ما انحل سبب الرفض)، مش علامة يدوية.</summary>
    public bool ResolvedAutomatically { get; private set; }

    public bool IsOpen => ResolvedAtUtc is null;

    private RejectedSaleAttempt() { } // EF Core

    public RejectedSaleAttempt(
        Guid branchId, Guid clientRequestId, Guid? cashierUserId, string errorCode, string errorMessage,
        string payloadJson, decimal paidAmountHint, int itemCount, DateTime nowUtc)
    {
        BranchId = branchId;
        ClientRequestId = clientRequestId;
        CashierUserId = cashierUserId;
        PayloadJson = payloadJson;
        PaidAmountHint = paidAmountHint;
        ItemCount = itemCount;
        FirstAttemptAtUtc = nowUtc;
        SetLastError(errorCode, errorMessage, nowUtc);
        AttemptCount = 1;
    }

    /// <summary>محاولة إضافية مرفوضة لنفس البيعة (الكاشير أعاد الإرسال). بتعيد فتح السجل لو كان معالَج وهي لسه بتنرفض.</summary>
    public void RegisterRetry(string errorCode, string errorMessage, DateTime nowUtc)
    {
        SetLastError(errorCode, errorMessage, nowUtc);
        AttemptCount++;
        // البيعة اللي علّمناها "معالَجة" يدويًا وضلت بتوصل = الكاشير لسه ما شطبها من طابوره؛ ما بنعيد فتحها
        // (الإدارة قررت)، بس العدّاد والسبب الأخير بيتحدّثوا.
    }

    public void MarkResolved(Guid? userId, string? note, DateTime nowUtc)
    {
        if (ResolvedAtUtc is not null)
        {
            return;
        }

        ResolvedAtUtc = nowUtc;
        ResolvedByUserId = userId;
        ResolutionNote = string.IsNullOrWhiteSpace(note) ? null : Truncate(note.Trim(), MaxNoteLength);
    }

    public void MarkAcceptedLater(DateTime nowUtc)
    {
        if (ResolvedAtUtc is not null)
        {
            return;
        }

        ResolvedAtUtc = nowUtc;
        ResolvedAutomatically = true;
        ResolutionNote = "انقبلت لاحقًا لحالها (انحل سبب الرفض وأعاد الكاشير الإرسال).";
    }

    private void SetLastError(string errorCode, string errorMessage, DateTime nowUtc)
    {
        ErrorCode = Truncate(errorCode, MaxErrorCodeLength);
        ErrorMessage = Truncate(errorMessage, MaxErrorMessageLength);
        LastAttemptAtUtc = nowUtc;
    }

    private static string Truncate(string value, int max) => value.Length <= max ? value : value[..max];
}
