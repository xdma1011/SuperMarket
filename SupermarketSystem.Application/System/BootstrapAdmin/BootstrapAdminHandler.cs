using Microsoft.EntityFrameworkCore;
using SupermarketSystem.Application.Common.Interfaces;
using SupermarketSystem.Application.Common.Results;
using SupermarketSystem.Domain.Branches;
using SupermarketSystem.Domain.Identity;

namespace SupermarketSystem.Application.System.BootstrapAdmin;

public sealed record BootstrapAdminResponse(
    Guid UserId, string Username, string Password, Guid BranchId, string BranchName);

/// <summary>
/// نقطة تمهيد لمرة واحدة — تنشئ فرعًا رئيسيًا ومستخدمًا إداريًا كامل
/// الصلاحيات (Master Admin) دفعة واحدة، بكلمة سر افتراضية ثابتة (123)
/// لسهولة أول دخول فقط.
///
/// أمان مقصود مبسَّط، لا معدوم بالكامل: لا فحص صلاحية على هذا الـendpoint
/// (طبيعي — قبل وجود أي مستخدم، ما في هوية تُفحص أصلًا). الحارس الوحيد:
/// **يرفض العمل لو يوجد مستخدم واحد على الأقل بقاعدة البيانات أصلًا** —
/// يعني قابل للتشغيل مرة وحدة بس، أول مرة، على نظام فارغ تمامًا. بعدها
/// بيقفل نفسه تلقائيًا، بلا حاجة نحذفه من الكود يدويًا قبل أي نشر فعلي.
///
/// كلمة السر الافتراضية (123) لازم تتغيّر فورًا بعد أول دخول — هذا
/// endpoint إعداد مبدئي بحت، لا مسار مُعتمَد للاستخدام المتكرر.
/// </summary>
public sealed class BootstrapAdminHandler
{
    // معرّف دور "Master Admin" الثابت من الـseed (IdentityConfigurations.cs).
    private static readonly Guid MasterAdminRoleId = Guid.Parse("50e6125a-cac0-4d82-a0b8-9f3c6fff59d7");

    private const string DefaultUsername = "admin";
    private const string DefaultPassword = "123";
    private const string MainBranchName = "الفرع الرئيسي";
    private const string MainBranchCode = "MAIN";

    private readonly IApplicationDbContext _context;
    private readonly IPasswordHasher _passwordHasher;

    public BootstrapAdminHandler(IApplicationDbContext context, IPasswordHasher passwordHasher)
    {
        _context = context;
        _passwordHasher = passwordHasher;
    }

    public async Task<Result<BootstrapAdminResponse>> HandleAsync(CancellationToken cancellationToken)
    {
        var anyUserExists = await _context.Users.IgnoreQueryFilters().AnyAsync(cancellationToken);
        if (anyUserExists)
        {
            return Result.Failure<BootstrapAdminResponse>(Error.Conflict(
                "System.AlreadyBootstrapped",
                "يوجد مستخدم مسجَّل أصلًا بالنظام — نقطة التمهيد هذه تعمل مرة واحدة فقط على نظام فارغ."));
        }

        var branch = new Branch(MainBranchName, MainBranchCode, address: null, phoneNumber: null);
        _context.Branches.Add(branch);

        var user = new User(fullName: "مدير النظام", username: DefaultUsername, email: "admin@local.test");
        user.SetPasswordHash(_passwordHasher.Hash(DefaultPassword));
        _context.Users.Add(user);

        _context.UserRoles.Add(new UserRole(user.Id, MasterAdminRoleId, branchId: null));
        _context.UserBranches.Add(new UserBranch(user.Id, branch.Id, isDefault: true));

        await _context.SaveChangesAsync(cancellationToken);

        return Result.Success(new BootstrapAdminResponse(
            user.Id, DefaultUsername, DefaultPassword, branch.Id, branch.Name));
    }
}
