using Microsoft.EntityFrameworkCore;
using SupermarketSystem.Application.Common.Interfaces;
using SupermarketSystem.Application.Common.Pagination;
using SupermarketSystem.Domain.Inventory;

namespace SupermarketSystem.Application.Reporting.GetWasteLog;

public sealed record GetWasteLogQuery(PagedRequest Paging, Guid? BranchId);

public sealed record WasteLogItemDto(
    Guid StockMovementId,
    string ProductName,
    decimal QuantityBase,
    WasteReason Reason,
    string? Notes,
    bool NeedsReview,
    DateTime OccurredAtUtc);

/// <summary>
/// سجل حركات التلف/الهلاك (MovementType.WasteOut) - منفصل كليًا عن سجل
/// الضيافة (لا استعلام مشترك، رغم الآلية المتشابهة - راجع تعليق
/// RecordWasteIssueHandler). Reason يُرجَع خامًا (int) - نفس نمط
/// RecentReturnItemDto.Reason بالضبط، الفرونت إند يترجمه عبر enumMap محلي
/// (report-configs.ts)، لا ترجمة نص بالباك إند هون.
/// </summary>
public sealed class GetWasteLogHandler
{
    private readonly IApplicationDbContext _context;

    public GetWasteLogHandler(IApplicationDbContext context)
    {
        _context = context;
    }

    public async Task<PagedResult<WasteLogItemDto>> HandleAsync(
        GetWasteLogQuery query, CancellationToken cancellationToken)
    {
        var paging = query.Paging.Normalized();

        var movements = _context.StockMovements.AsNoTracking()
            .Where(m => m.MovementType == MovementType.WasteOut);

        if (query.BranchId is { } branchId)
        {
            movements = movements.Where(m => m.BranchId == branchId);
        }

        var totalCount = await movements.CountAsync(cancellationToken);

        var rows = await movements
            .OrderByDescending(m => m.OccurredAtUtc)
            .Skip(paging.Skip)
            .Take(paging.PageSize)
            .Join(_context.Products.AsNoTracking(), m => m.ProductId, p => p.Id,
                (m, p) => new
                {
                    m.Id,
                    ProductName = p.Name,
                    m.QuantityBase,
                    m.WasteReason,
                    m.Reason,
                    m.NeedsReview,
                    m.OccurredAtUtc
                })
            .ToListAsync(cancellationToken);

        var items = rows.Select(x => new WasteLogItemDto(
            x.Id,
            x.ProductName,
            x.QuantityBase,
            // WasteReason غير null دائمًا هون (StockMovement.ctor يرفض
            // WasteOut بلا WasteReason - راجع الـDomain) - .Value آمن.
            x.WasteReason!.Value,
            x.Reason,
            x.NeedsReview,
            x.OccurredAtUtc)).ToList();

        return new PagedResult<WasteLogItemDto>(items, totalCount, paging.PageNumber, paging.PageSize);
    }
}
