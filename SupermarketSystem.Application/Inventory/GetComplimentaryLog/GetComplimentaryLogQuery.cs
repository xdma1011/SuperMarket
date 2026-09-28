using Microsoft.EntityFrameworkCore;
using SupermarketSystem.Application.Common.Interfaces;
using SupermarketSystem.Application.Common.Pagination;
using SupermarketSystem.Domain.Inventory;

namespace SupermarketSystem.Application.Inventory.GetComplimentaryLog;

public sealed record GetComplimentaryLogQuery(PagedRequest Paging, Guid? BranchId, DateTime? FromUtc, DateTime? ToUtc);

public sealed record ComplimentaryLogItemDto(
    Guid StockMovementId,
    string ProductName,
    decimal QuantityBase,
    string? Notes,
    bool NeedsReview,
    DateTime OccurredAtUtc,
    string Username);

/// <summary>
/// سجل الضيافة/الاستهلاك الداخلي (MovementType.ComplimentaryOut) - جدول بصفحة الضيافة: شو، قديش، مين،
/// إمتى، وهل انعلّم للمراجعة (فوق الحد اليومي). الأحدث أولًا. نفس فلسفة GetWasteLogHandler، بس منفصل عنه.
/// </summary>
public sealed class GetComplimentaryLogHandler
{
    private readonly IApplicationDbContext _context;

    public GetComplimentaryLogHandler(IApplicationDbContext context)
    {
        _context = context;
    }

    public async Task<PagedResult<ComplimentaryLogItemDto>> HandleAsync(GetComplimentaryLogQuery query, CancellationToken cancellationToken)
    {
        var paging = query.Paging.Normalized();

        var movements = _context.StockMovements.AsNoTracking()
            .Where(m => m.MovementType == MovementType.ComplimentaryOut);

        if (query.BranchId is { } branchId)
        {
            movements = movements.Where(m => m.BranchId == branchId);
        }

        if (query.FromUtc is { } fromUtc)
        {
            movements = movements.Where(m => m.OccurredAtUtc >= fromUtc);
        }

        if (query.ToUtc is { } toUtc)
        {
            movements = movements.Where(m => m.OccurredAtUtc <= toUtc);
        }

        var totalCount = await movements.CountAsync(cancellationToken);

        var rows = await movements
            .OrderByDescending(m => m.OccurredAtUtc)
            .Skip(paging.Skip)
            .Take(paging.PageSize)
            .Join(_context.Products.IgnoreQueryFilters().AsNoTracking(), m => m.ProductId, p => p.Id,
                (m, p) => new { m.Id, ProductName = p.Name, m.QuantityBase, m.Reason, m.NeedsReview, m.OccurredAtUtc, m.UserId })
            .GroupJoin(_context.Users.IgnoreQueryFilters().AsNoTracking(), x => x.UserId, u => u.Id,
                (x, users) => new { x, users })
            .SelectMany(x => x.users.DefaultIfEmpty(), (x, u) => new ComplimentaryLogItemDto(
                x.x.Id,
                x.x.ProductName,
                x.x.QuantityBase,
                x.x.Reason,
                x.x.NeedsReview,
                x.x.OccurredAtUtc,
                u != null ? u.FullName : "(غير معروف)"))
            .ToListAsync(cancellationToken);

        return new PagedResult<ComplimentaryLogItemDto>(rows, totalCount, paging.PageNumber, paging.PageSize);
    }
}
