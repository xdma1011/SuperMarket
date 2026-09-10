using Microsoft.EntityFrameworkCore;
using SupermarketSystem.Application.Common.Interfaces;
using SupermarketSystem.Application.Common.Results;
using SupermarketSystem.Domain.Inventory;

namespace SupermarketSystem.Application.Inventory.GetStockTransferDetail;

public sealed record GetStockTransferDetailQuery(Guid StockTransferId);

public sealed record StockTransferDetailItemDto(
    Guid ProductId, string ProductName, string UnitName, decimal QuantityBase, string? BatchNumber);

public sealed record StockTransferDetailDto(
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
    IReadOnlyList<StockTransferDetailItemDto> Items);

/// <summary>يعرض تفاصيل عملية النقل قبل تأكيد الاستلام - المدير يشوف بالضبط شو رح يستلم قبل ما يضغط.</summary>
public sealed class GetStockTransferDetailHandler
{
    private readonly IApplicationDbContext _context;

    public GetStockTransferDetailHandler(IApplicationDbContext context)
    {
        _context = context;
    }

    public async Task<Result<StockTransferDetailDto>> HandleAsync(GetStockTransferDetailQuery query, CancellationToken cancellationToken)
    {
        var transfer = await _context.StockTransfers.AsNoTracking()
            .FirstOrDefaultAsync(t => t.Id == query.StockTransferId, cancellationToken);

        if (transfer is null)
        {
            return Result.Failure<StockTransferDetailDto>(
                Error.NotFound("StockTransfer.NotFound", $"عملية النقل '{query.StockTransferId}' غير موجودة."));
        }

        var itemRows = await _context.StockTransferItems.AsNoTracking()
            .Where(i => i.StockTransferId == query.StockTransferId)
            .Join(_context.Products.AsNoTracking(), i => i.ProductId, p => p.Id, (i, p) => new { i, ProductName = p.Name })
            .Join(_context.ProductUnits.AsNoTracking(), x => x.i.ProductUnitId, u => u.Id, (x, u) => new
            {
                x.i.ProductId,
                x.ProductName,
                UnitName = u.UnitName,
                x.i.QuantityBase,
                x.i.BatchNumber
            })
            .ToListAsync(cancellationToken);

        var branchNames = await _context.Branches.AsNoTracking()
            .Where(b => b.Id == transfer.SourceBranchId || b.Id == transfer.DestinationBranchId)
            .Select(b => new { b.Id, b.Name })
            .ToDictionaryAsync(b => b.Id, b => b.Name, cancellationToken);

        var items = itemRows
            .Select(r => new StockTransferDetailItemDto(r.ProductId, r.ProductName, r.UnitName, r.QuantityBase, r.BatchNumber))
            .ToList();

        return Result.Success(new StockTransferDetailDto(
            transfer.Id,
            transfer.TransferNumber,
            transfer.SourceBranchId,
            branchNames.GetValueOrDefault(transfer.SourceBranchId, "(غير معروف)"),
            transfer.DestinationBranchId,
            branchNames.GetValueOrDefault(transfer.DestinationBranchId, "(غير معروف)"),
            (int)transfer.Status,
            transfer.Status == StockTransferStatus.Dispatched ? "بالطريق" : "مُستلَمة",
            transfer.DispatchedAtUtc,
            transfer.ReceivedAtUtc,
            items));
    }
}
