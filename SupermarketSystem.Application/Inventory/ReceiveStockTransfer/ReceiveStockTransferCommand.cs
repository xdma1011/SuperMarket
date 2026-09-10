using Microsoft.EntityFrameworkCore;
using SupermarketSystem.Application.Common.Interfaces;
using SupermarketSystem.Application.Common.Results;
using SupermarketSystem.Domain.Identity;
using SupermarketSystem.Domain.Inventory;

namespace SupermarketSystem.Application.Inventory.ReceiveStockTransfer;

public sealed record ReceiveStockTransferCommand(Guid StockTransferId);

public sealed record ReceiveStockTransferResponse(Guid StockTransferId, string TransferNumber);

/// <summary>
/// خطوة الاستلام - البضاعة وصلت فعليًا للفرع الوجهة، تُضاف لمخزونه الآن
/// فقط (لا وقت الإرسال). لمنتج متتبَّع دفعات: نلاقي دفعة موجودة أصلًا
/// بالفرع الوجهة بنفس رقم الدفعة (BatchNumber)، أو ننشئ وحدة جديدة لو
/// أول مرة توصل هذا الرقم لهالفرع - نفس نمط CompletePurchaseInvoiceHandler
/// بالضبط لإنشاء الدفعات.
/// </summary>
public sealed class ReceiveStockTransferHandler
{
    private readonly IApplicationDbContext _context;
    private readonly ICurrentUserContext _currentUser;
    private readonly IDateTimeProvider _dateTimeProvider;
    private readonly ICatalogVersionService _catalogVersionService;

    public ReceiveStockTransferHandler(
        IApplicationDbContext context,
        ICurrentUserContext currentUser,
        IDateTimeProvider dateTimeProvider,
        ICatalogVersionService catalogVersionService)
    {
        _context = context;
        _currentUser = currentUser;
        _dateTimeProvider = dateTimeProvider;
        _catalogVersionService = catalogVersionService;
    }

    public async Task<Result<ReceiveStockTransferResponse>> HandleAsync(
        ReceiveStockTransferCommand command, CancellationToken cancellationToken)
    {
        var transfer = await _context.StockTransfers
            .Include(t => t.Items)
            .FirstOrDefaultAsync(t => t.Id == command.StockTransferId, cancellationToken);

        if (transfer is null)
        {
            return Result.Failure<ReceiveStockTransferResponse>(
                Error.NotFound("StockTransfer.NotFound", $"عملية النقل '{command.StockTransferId}' غير موجودة."));
        }

        if (transfer.Status != StockTransferStatus.Dispatched)
        {
            return Result.Failure<ReceiveStockTransferResponse>(
                Error.Conflict("StockTransfer.NotDispatched", "عملية النقل هذه مُستلَمة أصلًا."));
        }

        var actorUserId = _currentUser.UserId ?? User.SystemUserId;
        var occurredAtUtc = _dateTimeProvider.UtcNow;
        var newStockMovements = new List<StockMovement>();
        var stockCache = new Dictionary<(Guid, Guid?), Domain.Inventory.Stock>();

        foreach (var item in transfer.Items)
        {
            Guid? destinationBatchId = null;

            if (item.BatchNumber is not null)
            {
                var existingBatch = await _context.ProductBatches.FirstOrDefaultAsync(
                    b => b.ProductId == item.ProductId && b.BranchId == transfer.DestinationBranchId && b.BatchNumber == item.BatchNumber,
                    cancellationToken);

                if (existingBatch is null)
                {
                    var newBatch = new ProductBatch(item.ProductId, transfer.DestinationBranchId, item.BatchNumber, item.BatchExpiryDate, unitCost: 0m);
                    _context.ProductBatches.Add(newBatch);
                    destinationBatchId = newBatch.Id;
                }
                else
                {
                    destinationBatchId = existingBatch.Id;
                }

                item.SetDestinationBatch(destinationBatchId.Value);
            }

            var stock = await GetOrCreateStockAsync(item.ProductId, transfer.DestinationBranchId, destinationBatchId, stockCache, cancellationToken);
            stock.Increase(item.QuantityBase);

            newStockMovements.Add(new StockMovement(
                item.ProductId, transfer.DestinationBranchId, item.ProductUnitId, destinationBatchId,
                item.QuantityBase, MovementType.TransferIn, reason: null, occurredAtUtc, actorUserId,
                StockMovementReferenceType.StockTransferItem, item.Id));
        }

        transfer.MarkReceived(actorUserId, occurredAtUtc);
        _context.StockMovements.AddRange(newStockMovements);

        await _context.SaveChangesAsync(cancellationToken);
        await _catalogVersionService.IncrementVersionAsync(cancellationToken);

        return Result.Success(new ReceiveStockTransferResponse(transfer.Id, transfer.TransferNumber));
    }

    private async Task<Domain.Inventory.Stock> GetOrCreateStockAsync(
        Guid productId, Guid branchId, Guid? productBatchId,
        Dictionary<(Guid, Guid?), Domain.Inventory.Stock> cache, CancellationToken cancellationToken)
    {
        var key = (productId, productBatchId);
        if (cache.TryGetValue(key, out var cached))
        {
            return cached;
        }

        var existing = await _context.Stocks.FirstOrDefaultAsync(
            s => s.ProductId == productId && s.BranchId == branchId && s.ProductBatchId == productBatchId, cancellationToken);

        var stock = existing ?? new Domain.Inventory.Stock(productId, branchId, productBatchId);
        if (existing is null)
        {
            _context.Stocks.Add(stock);
        }

        cache[key] = stock;
        return stock;
    }
}
