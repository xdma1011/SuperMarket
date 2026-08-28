using Microsoft.EntityFrameworkCore;
using SupermarketSystem.Application.Common.Interfaces;
using SupermarketSystem.Application.Common.Results;
using SupermarketSystem.Domain.Identity;

namespace SupermarketSystem.Application.Users.CreateUser;

public sealed record CreateUserCommand(
    string FullName,
    string Username,
    string Email,
    string Password,
    Guid RoleId,
    Guid BranchId);

public sealed record CreateUserResponse(Guid UserId, string Username);

public static class CreateUserValidator
{
    public static Error? Validate(CreateUserCommand command)
    {
        if (string.IsNullOrWhiteSpace(command.FullName))
        {
            return Error.Validation("User.FullNameRequired", "الاسم الكامل مطلوب.");
        }

        if (string.IsNullOrWhiteSpace(command.Username))
        {
            return Error.Validation("User.UsernameRequired", "اسم المستخدم مطلوب.");
        }

        if (string.IsNullOrWhiteSpace(command.Password) || command.Password.Length < 3)
        {
            return Error.Validation("User.PasswordTooShort", "كلمة السر قصيرة جدًا.");
        }

        return null;
    }
}

/// <summary>
/// ينشئ مستخدمًا، يجزّئ كلمة سره فورًا، ويربطه بدور وفرع بنفس العملية —
/// نفس نمط BootstrapAdminHandler بالضبط، بس عام لأي مستخدم لاحق.
/// </summary>
public sealed class CreateUserHandler
{
    private readonly IApplicationDbContext _context;
    private readonly IPasswordHasher _passwordHasher;

    public CreateUserHandler(IApplicationDbContext context, IPasswordHasher passwordHasher)
    {
        _context = context;
        _passwordHasher = passwordHasher;
    }

    public async Task<Result<CreateUserResponse>> HandleAsync(CreateUserCommand command, CancellationToken cancellationToken)
    {
        var validationError = CreateUserValidator.Validate(command);
        if (validationError is not null)
        {
            return Result.Failure<CreateUserResponse>(validationError);
        }

        var usernameTaken = await _context.Users.IgnoreQueryFilters()
            .AnyAsync(u => u.Username == command.Username, cancellationToken);
        if (usernameTaken)
        {
            return Result.Failure<CreateUserResponse>(
                Error.Conflict("User.UsernameTaken", $"اسم المستخدم '{command.Username}' مستخدَم أصلًا."));
        }

        var roleExists = await _context.Roles.AsNoTracking().AnyAsync(r => r.Id == command.RoleId, cancellationToken);
        if (!roleExists)
        {
            return Result.Failure<CreateUserResponse>(
                Error.NotFound("User.RoleNotFound", $"الدور '{command.RoleId}' غير موجود."));
        }

        var branchExists = await _context.Branches.AsNoTracking().AnyAsync(b => b.Id == command.BranchId, cancellationToken);
        if (!branchExists)
        {
            return Result.Failure<CreateUserResponse>(
                Error.NotFound("User.BranchNotFound", $"الفرع '{command.BranchId}' غير موجود."));
        }

        var user = new User(command.FullName, command.Username, command.Email);
        user.SetPasswordHash(_passwordHasher.Hash(command.Password));
        _context.Users.Add(user);

        _context.UserRoles.Add(new UserRole(user.Id, command.RoleId, branchId: null));
        _context.UserBranches.Add(new UserBranch(user.Id, command.BranchId, isDefault: true));

        await _context.SaveChangesAsync(cancellationToken);

        return Result.Success(new CreateUserResponse(user.Id, user.Username));
    }
}
