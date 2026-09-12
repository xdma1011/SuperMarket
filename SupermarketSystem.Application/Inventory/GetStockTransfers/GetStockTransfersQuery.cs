using Microsoft.EntityFrameworkCore;
using SupermarketSystem.Application.Common.Interfaces;
using SupermarketSystem.Application.Common.Pagination;
using SupermarketSystem.Domain.Inventory;

namespace SupermarketSystem.Application.Inventory.GetStockTransfers;

public sealed record GetStockTransfersQuery(PagedRequest Paging, Guid? BranchId);

public sealed record StockTransferListItemDto(
    Guid Id,
    string TransferNumber,
    Guid SourceBranchId,
    string SourceBranchName,
    Guid DestinationBranchId,
    string DestinationBranchName,
    int StatusCode,
    string StatusTitle,
    DateTime DispatchedAtUtc,
    DateTime? ReceivedAtUtc,
    int ItemCount);

/// <summary>كانت مفقودة بالكامل - راجع تعليق StockTransfer.cs بالـDomain.</summary>
public sealed class GetStockTransfersHandler
{
    private readonly IApplicationDbContext _context;

    public GetStockTransfersHandler(IApplicationDbContext context)
    {
        _context = context;
    }

    public async Task<PagedResult<StockTransferListItemDto>> HandleAsync(GetStockTransfersQuery query, CancellationToken cancellationToken)
    {
        var paging = query.Paging.Normalized();

        var transfers = _context.StockTransfers.AsNoTracking().AsQueryable();

        if (query.BranchId is not null)
        {
            transfers = transfers.Where(t => t.SourceBranchId == query.BranchId.Value || t.DestinationBranchId == query.BranchId.Value);
        }

        // كان معكوسًا (IsDescending=true بيرجّع تصاعدي والعكس) - انصلح هون
        // (راجع تقرير الاختبارات: GetCashClosingsQuery/GetCurrentStockQuery
        // نفس الملف عندهم النمط الصحيح، وهذا الملف بالذات كان الاستثناء).
        transfers = paging.IsDescending
            ? transfers.OrderByDescending(t => t.DispatchedAtUtc).ThenByDescending(t => t.Id)
            : transfers.OrderBy(t => t.DispatchedAtUtc).ThenBy(t => t.Id);

        var totalCount = await transfers.CountAsync(cancellationToken);

        var rows = await transfers
            .Skip(paging.Skip)
            .Take(paging.PageSize)
            .Select(t => new
            {
                t.Id,
                t.TransferNumber,
                t.SourceBranchId,
                t.DestinationBranchId,
                t.Status,
                t.DispatchedAtUtc,
                t.ReceivedAtUtc,
                ItemCount = t.Items.Count
            })
            .ToListAsync(cancellationToken);

        var branchIds = rows.Select(r => r.SourceBranchId).Concat(rows.Select(r => r.DestinationBranchId)).Distinct().ToList();
        var branchNames = await _context.Branches.AsNoTracking()
            .Where(b => branchIds.Contains(b.Id))
            .Select(b => new { b.Id, b.Name })
            .ToDictionaryAsync(b => b.Id, b => b.Name, cancellationToken);

        var items = rows
            .Select(r => new StockTransferListItemDto(
                r.Id,
                r.TransferNumber,
                r.SourceBranchId,
                branchNames.GetValueOrDefault(r.SourceBranchId, "(غير معروف)"),
                r.DestinationBranchId,
                branchNames.GetValueOrDefault(r.DestinationBranchId, "(غير معروف)"),
                (int)r.Status,
                StatusTitle(r.Status),
                r.DispatchedAtUtc,
                r.ReceivedAtUtc,
                r.ItemCount))
            .ToList();

        return new PagedResult<StockTransferListItemDto>(items, totalCount, paging.PageNumber, paging.PageSize);
    }

    // §3.1 CLAUDE.md - ممنوع .ToString() جوّا Select مترجَم لـSQL، الترجمة بعد التحميل للذاكرة.
    private static string StatusTitle(StockTransferStatus status) => status switch
    {
        StockTransferStatus.Dispatched => "بالطريق",
        StockTransferStatus.Received => "مُستلَمة",
        _ => "غير معروف"
    };
}
