using SupermarketSystem.Domain.Common;

namespace SupermarketSystem.Domain.Identity;

/// <summary>
/// نوع التطبيق اللي فُتحت منه الجلسة.
///
/// قاعدة "جلسة واحدة" مربوطة بـ(المستخدم + نوع التطبيق) لا بالمستخدم
/// لحاله — وهذا مقصود: مدير يقدر يكون مسجّل دخول بشاشة الكاشير وبلوحة
/// الإدارة بنفس الوقت (شخص واحد، جهازان شرعيان)، بس ما يقدر يكون مسجّل
/// دخول بكاشيرين مختلفين بنفس الحساب — وهذا بالضبط سيناريو مشاركة كلمة
/// السر اللي القاعدة موجودة لتمنعه.
/// </summary>
public enum ClientAppType
{
    Cashier = 1,
    Admin = 2
}

/// <summary>
/// سبب انتهاء الجلسة — يُسجَّل دائمًا، لا يُترك فاضي عند الإبطال، لأنه
/// "ليش انتهت الجلسة" معلومة تدقيق حقيقية لا تفصيل تقني.
/// </summary>
public enum SessionRevocationReason
{
    /// <summary>خروج طوعي من المستخدم نفسه.</summary>
    UserLoggedOut = 1,

    /// <summary>
    /// دخول جديد لنفس (المستخدم + نوع التطبيق) سحب هذه الجلسة تلقائيًا.
    /// هذا الحدث بحد ذاته إشارة تستحق المراجعة لو تكرر بفارق زمني قصير —
    /// دخول من مكانين مختلفين بنفس الحساب.
    /// </summary>
    NewLoginElsewhere = 2,

    /// <summary>إبطال إداري صريح (مثلًا: إنهاء خدمة موظف).</summary>
    RevokedByAdministrator = 3,

    /// <summary>المستخدم نفسه تم تعطيله أو حذفه.</summary>
    UserDeactivated = 4
}

/// <summary>
/// جلسة مصادقة واحدة. تُخزَّن بقاعدة البيانات (بخلاف الـaccess token اللي
/// هو JWT بلا حالة) — وهذا بالضبط اللي بيخلي "الإبطال الفوري" ممكنًا
/// أصلًا: JWT بطبيعته ما بينحل قبل انتهاء صلاحيته، فلو كانت الجلسة كلها
/// بالتوكن، إنهاء خدمة موظف ما رح يوقفه فعليًا لحد ما التوكن ينتهي لحاله.
///
/// المخزَّن هو *بصمة* الـrefresh token لا التوكن نفسه — نفس منطق كلمة
/// السر بالضبط: لو تسرّبت قاعدة البيانات، المهاجم ما بيقدر ينتحل جلسات
/// قائمة، لأنه البصمة ما بترجع للتوكن الأصلي.
///
/// IpAddress وDeviceInfo مسجَّلان هون (وبـUserLoginLog كمان) — كان مطلبًا
/// صريحًا بالقائمة الأصلية، وكان ناقصًا بالكامل قبل هذه المرحلة
/// (AuditLog.IpAddress كان عمودًا موجودًا بس دائمًا فاضي، لعدم وجود سياق
/// مستخدم حقيقي).
/// </summary>
public class UserSession : Entity
{
    public Guid UserId { get; private set; }
    public ClientAppType AppType { get; private set; }

    /// <summary>بصمة الـrefresh token، لا التوكن نفسه.</summary>
    public string RefreshTokenHash { get; private set; } = null!;

    /// <summary>الفرع المختار وقت الدخول — nullable لأن مستخدمًا قد لا يكون مرتبطًا بفرع بعد (لوحة إدارة مركزية مثلًا).</summary>
    public Guid? BranchId { get; private set; }

    public string? IpAddress { get; private set; }
    public string? DeviceInfo { get; private set; }

    public DateTime CreatedAtUtc { get; private set; }
    public DateTime ExpiresAtUtc { get; private set; }

    public DateTime? RevokedAtUtc { get; private set; }
    public SessionRevocationReason? RevocationReason { get; private set; }

    /// <summary>
    /// آخر مرة استُخدمت فيها الجلسة لتجديد التوكن — مفيدة لتنظيف الجلسات
    /// المهجورة لاحقًا، وللإجابة على "هل هذا الحساب لسه مستعمل فعليًا".
    /// </summary>
    public DateTime? LastRefreshedAtUtc { get; private set; }

    /// <summary>
    /// الجلسة فعّالة فقط لو ما انبطلت *ولا* انتهت صلاحيتها. الاثنين
    /// شرطان منفصلان عمدًا: انتهاء الصلاحية حدث طبيعي بمرور الوقت،
    /// والإبطال قرار صريح — والتمييز بينهم مهم بالتدقيق.
    ///
    /// utcNow يُمرَّر ولا يُقرأ من DateTime.UtcNow داخليًا — الـDomain لا
    /// يقرأ الساعة بنفسه (نفس النمط المتبع بكل الكيانات الأخرى، يخلي
    /// السلوك قابلًا للاختبار بلا حيَل).
    /// </summary>
    public bool IsActive(DateTime utcNow) => RevokedAtUtc is null && ExpiresAtUtc > utcNow;

    private UserSession() { } // EF Core

    public UserSession(
        Guid userId,
        ClientAppType appType,
        string refreshTokenHash,
        Guid? branchId,
        string? ipAddress,
        string? deviceInfo,
        DateTime createdAtUtc,
        DateTime expiresAtUtc)
    {
        if (string.IsNullOrWhiteSpace(refreshTokenHash))
        {
            throw new DomainException("A refresh token hash is required.");
        }

        if (expiresAtUtc <= createdAtUtc)
        {
            throw new DomainException("Session expiry must be after its creation time.");
        }

        UserId = userId;
        AppType = appType;
        RefreshTokenHash = refreshTokenHash;
        BranchId = branchId;
        IpAddress = ipAddress;
        DeviceInfo = deviceInfo;
        CreatedAtUtc = createdAtUtc;
        ExpiresAtUtc = expiresAtUtc;
    }

    /// <summary>
    /// الإبطال نهائي ولا يُلغى — جلسة انبطلت ما بترجع تشتغل. إعادة
    /// الإبطال بترفض، عشان سبب ووقت الإبطال الأصلي ما ينكتب فوقهم
    /// (نفس مبدأ ReturnInvoice.MarkReviewed).
    /// </summary>
    public void Revoke(SessionRevocationReason reason, DateTime revokedAtUtc)
    {
        if (RevokedAtUtc is not null)
        {
            throw new DomainException("This session has already been revoked.");
        }

        RevocationReason = reason;
        RevokedAtUtc = revokedAtUtc;
    }

    /// <summary>
    /// يُستدعى عند كل تجديد ناجح. الـrefresh token يُستبدل ببصمة جديدة كل
    /// مرة (rotation) — لو تسرّب توكن قديم واستُعمل بعد التجديد، بصمته ما
    /// عادت تطابق المخزَّن فبينرفض تلقائيًا.
    /// </summary>
    public void Rotate(string newRefreshTokenHash, DateTime refreshedAtUtc, DateTime newExpiresAtUtc)
    {
        if (RevokedAtUtc is not null)
        {
            throw new DomainException("Cannot rotate a revoked session.");
        }

        if (string.IsNullOrWhiteSpace(newRefreshTokenHash))
        {
            throw new DomainException("A refresh token hash is required.");
        }

        RefreshTokenHash = newRefreshTokenHash;
        LastRefreshedAtUtc = refreshedAtUtc;
        ExpiresAtUtc = newExpiresAtUtc;
    }
}
