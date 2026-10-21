using Microsoft.EntityFrameworkCore;
using SupermarketSystem.Application.Authentication.Login;
using SupermarketSystem.Application.Common.Interfaces;
using SupermarketSystem.Application.Common.Results;
using SupermarketSystem.Domain.Identity;

namespace SupermarketSystem.Application.Partners;

/// <summary>
/// إعادة تحقق من كلمة سر <b>المستخدم الحالي نفسه</b> قبل عمليات الشركاء الحرجة (إعادة الاستثمار، التراجع عنها) - الحماية من الكبسة الغلط
/// أو من جلسة متروكة مفتوحة. الفشل بيتسجّل بنفس سجل الدخول (<see cref="UserLoginLog"/>) فبيتفعّل نفس قفل الدخول، بس العدّ
/// ضمن نافذة القفل بس: غلطات قديمة ما بتتراكم مع غلطة اليوم (تسجيل الدخول الناجح ما بيصير بين عمليات الشاشة عشان يصفّر العدّاد).
/// </summary>
public sealed class PartnerActionPasswordVerifier
{
    private readonly IApplicationDbContext _context;
    private readonly ICurrentUserContext _currentUser;
    private readonly IPasswordHasher _passwordHasher;
    private readonly ISettingsProvider _settingsProvider;
    private readonly IDateTimeProvider _dateTimeProvider;

    public PartnerActionPasswordVerifier(
        IApplicationDbContext context, ICurrentUserContext currentUser, IPasswordHasher passwordHasher,
        ISettingsProvider settingsProvider, IDateTimeProvider dateTimeProvider)
    {
        _context = context;
        _currentUser = currentUser;
        _passwordHasher = passwordHasher;
        _settingsProvider = settingsProvider;
        _dateTimeProvider = dateTimeProvider;
    }

    /// <summary>null = كلمة السر صحيحة. <paramref name="invalidPassword"/> هو الخطأ اللي بيرجع لكل أنواع الفشل (ما بنفرّق عن قصد).</summary>
    public async Task<Error?> VerifyAsync(Guid userId, Guid branchId, string? password, Error invalidPassword, CancellationToken cancellationToken)
    {
        if (string.IsNullOrEmpty(password))
        {
            return invalidPassword;
        }

        var user = await _context.Users.IgnoreQueryFilters().FirstOrDefaultAsync(u => u.Id == userId, cancellationToken);
        if (user is null || string.IsNullOrWhiteSpace(user.PasswordHash) || !user.IsActive || user.IsDeleted)
        {
            return invalidPassword;
        }

        var now = _dateTimeProvider.UtcNow;
        if (await IsLockedOutAsync(user.Id, now, cancellationToken))
        {
            return Error.Forbidden("Auth.AccountLocked", "الحساب مقفل مؤقتًا بسبب محاولات كلمة سر فاشلة متكررة - جرّب بعدين.");
        }

        if (_passwordHasher.Verify(password, user.PasswordHash) == PasswordVerificationOutcome.Failed)
        {
            _context.UserLoginLogs.Add(new UserLoginLog(user.Id, branchId, now, false, _currentUser.IpAddress));
            await _context.SaveChangesAsync(cancellationToken);
            return invalidPassword;
        }

        return null;
    }

    private async Task<bool> IsLockedOutAsync(Guid userId, DateTime utcNow, CancellationToken cancellationToken)
    {
        var maxAttempts = (int)await _settingsProvider.GetDecimalAsync(AuthSettingsKeys.MaxFailedLoginAttempts, 5m, cancellationToken);
        if (maxAttempts <= 0)
        {
            return false;
        }

        var lockoutMinutes = await _settingsProvider.GetDecimalAsync(AuthSettingsKeys.LockoutDurationMinutes, 15m, cancellationToken);
        var windowStart = utcNow.AddMinutes(-(double)lockoutMinutes);
        var recent = await _context.UserLoginLogs.AsNoTracking()
            .Where(l => l.UserId == userId && l.AttemptedAtUtc > windowStart)
            .OrderByDescending(l => l.AttemptedAtUtc)
            .Take(maxAttempts)
            .Select(l => new { l.Success })
            .ToListAsync(cancellationToken);

        return recent.Count >= maxAttempts && !recent.Any(a => a.Success);
    }
}
