using Microsoft.EntityFrameworkCore;
using SupermarketSystem.Application.Common.Interfaces;
using SupermarketSystem.Application.Common.Results;
using SupermarketSystem.Domain.Identity;

namespace SupermarketSystem.Application.Authentication.Login;

public static class AuthSettingsKeys
{
    /// <summary>عدد المحاولات الفاشلة المتتالية قبل القفل المؤقت.</summary>
    public const string MaxFailedLoginAttempts = "Auth.MaxFailedLoginAttempts";

    /// <summary>مدة القفل المؤقت بالدقائق بعد بلوغ الحد.</summary>
    public const string LockoutDurationMinutes = "Auth.LockoutDurationMinutes";
}

public sealed record LoginCommand(
    string Username,
    string Password,
    ClientAppType AppType,
    // الفرع المطلوب صراحةً؛ لو null يُستخدم الفرع الافتراضي للمستخدم.
    Guid? BranchId,
    string? IpAddress,
    string? DeviceInfo);

public sealed record LoginResponse(
    string AccessToken,
    DateTime AccessTokenExpiresAtUtc,
    string RefreshToken,
    DateTime RefreshTokenExpiresAtUtc,
    Guid UserId,
    string FullName,
    Guid? BranchId,
    // true لو هذا الدخول سحب جلسة قائمة لنفس المستخدم بنفس نوع التطبيق —
    // إشارة تستحق انتباه المستخدم نفسه ("في حدا تاني كان داخل بحسابك").
    bool PreviousSessionRevoked);

/// <summary>
/// ═══════════════════════════════════════════════════════════════════
/// مبدأ حاكم: رسالة فشل واحدة لكل الأسباب.
/// ═══════════════════════════════════════════════════════════════════
/// اسم مستخدم غير موجود، كلمة سر غلط، حساب معطَّل، حساب محذوف — كلهم
/// بيرجّعوا نفس الخطأ ونفس النص بالضبط. التمييز بينهم بيعطي مهاجمًا
/// طريقة يعدّ فيها أسماء المستخدمين الصحيحة ("هذا الاسم موجود بس كلمة
/// السر غلط") — وهذا أول شي بيعمله قبل ما يبدأ تخمين كلمات السر.
///
/// الأسباب الحقيقية تُسجَّل بـUserLoginLog للتدقيق، بس ما بتوصل للمستخدم.
/// </summary>
public sealed class LoginHandler
{
    private readonly IApplicationDbContext _context;
    private readonly IPasswordHasher _passwordHasher;
    private readonly ITokenService _tokenService;
    private readonly ISettingsProvider _settingsProvider;
    private readonly IDateTimeProvider _dateTimeProvider;

    private static Error InvalidCredentials()
        => Error.Forbidden("Auth.InvalidCredentials", "اسم المستخدم أو كلمة السر غير صحيحة.");

    public LoginHandler(
        IApplicationDbContext context,
        IPasswordHasher passwordHasher,
        ITokenService tokenService,
        ISettingsProvider settingsProvider,
        IDateTimeProvider dateTimeProvider)
    {
        _context = context;
        _passwordHasher = passwordHasher;
        _tokenService = tokenService;
        _settingsProvider = settingsProvider;
        _dateTimeProvider = dateTimeProvider;
    }

    public async Task<Result<LoginResponse>> HandleAsync(LoginCommand command, CancellationToken cancellationToken)
    {
        if (string.IsNullOrWhiteSpace(command.Username) || string.IsNullOrWhiteSpace(command.Password))
        {
            return Result.Failure<LoginResponse>(InvalidCredentials());
        }

        var utcNow = _dateTimeProvider.UtcNow;

        // IgnoreQueryFilters: المرشِّح العام بيخفي المستخدمين المحذوفين
        // ناعمًا. بدونها، محاولة دخول بحساب محذوف بترجّع "غير موجود" —
        // وهي نفس النتيجة، بس ما رح ينكتب سطر تدقيق لأنه ما لقينا مستخدمًا
        // نربط المحاولة فيه. جلبه صراحةً بيخلينا نسجّل المحاولة ثم نرفضها.
        var user = await _context.Users
            .IgnoreQueryFilters()
            .FirstOrDefaultAsync(u => u.Username == command.Username, cancellationToken);

        if (user is null)
        {
            // ما في UserLoginLog هون: الجدول بيتطلب UserId حقيقيًا (FK
            // مقيَّد)، وما عنا مستخدم نربط فيه محاولة باسم غير موجود
            // أصلًا. تتبّع هذه المحاولات محتاج مسارًا مستقلًا عن UserId
            // (الخطوة 5، حد المحاولات الفاشلة).
            return Result.Failure<LoginResponse>(InvalidCredentials());
        }

        if (string.IsNullOrWhiteSpace(user.PasswordHash))
        {
            await LogAttemptAsync(user.Id, command, success: false, utcNow, cancellationToken);
            return Result.Failure<LoginResponse>(InvalidCredentials());
        }

        // === حد محاولات الدخول الفاشلة ===
        // يُفحص *قبل* التحقق من كلمة السر، لا بعده — فحص كلمة السر لحساب
        // مقفول مؤقتًا بيضيف تأخير معالجة بلا أي فائدة، وممكن يسرّب توقيت
        // مختلف قابل للاستغلال (من عرف كلمة السر الصحيحة ضد حساب مقفول
        // ممكن يميّز التوقيت عن حساب فعلي بلا كلمة سر صحيحة).
        var lockoutError = await CheckLockoutAsync(user.Id, utcNow, cancellationToken);
        if (lockoutError is not null)
        {
            // ملاحظة: ما بنسجّل محاولة جديدة هون — الحساب مقفول أصلًا،
            // ومحاولة إضافية فاشلة ما بتضيف معلومة، بس بتطيل مدة القفل
            // بلا داعٍ لو حسبنا "آخر فشل" من هذا التسجيل بالذات.
            return Result.Failure<LoginResponse>(lockoutError);
        }

        var verification = _passwordHasher.Verify(command.Password, user.PasswordHash);

        if (verification == PasswordVerificationOutcome.Failed)
        {
            await LogAttemptAsync(user.Id, command, success: false, utcNow, cancellationToken);
            return Result.Failure<LoginResponse>(InvalidCredentials());
        }

        // كلمة السر صحيحة — لكن الحساب نفسه ممكن يكون معطَّلًا أو محذوفًا.
        // نفس الرسالة بالضبط، عن قصد (راجع تعليق الصنف).
        if (!user.IsActive || user.IsDeleted)
        {
            await LogAttemptAsync(user.Id, command, success: false, utcNow, cancellationToken);
            return Result.Failure<LoginResponse>(InvalidCredentials());
        }

        // ترقية البصمة لو كانت مبنية بإعدادات قديمة — هذه اللحظة الوحيدة
        // اللي فيها كلمة السر الأصلية متاحة.
        if (verification == PasswordVerificationOutcome.SuccessRehashNeeded)
        {
            user.SetPasswordHash(_passwordHasher.Hash(command.Password));
        }

        // === صلاحية تجاوز الفروع — تُحسب هون، تُحفظ بالتوكن، ما تُفحص حيًّا بعدها ===
        // (راجع تعليق RealCurrentUserContext للسبب: استعلام حي وقت كل
        // طلب كان رح يعمل اعتمادية دائرية مع AppDbContext).
        var allowCrossBranch = await HasCrossBranchPermissionAsync(user.Id, cancellationToken);

        // === تحديد الفرع ===
        var branchResult = await ResolveBranchAsync(user.Id, command.BranchId, cancellationToken);
        if (branchResult.IsFailure)
        {
            await LogAttemptAsync(user.Id, command, success: false, utcNow, cancellationToken);
            return Result.Failure<LoginResponse>(branchResult.Error!);
        }

        var branchId = branchResult.Value;

        // === سحب أي جلسة قائمة لنفس (المستخدم + نوع التطبيق) ===
        var existingSessions = await _context.UserSessions
            .Where(s => s.UserId == user.Id && s.AppType == command.AppType && s.RevokedAtUtc == null)
            .ToListAsync(cancellationToken);

        var previousSessionRevoked = false;

        foreach (var session in existingSessions)
        {
            // الجلسات المنتهية طبيعيًا تُترك كما هي: "انتهت صلاحيتها"
            // و"سُحبت لدخول جديد" حدثان مختلفان، ودمجهما بيضيّع إشارة
            // أمنية حقيقية (دخول متزامن من مكانين).
            if (!session.IsActive(utcNow))
            {
                continue;
            }

            session.Revoke(SessionRevocationReason.NewLoginElsewhere, utcNow);
            previousSessionRevoked = true;
        }

        // === الجلسة الجديدة ===
        var refreshToken = _tokenService.CreateRefreshToken();

        var newSession = new UserSession(
            user.Id, command.AppType, refreshToken.TokenHash, branchId,
            command.IpAddress, command.DeviceInfo, utcNow, refreshToken.ExpiresAtUtc);

        _context.UserSessions.Add(newSession);

        var accessToken = _tokenService.CreateAccessToken(
            user.Id, user.Username, command.AppType, newSession.Id, branchId, allowCrossBranch);

        await LogAttemptAsync(user.Id, command, success: true, utcNow, cancellationToken);
        await _context.SaveChangesAsync(cancellationToken);

        return Result.Success(new LoginResponse(
            accessToken.Token, accessToken.ExpiresAtUtc,
            refreshToken.Token, refreshToken.ExpiresAtUtc,
            user.Id, user.FullName, branchId, previousSessionRevoked));
    }

    /// <summary>
    /// الفرع المطلوب لازم يكون من الفروع المصرَّح للمستخدم بها — فحص هون
    /// وليس تفصيلًا شكليًا: بدونه يقدر أي مستخدم يطلب أي فرع وقت الدخول
    /// ويحصل على توكن يحمل فرعًا لا يخصه، فينهار عزل الفروع كله من أول
    /// خطوة.
    /// </summary>
    private async Task<Result<Guid?>> ResolveBranchAsync(Guid userId, Guid? requestedBranchId, CancellationToken cancellationToken)
    {
        var assignments = await _context.UserBranches.AsNoTracking()
            .Where(ub => ub.UserId == userId)
            .Select(ub => new { ub.BranchId, ub.IsDefault })
            .ToListAsync(cancellationToken);

        if (requestedBranchId is { } requested)
        {
            if (!assignments.Any(a => a.BranchId == requested))
            {
                return Result.Failure<Guid?>(
                    Error.Forbidden("Auth.BranchNotAssigned", "ليس لديك صلاحية العمل على هذا الفرع."));
            }

            return Result.Success<Guid?>(requested);
        }

        // بلا فرع مطلوب: الافتراضي، ثم أي فرع مُسنَد، ثم لا شيء.
        // null مقبول عمدًا — مستخدم إدارة مركزي قد لا يكون مرتبطًا بفرع
        // إطلاقًا، وإجباره على فرع كان رح يعطيه سياقًا كاذبًا.
        var defaultBranch = assignments.FirstOrDefault(a => a.IsDefault) ?? assignments.FirstOrDefault();

        return Result.Success(defaultBranch?.BranchId);
    }

    /// <summary>
    /// قفل مبني على "محاولات فاشلة متتالية منذ آخر دخول ناجح" — لا نافذة
    /// زمنية بسيطة (آخر X دقيقة). الفرق مهم: نافذة زمنية بسيطة بتنسى
    /// المحاولات القديمة تلقائيًا حتى لو ما كان في دخول ناجح بينهم، بينما
    /// هذا التصميم بيصفّر العدّاد *فقط* بدخول ناجح فعلي — محاولات فاشلة
    /// متباعدة زمنيًا (محاولة كل ساعة مثلًا، لتفادي أي نافذة) لسا بتتراكم
    /// وتوصل للقفل بالنهاية.
    /// </summary>
    private async Task<Error?> CheckLockoutAsync(Guid userId, DateTime utcNow, CancellationToken cancellationToken)
    {
        var maxAttempts = (int)await _settingsProvider.GetDecimalAsync(
            AuthSettingsKeys.MaxFailedLoginAttempts, defaultValue: 5m, cancellationToken);

        if (maxAttempts <= 0)
        {
            // 0 أو أقل = تعطيل القفل كليًا — قرار إداري صريح.
            return null;
        }

        var recentAttempts = await _context.UserLoginLogs.AsNoTracking()
            .Where(l => l.UserId == userId)
            .OrderByDescending(l => l.AttemptedAtUtc)
            .Take(maxAttempts)
            .Select(l => new { l.Success, l.AttemptedAtUtc })
            .ToListAsync(cancellationToken);

        // لسه ما وصلنا لعدد المحاولات الكافي لتقييم القفل، أو آخر محاولة
        // بينهم كانت ناجحة (يعني العدّاد اتصفّر لاحقًا) — بلا قفل.
        if (recentAttempts.Count < maxAttempts || recentAttempts.Any(a => a.Success))
        {
            return null;
        }

        var lockoutMinutes = await _settingsProvider.GetDecimalAsync(
            AuthSettingsKeys.LockoutDurationMinutes, defaultValue: 15m, cancellationToken);

        var mostRecentFailureAt = recentAttempts[0].AttemptedAtUtc; // الأحدث أول (ترتيب تنازلي)
        var lockedUntilUtc = mostRecentFailureAt.AddMinutes((double)lockoutMinutes);

        if (utcNow >= lockedUntilUtc)
        {
            // مدة القفل انتهت — العدّاد ما ينصفّر تلقائيًا (لسه في نفس
            // العدد من المحاولات الفاشلة بالسجل)، بس القفل نفسه ما عاد
            // فعّالًا. محاولة فاشلة جديدة بترجّع القفل فورًا (نفس منطق
            // "محاولات متتالية").
            return null;
        }

        var remainingMinutes = Math.Ceiling((lockedUntilUtc - utcNow).TotalMinutes);
        return Error.Forbidden(
            "Auth.AccountLocked",
            $"الحساب مقفل مؤقتًا بسبب محاولات دخول فاشلة متكررة. حاول مرة أخرى بعد {remainingMinutes} دقيقة تقريبًا.");
    }

    /// <summary>
    /// true لو أي دور مربوط بالمستخدم عنده صلاحية CrossBranchAccess —
    /// عبر UserRole → RolePermission → Permission، بلا تحميل الصفوف
    /// كاملة للذاكرة.
    /// </summary>
    private async Task<bool> HasCrossBranchPermissionAsync(Guid userId, CancellationToken cancellationToken)
    {
        return await _context.UserRoles.AsNoTracking()
            .Where(ur => ur.UserId == userId)
            .Join(_context.RolePermissions.AsNoTracking(), ur => ur.RoleId, rp => rp.RoleId, (ur, rp) => rp.PermissionId)
            .Join(_context.Permissions.AsNoTracking(), pid => pid, p => p.Id, (pid, p) => p.Code)
            .AnyAsync(code => code == PermissionCodes.CrossBranchAccess, cancellationToken);
    }

    private async Task LogAttemptAsync(
        Guid userId, LoginCommand command, bool success, DateTime utcNow, CancellationToken cancellationToken)
    {
        _context.UserLoginLogs.Add(new UserLoginLog(userId, command.BranchId, utcNow, success, command.IpAddress));

        // المحاولات الفاشلة تُحفظ فورًا: مسار الفشل بيرجع مباشرة بعدها بلا
        // ما يوصل لـSaveChanges النهائي، فبلا هذا الحفظ ما رح ينكتب ولا
        // سطر تدقيق لأي محاولة فاشلة — وهي بالضبط المحاولات اللي التسجيل
        // موجود لأجلها.
        if (!success)
        {
            await _context.SaveChangesAsync(cancellationToken);
        }
    }
}
