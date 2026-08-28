using Microsoft.EntityFrameworkCore;
using SupermarketSystem.Application.Common.Interfaces;
using SupermarketSystem.Application.Common.Results;
using SupermarketSystem.Domain.Identity;

namespace SupermarketSystem.Application.Authentication.Logout;

public sealed record LogoutCommand(string RefreshToken);

/// <summary>
/// خروج طوعي — بيتعرّف على الجلسة عبر بصمة توكن التجديد نفسه (نفس آلية
/// RefreshTokenHandler)، لا عبر هوية مُستخرَجة من الـaccess token؛ السياق
/// الحقيقي (ICurrentUserContext) لسه ما بُني (الخطوة 8)، فهذا أبسط مسار
/// موثوق متاح حاليًا، وبيضل صالحًا حتى بعد الخطوة 8.
///
/// مقصود يكون idempotent بالكامل: توكن غير موجود، جلسة مُبطَلة أصلًا،
/// حتى نص فاضي — كلهم بيرجّعوا نجاح بهدوء بلا أي رسالة خطأ. "خروج"
/// بطبيعته يعني "تأكّد إن هالجلسة انتهت" — لو انتهت أصلًا، الهدف محقَّق.
/// رسالة خطأ هون كانت رح تكشف معلومة بلا داعٍ (هل التوكن كان صالحًا
/// أصلًا؟) لعملية ما بتحتاج هالتفصيل إطلاقًا.
/// </summary>
public sealed class LogoutHandler
{
    private readonly IApplicationDbContext _context;
    private readonly ITokenService _tokenService;
    private readonly IDateTimeProvider _dateTimeProvider;

    public LogoutHandler(IApplicationDbContext context, ITokenService tokenService, IDateTimeProvider dateTimeProvider)
    {
        _context = context;
        _tokenService = tokenService;
        _dateTimeProvider = dateTimeProvider;
    }

    public async Task HandleAsync(LogoutCommand command, CancellationToken cancellationToken)
    {
        if (string.IsNullOrWhiteSpace(command.RefreshToken))
        {
            return;
        }

        var tokenHash = _tokenService.HashRefreshToken(command.RefreshToken);

        var session = await _context.UserSessions
            .FirstOrDefaultAsync(s => s.RefreshTokenHash == tokenHash, cancellationToken);

        if (session is null || session.RevokedAtUtc is not null)
        {
            return;
        }

        session.Revoke(SessionRevocationReason.UserLoggedOut, _dateTimeProvider.UtcNow);
        await _context.SaveChangesAsync(cancellationToken);
    }
}
