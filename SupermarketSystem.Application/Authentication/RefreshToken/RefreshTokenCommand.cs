using Microsoft.EntityFrameworkCore;
using SupermarketSystem.Application.Common.Interfaces;
using SupermarketSystem.Application.Common.Results;
using SupermarketSystem.Domain.Identity;

namespace SupermarketSystem.Application.Authentication.RefreshToken;

public sealed record RefreshTokenCommand(string RefreshToken);

public sealed record RefreshTokenResponse(
    string AccessToken,
    DateTime AccessTokenExpiresAtUtc,
    string RefreshToken,
    DateTime RefreshTokenExpiresAtUtc);

/// <summary>
/// يجدّد الجلسة: يتحقق من صلاحية الجلسة الحالية، يدوّر توكن التجديد
/// (rotation)، ويصدر access token جديد بنفس الهوية والفرع.
///
/// نفس مبدأ رسالة الفشل الموحّدة المطبَّق بـLoginHandler، لنفس السبب:
/// "التوكن غير موجود"، "الجلسة منتهية"، "الجلسة مُبطَلة"، "المستخدم
/// معطَّل" — كلهم بيرجّعوا نفس الخطأ. تسريب أي منهم بيعطي معلومة عن حالة
/// حساب أو جلسة تحديدًا لشخص عنده توكن مسروق أو منتهي.
///
/// ═══════════════════════════════════════════════════════════════════
/// فحص مهم غير موجود بأي مكان تاني بالنظام: حالة المستخدم *وقت التجديد*،
/// لا وقت الدخول فقط.
/// ═══════════════════════════════════════════════════════════════════
/// لو موظف تعطّل حسابه بعد ما دخل، الجلسة القديمة تبقى "فعّالة" تقنيًا
/// (ما انبطلت، ما انتهت) لحد ما حدا يبطلها صراحةً — وهذا الإبطال الصريح
/// (تعطيل تلقائي لكل جلسات مستخدم عند تعطيله) *لسه ما بُني* (يحتاج
/// اقترانًا بميثود تعطيل المستخدم، خارج نطاق هذه الخطوة). لحد ما يُبنى،
/// هذا الفحص هون هو خط الدفاع الوحيد: كل تجديد بيتحقق من User.IsActive
/// من جديد، فأطول فترة ممكن يشتغل فيها موظف مُعطَّل بجلسة قديمة هي عمر
/// الـaccess token الحالي (15 دقيقة)، لا عمر الجلسة كله (7 أيام).
/// </summary>
public sealed class RefreshTokenHandler
{
    private readonly IApplicationDbContext _context;
    private readonly ITokenService _tokenService;
    private readonly IDateTimeProvider _dateTimeProvider;

    private static Error InvalidToken()
        => Error.Forbidden("Auth.InvalidRefreshToken", "جلسة غير صالحة؛ يرجى تسجيل الدخول من جديد.");

    public RefreshTokenHandler(IApplicationDbContext context, ITokenService tokenService, IDateTimeProvider dateTimeProvider)
    {
        _context = context;
        _tokenService = tokenService;
        _dateTimeProvider = dateTimeProvider;
    }

    public async Task<Result<RefreshTokenResponse>> HandleAsync(RefreshTokenCommand command, CancellationToken cancellationToken)
    {
        if (string.IsNullOrWhiteSpace(command.RefreshToken))
        {
            return Result.Failure<RefreshTokenResponse>(InvalidToken());
        }

        var utcNow = _dateTimeProvider.UtcNow;
        var tokenHash = _tokenService.HashRefreshToken(command.RefreshToken);

        var session = await _context.UserSessions
            .FirstOrDefaultAsync(s => s.RefreshTokenHash == tokenHash, cancellationToken);

        if (session is null || !session.IsActive(utcNow))
        {
            return Result.Failure<RefreshTokenResponse>(InvalidToken());
        }

        // IgnoreQueryFilters: لو المستخدم انحذف ناعمًا بعد إنشاء الجلسة،
        // لازم نشوفه هون عشان نرفض ونبطل الجلسة صراحةً — لا نتجاهله
        // ونترك الجلسة "شغّالة" لأنه الاستعلام العادي أخفاه.
        var user = await _context.Users
            .IgnoreQueryFilters()
            .FirstOrDefaultAsync(u => u.Id == session.UserId, cancellationToken);

        if (user is null || !user.IsActive || user.IsDeleted)
        {
            // دفاع بعمق: نبطل الجلسة صراحةً هون بدل ما نتركها "تنتهي
            // لحالها" — أي محاولة تجديد تانية بنفس التوكن (لو تسرّب) لازم
            // تُرفض فورًا، لا تعتمد على إعادة فحص المستخدم كل مرة.
            session.Revoke(SessionRevocationReason.UserDeactivated, utcNow);
            await _context.SaveChangesAsync(cancellationToken);

            return Result.Failure<RefreshTokenResponse>(InvalidToken());
        }

        var newRefreshToken = _tokenService.CreateRefreshToken();
        session.Rotate(newRefreshToken.TokenHash, utcNow, newRefreshToken.ExpiresAtUtc);

        // تُعاد الفحص هون، لا تُعاد من التوكن القديم — هذا بالضبط اللي
        // بيخلي سحب صلاحية "تجاوز الفروع" فعّالًا خلال أقصى 15 دقيقة
        // (عمر التوكن)، لا يبقى ثابتًا لعمر الجلسة كله (7 أيام).
        var allowCrossBranch = await _context.UserRoles.AsNoTracking()
            .Where(ur => ur.UserId == user.Id)
            .Join(_context.RolePermissions.AsNoTracking(), ur => ur.RoleId, rp => rp.RoleId, (ur, rp) => rp.PermissionId)
            .Join(_context.Permissions.AsNoTracking(), pid => pid, p => p.Id, (pid, p) => p.Code)
            .AnyAsync(code => code == PermissionCodes.CrossBranchAccess, cancellationToken);

        var newAccessToken = _tokenService.CreateAccessToken(
            user.Id, user.Username, session.AppType, session.Id, session.BranchId, allowCrossBranch);

        await _context.SaveChangesAsync(cancellationToken);

        return Result.Success(new RefreshTokenResponse(
            newAccessToken.Token, newAccessToken.ExpiresAtUtc,
            newRefreshToken.Token, newRefreshToken.ExpiresAtUtc));
    }
}
