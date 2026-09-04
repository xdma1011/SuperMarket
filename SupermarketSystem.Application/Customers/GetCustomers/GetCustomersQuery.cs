using Microsoft.EntityFrameworkCore;
using SupermarketSystem.Application.Common.Interfaces;
using SupermarketSystem.Application.Common.Pagination;

namespace SupermarketSystem.Application.Customers.GetCustomers;

public sealed record GetCustomersQuery(PagedRequest Paging);

public sealed record CustomerListItemDto(Guid Id, string FullName, string? Phone, string? Email, bool IsBlocked, DateTime CreatedAtUtc);

public sealed class GetCustomersHandler
{
    private readonly IApplicationDbContext _context;

    public GetCustomersHandler(IApplicationDbContext context)
    {
        _context = context;
    }

    public async Task<PagedResult<CustomerListItemDto>> HandleAsync(GetCustomersQuery query, CancellationToken cancellationToken)
    {
        var paging = query.Paging.Normalized();

        var customers = _context.Customers.AsNoTracking().Where(c => !c.IsDeleted).AsQueryable();

        if (!string.IsNullOrWhiteSpace(paging.Search))
        {
            var pattern = $"%{paging.Search.Trim()}%";
            customers = customers.Where(c => EF.Functions.Like(c.FullName, pattern) || (c.Phone != null && EF.Functions.Like(c.Phone, pattern)));
        }

        customers = paging.IsDescending
            ? customers.OrderByDescending(c => c.CreatedAtUtc).ThenByDescending(c => c.Id)
            : customers.OrderBy(c => c.FullName).ThenBy(c => c.Id);

        var totalCount = await customers.CountAsync(cancellationToken);

        var items = await customers
            .Skip(paging.Skip)
            .Take(paging.PageSize)
            .Select(c => new CustomerListItemDto(c.Id, c.FullName, c.Phone, c.Email, c.IsBlocked, c.CreatedAtUtc))
            .ToListAsync(cancellationToken);

        return new PagedResult<CustomerListItemDto>(items, totalCount, paging.PageNumber, paging.PageSize);
    }
}
