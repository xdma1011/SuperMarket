using Microsoft.EntityFrameworkCore;
using SupermarketSystem.Application.Common.Interfaces;
using SupermarketSystem.Application.Common.Pagination;

namespace SupermarketSystem.Application.Branches.GetBranches;

public sealed record GetBranchesQuery(PagedRequest Paging);

public sealed record BranchListItemDto(Guid Id, string Name, string Code, bool IsActive);

public sealed class GetBranchesHandler
{
    private readonly IApplicationDbContext _context;

    public GetBranchesHandler(IApplicationDbContext context)
    {
        _context = context;
    }

    public async Task<PagedResult<BranchListItemDto>> HandleAsync(GetBranchesQuery query, CancellationToken cancellationToken)
    {
        var paging = query.Paging.Normalized();

        var branches = _context.Branches.AsNoTracking().AsQueryable();

        if (!string.IsNullOrWhiteSpace(paging.Search))
        {
            var pattern = $"%{paging.Search.Trim()}%";
            branches = branches.Where(b => EF.Functions.Like(b.Name, pattern) || EF.Functions.Like(b.Code, pattern));
        }

        branches = paging.IsDescending
            ? branches.OrderByDescending(b => b.Name).ThenByDescending(b => b.Id)
            : branches.OrderBy(b => b.Name).ThenBy(b => b.Id);

        var totalCount = await branches.CountAsync(cancellationToken);

        var items = await branches
            .Skip(paging.Skip)
            .Take(paging.PageSize)
            .Select(b => new BranchListItemDto(b.Id, b.Name, b.Code, b.IsActive))
            .ToListAsync(cancellationToken);

        return new PagedResult<BranchListItemDto>(items, totalCount, paging.PageNumber, paging.PageSize);
    }
}
