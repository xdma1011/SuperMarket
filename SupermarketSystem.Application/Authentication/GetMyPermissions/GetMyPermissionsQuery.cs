using Microsoft.EntityFrameworkCore;
using SupermarketSystem.Application.Common.Interfaces;

namespace SupermarketSystem.Application.Authentication.GetMyPermissions;

public sealed record MyPermissionsResponse(IReadOnlyList<string> PermissionCodes);

/// <summary>
/// يقرأ هوية المستخدم من ICurrentUserContext (الـclaims) — يرجّع صلاحيات
/// *المتصل نفسه* دائمًا. الفرونت إند بيستخدمه لإخفاء عناصر Sidebar
/// وتعطيل أزرار — راحة تجربة لا أمان حقيقي. **الأمان الفعلي دائمًا
/// بالباك إند** (RequirePermissionFilter على كل endpoint).
/// </summary>
public sealed class GetMyPermissionsHandler
{
    private readonly IApplicationDbContext _context;
    private readonly ICurrentUserContext _currentUser;

    public GetMyPermissionsHandler(IApplicationDbContext context, ICurrentUserContext currentUser)
    {
        _context = context;
        _currentUser = currentUser;
    }

    public async Task<MyPermissionsResponse> HandleAsync(CancellationToken cancellationToken)
    {
        if (_currentUser.UserId is not { } userId)
        {
            return new MyPermissionsResponse(Array.Empty<string>());
        }

        var codes = await _context.UserRoles.AsNoTracking()
            .Where(ur => ur.UserId == userId)
            .Join(_context.RolePermissions.AsNoTracking(), ur => ur.RoleId, rp => rp.RoleId, (ur, rp) => rp.PermissionId)
            .Join(_context.Permissions.AsNoTracking(), pid => pid, p => p.Id, (pid, p) => p.Code)
            .Distinct()
            .ToListAsync(cancellationToken);

        return new MyPermissionsResponse(codes);
    }
}
