using Microsoft.EntityFrameworkCore;
using SupermarketSystem.Application.Common.Interfaces;
using SupermarketSystem.Application.Common.Pagination;

namespace SupermarketSystem.Application.Purchasing.GetSuppliers;

public sealed record GetSuppliersQuery(PagedRequest Paging);

public sealed record SupplierListItemDto(
    Guid Id, string Name, string? ContactName, string? Phone, string? Email, bool IsActive);

public sealed class GetSuppliersHandler
{
    private readonly IApplicationDbContext _context;

    public GetSuppliersHandler(IApplicationDbContext context)
    {
        _context = context;
    }

    public async Task<PagedResult<SupplierListItemDto>> HandleAsync(GetSuppliersQuery query, CancellationToken cancellationToken)
    {
        var paging = query.Paging.Normalized();

        var suppliers = _context.Suppliers.AsNoTracking().AsQueryable();

        if (!string.IsNullOrWhiteSpace(paging.Search))
        {
            var pattern = $"%{paging.Search.Trim()}%";
            suppliers = suppliers.Where(s => EF.Functions.Like(s.Name, pattern));
        }

        suppliers = paging.IsDescending
            ? suppliers.OrderByDescending(s => s.Name).ThenByDescending(s => s.Id)
            : suppliers.OrderBy(s => s.Name).ThenBy(s => s.Id);

        var totalCount = await suppliers.CountAsync(cancellationToken);

        var items = await suppliers
            .Skip(paging.Skip)
            .Take(paging.PageSize)
            .Select(s => new SupplierListItemDto(s.Id, s.Name, s.ContactName, s.Phone, s.Email, s.IsActive))
            .ToListAsync(cancellationToken);

        return new PagedResult<SupplierListItemDto>(items, totalCount, paging.PageNumber, paging.PageSize);
    }
}
