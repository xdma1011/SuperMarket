using Microsoft.EntityFrameworkCore;
using SupermarketSystem.Application.Common.Interfaces;
using SupermarketSystem.Application.Common.Results;
using SupermarketSystem.Domain.Identity;

namespace SupermarketSystem.Application.Authentication.RevokeSession;

public sealed record RevokeSessionCommand(Guid SessionId);

/// <summary>
/// إبطال إداري فوري — الاستخدام النموذجي: إنهاء خدمة موظف، أو اشتباه
/// بجلسة مسروقة (IP/جهاز غريب بتقرير الجلسات).
///
/// بخلاف LogoutHandler (idempotent وهادئ)، هذا فعل إداري صريح — لازم
/// المدير يعرف بالضبط شو صار (نجح، ما لقيناها، كانت مُبطَلة أصلًا). صمت
/// هون كان رح يخفي عن المدير إذا فعليًا نجح الإجراء اللي يعتمد عليه.
///
/// ملاحظة نطاق: هذا يبطل جلسة *واحدة محدَّدة*، لا كل جلسات مستخدم دفعة
/// وحدة. "عطّل كل جلسات هذا المستخدم" (المقترنة بتعطيل الحساب نفسه) قرار
/// مختلف ومكانه الطبيعي داخل عملية تعطيل المستخدم ذاتها، لا هون — راجع
/// الفجوة الموثَّقة بـRefreshTokenHandler لنفس السبب بالضبط.
/// </summary>
public sealed class RevokeSessionHandler
{
    private readonly IApplicationDbContext _context;
    private readonly IDateTimeProvider _dateTimeProvider;

    public RevokeSessionHandler(IApplicationDbContext context, IDateTimeProvider dateTimeProvider)
    {
        _context = context;
        _dateTimeProvider = dateTimeProvider;
    }

    public async Task<Result> HandleAsync(RevokeSessionCommand command, CancellationToken cancellationToken)
    {
        var session = await _context.UserSessions
            .FirstOrDefaultAsync(s => s.Id == command.SessionId, cancellationToken);

        if (session is null)
        {
            return Result.Failure(Error.NotFound("Session.NotFound", $"الجلسة '{command.SessionId}' غير موجودة."));
        }

        if (session.RevokedAtUtc is not null)
        {
            return Result.Failure(Error.Conflict("Session.AlreadyRevoked", "هذه الجلسة مُبطَلة أصلًا."));
        }

        session.Revoke(SessionRevocationReason.RevokedByAdministrator, _dateTimeProvider.UtcNow);
        await _context.SaveChangesAsync(cancellationToken);

        return Result.Success();
    }
}
