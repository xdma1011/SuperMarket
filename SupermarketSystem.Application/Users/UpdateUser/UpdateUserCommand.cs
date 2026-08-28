using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Caching.Memory;
using SupermarketSystem.Application.Common.Interfaces;
using SupermarketSystem.Application.Common.Results;
using SupermarketSystem.Domain.Identity;

namespace SupermarketSystem.Application.Users.UpdateUser;

public sealed record UpdateUserCommand(
    Guid UserId,
    string FullName,
    string Email,
    Guid RoleId,
    Guid BranchId,
    bool IsActive);

/// <summary>
/// أول Edit حقيقي لكيان المستخدم — بروفايل + دور + فرع افتراضي + تفعيل،
/// بطلب واحد. UserRole ثابتة بعد الإنشاء (بلا ميثود تعديل بالـDomain
/// عمدًا) — حذف القديم وإضافة جديد أوضح من "تعديل" علاقة بسيطة.
///
/// تحذير أمني مهم: CachedPermissionChecker بيخبّئ صلاحيات المستخدم 60
/// ثانية. لو غيّرنا دور مستخدم هون بلا مسح الكاش، ممكن يضل عنده
/// صلاحياته القديمة لحد 60 ثانية إضافية. الحل: IMemoryCache تُستهلَك
/// مباشرة هون لمسح مفتاح هذا المستخدم فور التعديل.
/// </summary>
public sealed class UpdateUserHandler
{
    private readonly IApplicationDbContext _context;
    private readonly IMemoryCache _cache;

    public UpdateUserHandler(IApplicationDbContext context, IMemoryCache cache)
    {
        _context = context;
        _cache = cache;
    }

    public async Task<Result> HandleAsync(UpdateUserCommand command, CancellationToken cancellationToken)
    {
        if (string.IsNullOrWhiteSpace(command.FullName))
        {
            return Result.Failure(Error.Validation("User.FullNameRequired", "الاسم الكامل مطلوب."));
        }

        var user = await _context.Users.IgnoreQueryFilters()
            .FirstOrDefaultAsync(u => u.Id == command.UserId, cancellationToken);

        if (user is null)
        {
            return Result.Failure(Error.NotFound("User.NotFound", $"المستخدم '{command.UserId}' غير موجود."));
        }

        var roleExists = await _context.Roles.AsNoTracking().AnyAsync(r => r.Id == command.RoleId, cancellationToken);
        if (!roleExists)
        {
            return Result.Failure(Error.NotFound("User.RoleNotFound", $"الدور '{command.RoleId}' غير موجود."));
        }

        var branchExists = await _context.Branches.AsNoTracking().AnyAsync(b => b.Id == command.BranchId, cancellationToken);
        if (!branchExists)
        {
            return Result.Failure(Error.NotFound("User.BranchNotFound", $"الفرع '{command.BranchId}' غير موجود."));
        }

        user.UpdateProfile(command.FullName.Trim(), command.Email.Trim());

        if (command.IsActive)
        {
            user.Activate();
        }
        else
        {
            user.Deactivate();
        }

        var existingRoles = await _context.UserRoles
            .Where(ur => ur.UserId == command.UserId)
            .ToListAsync(cancellationToken);
        foreach (var role in existingRoles)
        {
            _context.UserRoles.Remove(role);
        }
        _context.UserRoles.Add(new UserRole(command.UserId, command.RoleId, branchId: null));

        var existingBranches = await _context.UserBranches
            .Where(ub => ub.UserId == command.UserId)
            .ToListAsync(cancellationToken);
        foreach (var branch in existingBranches)
        {
            _context.UserBranches.Remove(branch);
        }
        _context.UserBranches.Add(new UserBranch(command.UserId, command.BranchId, isDefault: true));

        await _context.SaveChangesAsync(cancellationToken);

        // إبطال فوري لكاش صلاحيات هذا المستخدم — نفس مفتاح
        // CachedPermissionChecker بالضبط (userpermissions:{userId}).
        _cache.Remove($"userpermissions:{command.UserId}");

        return Result.Success();
    }
}
