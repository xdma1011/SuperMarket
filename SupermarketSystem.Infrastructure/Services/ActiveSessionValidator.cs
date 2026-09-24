using System.Security.Claims;
using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using SupermarketSystem.Application.Common.Interfaces;
using SupermarketSystem.Infrastructure.Persistence;

namespace SupermarketSystem.Infrastructure.Services;

/// <summary>
/// فحص حي بكل طلب مصادَق: الجلسة (session_id بالتوكن) لسه فعّالة، وصاحبها
/// لسه فعّال. بدونه، إلغاء جلسة أو تعطيل مستخدم كان يوقف التجديد بس، والتوكن
/// الحالي يضل شغّال لحد عمره (15 دقيقة).
///
/// ليش هون (OnTokenValidated) لا middleware بعد UseAuthentication: Fail هون
/// بيخلي التوكن كأنه مش موجود - endpoints المحمية بترجع 401، بس
/// AllowAnonymous (زي /auth/login) بتضل تشتغل. تطبيق الكاشير بيبعت التوكن
/// القديم حتى مع طلب الدخول، فرفض الطلب كامل كان رح يمنع الكاشير المطرود من
/// إنه يرجع يسجّل دخول أبدًا.
///
/// ليش AppDbContext منفصل لا المحقون: AppDbContext بياخد لقطة من فرع
/// المستخدم وقت إنشائه، وهون HttpContext.User لسه مش متعبّى - لو استعملنا
/// النسخة المحقونة كانت فلاتر الفروع رح تنكسر لباقي الطلب كامل.
///
/// بلا كاش عمدًا: استعلام واحد على مفتاح أساسي بكل طلب، مقابل ضمان إنه
/// الطرد فوري فعلًا بلا أي نافذة تأخير. كافٍ لحجم محل؛ لو احتجنا كاش لاحقًا،
/// لازم يترافق مع مسح صريح عند كل إبطال.
/// </summary>
internal static class ActiveSessionValidator
{
    public static async Task ValidateAsync(TokenValidatedContext context)
    {
        var principal = context.Principal;
        var sessionIdValue = principal?.FindFirst(JwtTokenService.SessionIdClaim)?.Value;
        var userIdValue = principal?.FindFirst(ClaimTypes.NameIdentifier)?.Value ?? principal?.FindFirst("sub")?.Value;

        if (!Guid.TryParse(sessionIdValue, out var sessionId) || !Guid.TryParse(userIdValue, out var userId))
        {
            context.Fail("Token has no valid session.");
            return;
        }

        var services = context.HttpContext.RequestServices;
        var options = services.GetRequiredService<DbContextOptions<AppDbContext>>();
        var utcNow = services.GetRequiredService<IDateTimeProvider>().UtcNow;

        await using var db = new AppDbContext(options, NeutralUserContext.Instance);

        var isActive = await db.UserSessions.AsNoTracking()
            .Where(s => s.Id == sessionId && s.UserId == userId && s.RevokedAtUtc == null && s.ExpiresAtUtc > utcNow)
            .Join(db.Users.IgnoreQueryFilters().AsNoTracking(), s => s.UserId, u => u.Id, (s, u) => u)
            .AnyAsync(u => u.IsActive && !u.IsDeleted, context.HttpContext.RequestAborted);

        if (!isActive)
        {
            context.Fail("Session revoked or user inactive.");
        }
    }

    private sealed class NeutralUserContext : ICurrentUserContext
    {
        public static readonly NeutralUserContext Instance = new();

        public Guid? UserId => null;
        public Guid? BranchId => null;
        public bool IsCrossBranchAccessAllowed => false;
        public string? IpAddress => null;
        public Guid? CorrelationId => null;
    }
}
