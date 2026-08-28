using Microsoft.EntityFrameworkCore;
using SupermarketSystem.Application.Common.Interfaces;

namespace SupermarketSystem.Application.Users.GetRoles;

public sealed record RoleItemDto(Guid Id, string Name, string? Description);

/// <summary>بلا ترقيم صفحات — عدد الأدوار قليل بطبيعته، وهذا الاستعلام الأساسي لتعبئة قائمة اختيار بنموذج إنشاء مستخدم.</summary>
public sealed class GetRolesHandler
{
    private readonly IApplicationDbContext _context;

    public GetRolesHandler(IApplicationDbContext context)
    {
        _context = context;
    }

    public async Task<IReadOnlyList<RoleItemDto>> HandleAsync(CancellationToken cancellationToken)
    {
        return await _context.Roles.AsNoTracking()
            .Where(r => r.IsActive)
            .OrderBy(r => r.Name)
            .Select(r => new RoleItemDto(r.Id, r.Name, r.Description))
            .ToListAsync(cancellationToken);
    }
}
