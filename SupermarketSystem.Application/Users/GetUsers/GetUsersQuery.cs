using Microsoft.EntityFrameworkCore;
using SupermarketSystem.Application.Common.Interfaces;
using SupermarketSystem.Application.Common.Pagination;

namespace SupermarketSystem.Application.Users.GetUsers;

public sealed record GetUsersQuery(PagedRequest Paging);

/// <summary>
/// RoleId وDefaultBranchId (بجانب الأسماء) أُضيفا لدعم شاشة التعديل —
/// نموذج Edit محتاج المعرّفات الفعلية ليعبّي قوائم الاختيار مسبقًا، لا
/// الأسماء بس (اللي كافية للعرض فقط).
/// </summary>
public sealed record UserItemDto(
    Guid UserId,
    string FullName,
    string Username,
    string Email,
    bool IsActive,
    IReadOnlyList<string> RoleNames,
    Guid? RoleId,
    string? DefaultBranchName,
    Guid? DefaultBranchId);

public sealed class GetUsersHandler
{
    private readonly IApplicationDbContext _context;

    public GetUsersHandler(IApplicationDbContext context)
    {
        _context = context;
    }

    public async Task<PagedResult<UserItemDto>> HandleAsync(GetUsersQuery query, CancellationToken cancellationToken)
    {
        var paging = query.Paging.Normalized();

        var usersQuery = _context.Users.AsNoTracking().IgnoreQueryFilters()
            .Where(u => !u.IsDeleted)
            .OrderBy(u => u.FullName);

        var totalCount = await usersQuery.CountAsync(cancellationToken);

        var pageUsers = await usersQuery
            .Skip(paging.Skip)
            .Take(paging.PageSize)
            .Select(u => new { u.Id, u.FullName, u.Username, u.Email, u.IsActive })
            .ToListAsync(cancellationToken);

        var userIds = pageUsers.Select(u => u.Id).ToList();

        var rolesByUser = await _context.UserRoles.AsNoTracking()
            .Where(ur => userIds.Contains(ur.UserId))
            .Join(_context.Roles.AsNoTracking(), ur => ur.RoleId, r => r.Id, (ur, r) => new { ur.UserId, r.Id, r.Name })
            .ToListAsync(cancellationToken);

        var defaultBranchByUser = await _context.UserBranches.AsNoTracking()
            .Where(ub => userIds.Contains(ub.UserId) && ub.IsDefault)
            .Join(_context.Branches.AsNoTracking(), ub => ub.BranchId, b => b.Id, (ub, b) => new { ub.UserId, b.Id, b.Name })
            .ToDictionaryAsync(x => x.UserId, x => x, cancellationToken);

        var items = pageUsers.Select(u =>
        {
            var userRoles = rolesByUser.Where(r => r.UserId == u.Id).ToList();
            var defaultBranch = defaultBranchByUser.GetValueOrDefault(u.Id);

            return new UserItemDto(
                u.Id, u.FullName, u.Username, u.Email, u.IsActive,
                userRoles.Select(r => r.Name).ToList(),
                userRoles.Select(r => (Guid?)r.Id).FirstOrDefault(),
                defaultBranch?.Name,
                defaultBranch?.Id);
        }).ToList();

        return new PagedResult<UserItemDto>(items, totalCount, paging.PageNumber, paging.PageSize);
    }
}
