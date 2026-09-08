using Microsoft.EntityFrameworkCore;
using SupermarketSystem.Application.Common.Interfaces;

namespace SupermarketSystem.Application.Ordering.GetDrivers;

public sealed record DriverDto(Guid Id, string FullName);

/// <summary>قائمة المستخدمين الفعّالين اللي عندهم دور "سائق" (أو أي دور مربوط بصلاحية Orders.Deliver) - تعبّي قائمة اختيار السائق لحظة إسناد الطلب.</summary>
public sealed class GetDriversHandler
{
    private readonly IApplicationDbContext _context;

    public GetDriversHandler(IApplicationDbContext context)
    {
        _context = context;
    }

    public async Task<IReadOnlyList<DriverDto>> HandleAsync(CancellationToken cancellationToken)
    {
        var driverPermissionId = await _context.Permissions.AsNoTracking()
            .Where(p => p.Code == PermissionCodes.OrdersDeliver)
            .Select(p => p.Id)
            .FirstOrDefaultAsync(cancellationToken);

        var driverRoleIds = await _context.Roles.AsNoTracking()
            .Where(r => r.Permissions.Any(p => p.PermissionId == driverPermissionId))
            .Select(r => r.Id)
            .ToListAsync(cancellationToken);

        return await _context.UserRoles.AsNoTracking()
            .Where(ur => driverRoleIds.Contains(ur.RoleId))
            .Join(_context.Users.AsNoTracking(), ur => ur.UserId, u => u.Id, (ur, u) => u)
            .Where(u => u.IsActive && !u.IsDeleted)
            .Distinct()
            .OrderBy(u => u.FullName)
            .Select(u => new DriverDto(u.Id, u.FullName))
            .ToListAsync(cancellationToken);
    }
}
