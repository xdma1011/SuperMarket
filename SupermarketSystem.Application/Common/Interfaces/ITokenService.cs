using SupermarketSystem.Domain.Identity;

namespace SupermarketSystem.Application.Common.Interfaces;

public sealed record AccessTokenResult(string Token, DateTime ExpiresAtUtc);

public sealed record RefreshTokenResult(string Token, string TokenHash, DateTime ExpiresAtUtc);

/// <summary>
/// توليد التوكنات. التحقق من صحة الـaccess token نفسه *ليس* هنا — هو
/// مسؤولية وسيط المصادقة بالـAPI (JwtBearer)، اللي بيتحقق من التوقيع
/// والصلاحية قبل ما يوصل الطلب لأي handler أصلًا.
///
/// نوعان مختلفان جوهريًا، عن قصد:
///
/// - Access token: JWT موقَّع، **قصير العمر**، بلا حالة مخزَّنة. بيحمل
///   هوية المستخدم والجلسة فقط — لا صلاحيات. الصلاحيات تُفحص حيًّا كل
///   طلب (الخطوة 9)، لأنه صلاحية محفورة بتوكن عمره 15 دقيقة معناها
///   موظف انسحبت صلاحيته بيضل قادر يستعملها لـ15 دقيقة بعدها.
///
/// - Refresh token: **ليس JWT** — سلسلة عشوائية عالية الإنتروبيا، بلا أي
///   معلومة بداخلها. قيمته الوحيدة إنه يطابق بصمة مخزَّنة بجدول
///   UserSession، وهذا بالضبط اللي بيخلي إبطاله فوريًا وممكنًا.
/// </summary>
public interface ITokenService
{
    AccessTokenResult CreateAccessToken(Guid userId, string username, ClientAppType appType, Guid sessionId, Guid? branchId, bool allowCrossBranch);

    RefreshTokenResult CreateRefreshToken();

    /// <summary>
    /// يجزّئ توكن تجديد وارد لمطابقته بالمخزَّن. تجزئة سريعة (SHA-256) لا
    /// بطيئة (PBKDF2) — والفرق مقصود ومبرَّر: التجزئة البطيئة موجودة لتقاوم
    /// تخمين كلمات سر يختارها بشر (إنتروبيا منخفضة). توكن التجديد مولَّد
    /// عشوائيًا بإنتروبيا عالية، فتخمينه غير وارد أصلًا، وإبطاؤه معناه بس
    /// إبطاء كل عملية تجديد بلا أي مكسب أمني.
    /// </summary>
    string HashRefreshToken(string refreshToken);
}
