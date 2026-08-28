using System.IdentityModel.Tokens.Jwt;
using System.Security.Claims;
using System.Security.Cryptography;
using System.Text;
using Microsoft.Extensions.Options;
using Microsoft.IdentityModel.Tokens;
using SupermarketSystem.Application.Common.Interfaces;
using SupermarketSystem.Domain.Identity;

namespace SupermarketSystem.Infrastructure.Services;

/// <summary>
/// إعدادات التوكن — تُقرأ من appsettings/متغيّرات البيئة، **لا** من جدول
/// الإعدادات بقاعدة البيانات (بخلاف كل إعدادات النظام الأخرى).
///
/// السبب مش تناقضًا مع النمط المتبع، هو فرق حقيقي بالطبيعة:
/// SigningKey مفتاح تشفير سرّي لازمه وسيط المصادقة **وقت إقلاع التطبيق**
/// نفسه (قبل ما يصير في أي طلب أو أي اتصال بقاعدة البيانات). كمان: مفاتيح
/// التوقيع ما بتنحط بمكان يقرأه أي حد عنده صلاحية على جدول الإعدادات —
/// جدول الإعدادات مصمَّم ليعدّله مدير النظام من لوحة التحكم، وهذا آخر
/// مكان يصلح لمفتاح توقيع.
/// </summary>
public sealed class JwtOptions
{
    public const string SectionName = "Jwt";

    public string SigningKey { get; set; } = string.Empty;
    public string Issuer { get; set; } = "SupermarketSystem";
    public string Audience { get; set; } = "SupermarketSystem";

    /// <summary>قصير عمدًا: نافذة الخطر لو تسرّب التوكن، وسقف تأخّر سحب الصلاحية.</summary>
    public int AccessTokenMinutes { get; set; } = 15;

    /// <summary>عمر الجلسة الفعلي قبل ما يُجبَر المستخدم يسجّل دخول من جديد.</summary>
    public int RefreshTokenDays { get; set; } = 7;
}

public sealed class JwtTokenService : ITokenService
{
    /// <summary>
    /// أسماء الـclaims المخصصة — معرَّفة كثوابت هون ومستهلَكة بنفس الأسماء
    /// عند قراءة الهوية (الخطوة 8). سلسلة نصية مكتوبة يدويًا بمكانين
    /// مختلفين هي بالضبط نوع الخطأ اللي بينتج مصادقة "شغّالة" بس فاضية
    /// من أي هوية فعلية.
    /// </summary>
    public const string SessionIdClaim = "session_id";
    public const string AppTypeClaim = "app_type";
    public const string BranchIdClaim = "branch_id";

    /// <summary>
    /// "true"/"false" فقط — يُقرأ حرفيًا بلا محاولة تفسير أي قيمة أخرى
    /// كـtrue (راجع RealCurrentUserContext). القرار يُحسم وقت الدخول
    /// (LoginHandler) ويُعاد تقييمه بكل تجديد (RefreshTokenHandler)، لا
    /// يُقرأ حيًّا من قاعدة البيانات هون — AppDbContext نفسه يعتمد على
    /// ICurrentUserContext بمنشئه، فأي استعلام من داخل تحقق التوكن كان
    /// رح يعمل اعتمادية دائرية.
    /// </summary>
    public const string CrossBranchClaim = "cross_branch";

    private readonly JwtOptions _options;

    public JwtTokenService(IOptions<JwtOptions> options)
    {
        _options = options.Value;

        if (string.IsNullOrWhiteSpace(_options.SigningKey))
        {
            // فشل صريح وقت الإقلاع أفضل بما لا يقاس من مفتاح افتراضي مدسوس
            // بالكود: مفتاح افتراضي معناه نظام "شغّال" بتوكنات يقدر أي حد
            // عنده نسخة من الكود يزوّرها — عطل صامت وكارثي.
            throw new InvalidOperationException(
                $"مفتاح توقيع التوكن غير مُعدّ. أضف '{JwtOptions.SectionName}:SigningKey' بالإعدادات أو بمتغيّرات البيئة.");
        }

        if (Encoding.UTF8.GetByteCount(_options.SigningKey) < 32)
        {
            // HMAC-SHA256 بيتطلب مفتاحًا لا يقل عن 256 بت؛ أقصر من هيك
            // بيرفضه المكتبة وقت التوقيع برسالة غامضة، فنكشفه هون بوضوح.
            throw new InvalidOperationException("مفتاح توقيع التوكن قصير جدًا؛ يجب أن يكون 32 بايت على الأقل.");
        }
    }

    public AccessTokenResult CreateAccessToken(
        Guid userId, string username, ClientAppType appType, Guid sessionId, Guid? branchId, bool allowCrossBranch)
    {
        var expiresAtUtc = DateTime.UtcNow.AddMinutes(_options.AccessTokenMinutes);

        var claims = new List<Claim>
        {
            new(JwtRegisteredClaimNames.Sub, userId.ToString()),
            new(JwtRegisteredClaimNames.UniqueName, username),
            // معرّف فريد لكل توكن — يخلي تتبّع توكن بعينه ممكنًا بالتحقيق.
            new(JwtRegisteredClaimNames.Jti, Guid.NewGuid().ToString()),
            new(SessionIdClaim, sessionId.ToString()),
            new(AppTypeClaim, appType.ToString()),
            new(CrossBranchClaim, allowCrossBranch ? "true" : "false")
        };

        if (branchId is { } branch)
        {
            claims.Add(new Claim(BranchIdClaim, branch.ToString()));
        }

        // ملاحظة مقصودة: ولا claim واحد للصلاحيات. الصلاحيات تُفحص حيًّا كل
        // طلب (الخطوة 9) — راجع تعليق ITokenService للسبب.

        var credentials = new SigningCredentials(
            new SymmetricSecurityKey(Encoding.UTF8.GetBytes(_options.SigningKey)),
            SecurityAlgorithms.HmacSha256);

        var token = new JwtSecurityToken(
            issuer: _options.Issuer,
            audience: _options.Audience,
            claims: claims,
            notBefore: DateTime.UtcNow,
            expires: expiresAtUtc,
            signingCredentials: credentials);

        return new AccessTokenResult(new JwtSecurityTokenHandler().WriteToken(token), expiresAtUtc);
    }

    public RefreshTokenResult CreateRefreshToken()
    {
        // 256 بت من مولّد عشوائي مشفَّر — لا Random العادي، اللي مخرجاته
        // متوقَّعة لمن يعرف البذرة.
        var bytes = RandomNumberGenerator.GetBytes(32);
        var token = Convert.ToBase64String(bytes);

        return new RefreshTokenResult(token, HashRefreshToken(token), DateTime.UtcNow.AddDays(_options.RefreshTokenDays));
    }

    public string HashRefreshToken(string refreshToken)
        => Convert.ToBase64String(SHA256.HashData(Encoding.UTF8.GetBytes(refreshToken)));
}
