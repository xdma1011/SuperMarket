using Microsoft.EntityFrameworkCore;
using SupermarketSystem.Application.Common.Interfaces;
using SupermarketSystem.Application.Common.Results;
using SupermarketSystem.Domain.Common;
using SupermarketSystem.Domain.Identity;
using SupermarketSystem.Domain.Inventory;

namespace SupermarketSystem.Application.Inventory.CreateStockTransfer;

public sealed record CreateStockTransferItemDto(
    Guid ProductId,
    Guid ProductUnitId,
    decimal Quantity,
    // مطلوب لمنتج متتبَّع دفعات فقط - الدفعة بالفرع المصدر اللي رح تُخصَم منها.
    Guid? SourceProductBatchId);

public sealed record CreateStockTransferCommand(
    Guid SourceBranchId,
    Guid DestinationBranchId,
    IReadOnlyList<CreateStockTransferItemDto> Items);

public sealed record CreateStockTransferResponse(Guid StockTransferId, string TransferNumber);

public static class CreateStockTransferValidator
{
    public static Error? Validate(CreateStockTransferCommand command)
    {
        if (command.SourceBranchId == Guid.Empty)
        {
            return Error.Validation("StockTransfer.SourceBranchRequired", "الفرع المصدر مطلوب.");
        }

        if (command.DestinationBranchId == Guid.Empty)
        {
            return Error.Validation("StockTransfer.DestinationBranchRequired", "الفرع الوجهة مطلوب.");
        }

        if (command.SourceBranchId == command.DestinationBranchId)
        {
            return Error.Validation("StockTransfer.SameBranch", "الفرع المصدر والوجهة لازم يكونوا مختلفين.");
        }

        if (command.Items.Count == 0)
        {
            return Error.Validation("StockTransfer.ItemsRequired", "صنف واحد على الأقل مطلوب.");
        }

        foreach (var item in command.Items)
        {
            if (item.ProductId == Guid.Empty || item.ProductUnitId == Guid.Empty)
            {
                return Error.Validation("StockTransfer.ItemProductRequired", "كل سطر يحتاج منتج ووحدة.");
            }

            if (item.Quantity <= 0)
            {
                return Error.Validation("StockTransfer.ItemQuantityInvalid", "كل سطر يحتاج كمية موجبة.");
            }
        }

        return null;
    }
}

/// <summary>
/// خطوة الإرسال - البضاعة تخرج فعليًا من الفرع المصدر فورًا (خصم مباشر
/// EF-tracked، لا الطريقة الذرية للبيع - هذا إجراء مدير نادر، لا ضغط POS
/// متزامن). الفرع الوجهة ما بيتأثر إطلاقًا لحد خطوة الاستلام المنفصلة
/// (ReceiveStockTransferCommand) - البضاعة "بالطريق" فترة انتقالية مقصودة.
/// </summary>
public sealed class CreateStockTransferHandler
{
    private readonly IApplicationDbContext _context;
    private readonly IDocumentNumberGenerator _documentNumberGenerator;
    private readonly ICurrentUserContext _currentUser;
    private readonly IDateTimeProvider _dateTimeProvider;
    private readonly ICatalogVersionService _catalogVersionService;

    public CreateStockTransferHandler(
        IApplicationDbContext context,
        IDocumentNumberGenerator documentNumberGenerator,
        ICurrentUserContext currentUser,
        IDateTimeProvider dateTimeProvider,
        ICatalogVersionService catalogVersionService)
    {
        _context = context;
        _documentNumberGenerator = documentNumberGenerator;
        _currentUser = currentUser;
        _dateTimeProvider = dateTimeProvider;
        _catalogVersionService = catalogVersionService;
    }

    public async Task<Result<CreateStockTransferResponse>> HandleAsync(
        CreateStockTransferCommand command, CancellationToken cancellationToken)
    {
        var validationError = CreateStockTransferValidator.Validate(command);
        if (validationError is not null)
        {
            return Result.Failure<CreateStockTransferResponse>(validationError);
        }

        var sourceExists = await _context.Branches.AsNoTracking().AnyAsync(b => b.Id == command.SourceBranchId, cancellationToken);
        if (!sourceExists)
        {
            return Result.Failure<CreateStockTransferResponse>(
                Error.NotFound("StockTransfer.SourceBranchNotFound", $"الفرع '{command.SourceBranchId}' غير موجود."));
        }

        var destinationExists = await _context.Branches.AsNoTracking().AnyAsync(b => b.Id == command.DestinationBranchId, cancellationToken);
        if (!destinationExists)
        {
            return Result.Failure<CreateStockTransferResponse>(
                Error.NotFound("StockTransfer.DestinationBranchNotFound", $"الفرع '{command.DestinationBranchId}' غير موجود."));
        }

        var productIds = command.Items.Select(i => i.ProductId).Distinct().ToList();
        var products = await _context.Products.AsNoTracking()
            .Where(p => productIds.Contains(p.Id))
            .Select(p => new { p.Id, p.IsBatchTracked })
            .ToDictionaryAsync(p => p.Id, cancellationToken);

        var missingProduct = productIds.FirstOrDefault(id => !products.ContainsKey(id));
        if (missingProduct != default)
        {
            return Result.Failure<CreateStockTransferResponse>(
                Error.NotFound("StockTransfer.ProductNotFound", $"المنتج '{missingProduct}' غير موجود."));
        }

        var unitIds = command.Items.Select(i => i.ProductUnitId).Distinct().ToList();
        var units = await _context.ProductUnits.AsNoTracking()
            .Where(u => unitIds.Contains(u.Id))
            .Select(u => new { u.Id, u.ProductId, u.ConversionFactorToBase })
            .ToDictionaryAsync(u => u.Id, cancellationToken);

        foreach (var item in command.Items)
        {
            if (!units.TryGetValue(item.ProductUnitId, out var unit) || unit.ProductId != item.ProductId)
            {
                return Result.Failure<CreateStockTransferResponse>(
                    Error.Validation("StockTransfer.UnitProductMismatch", $"الوحدة '{item.ProductUnitId}' لا تطابق منتجها."));
            }
        }

        var batchIds = command.Items.Where(i => i.SourceProductBatchId is not null)
            .Select(i => i.SourceProductBatchId!.Value).Distinct().ToList();
        var batches = batchIds.Count == 0
            ? new Dictionary<Guid, (Guid ProductId, Guid BranchId, string BatchNumber, DateOnly? ExpiryDate)>()
            : await _context.ProductBatches.AsNoTracking()
                .Where(b => batchIds.Contains(b.Id))
                .Select(b => new { b.Id, b.ProductId, b.BranchId, b.BatchNumber, b.ExpiryDate })
                .ToDictionaryAsync(b => b.Id, b => (b.ProductId, b.BranchId, b.BatchNumber, b.ExpiryDate), cancellationToken);

        foreach (var item in command.Items)
        {
            var product = products[item.ProductId];

            if (product.IsBatchTracked && item.SourceProductBatchId is null)
            {
                return Result.Failure<CreateStockTransferResponse>(
                    Error.Validation("StockTransfer.BatchRequired", $"المنتج '{item.ProductId}' يتتبّع دفعات - حدّد الدفعة المصدر."));
            }

            if (item.SourceProductBatchId is { } batchId)
            {
                if (!batches.TryGetValue(batchId, out var batch) || batch.ProductId != item.ProductId || batch.BranchId != command.SourceBranchId)
                {
                    return Result.Failure<CreateStockTransferResponse>(
                        Error.Validation("StockTransfer.BatchMismatch", $"الدفعة '{batchId}' لا تخص هذا المنتج بالفرع المصدر."));
                }
            }
        }

        var invoiceNumber = await _documentNumberGenerator.GetNextNumberAsync(
            command.SourceBranchId, DocumentType.StockTransfer, cancellationToken);

        var actorUserId = _currentUser.UserId ?? User.SystemUserId;
        var occurredAtUtc = _dateTimeProvider.UtcNow;

        var transfer = new StockTransfer(command.SourceBranchId, command.DestinationBranchId, invoiceNumber, actorUserId, occurredAtUtc);
        var newStockMovements = new List<StockMovement>();
        var stockCache = new Dictionary<(Guid, Guid?), Domain.Inventory.Stock>();

        foreach (var itemDto in command.Items)
        {
            var unit = units[itemDto.ProductUnitId];
            var quantityBase = itemDto.Quantity * unit.ConversionFactorToBase;

            string? batchNumber = null;
            DateOnly? batchExpiryDate = null;
            if (itemDto.SourceProductBatchId is { } sourceBatchId)
            {
                var batch = batches[sourceBatchId];
                batchNumber = batch.BatchNumber;
                batchExpiryDate = batch.ExpiryDate;
            }

            var item = transfer.AddItem(itemDto.ProductId, itemDto.ProductUnitId, quantityBase, itemDto.SourceProductBatchId, batchNumber, batchExpiryDate);

            var stock = await GetTrackedStockAsync(itemDto.ProductId, command.SourceBranchId, itemDto.SourceProductBatchId, stockCache, cancellationToken);
            if (stock is null || stock.QuantityOnHand < quantityBase)
            {
                return Result.Failure<CreateStockTransferResponse>(
                    Error.BusinessRule("StockTransfer.InsufficientStock", $"المخزون غير كافٍ للمنتج '{itemDto.ProductId}' بالفرع المصدر."));
            }

            stock.Decrease(quantityBase);

            newStockMovements.Add(new StockMovement(
                itemDto.ProductId, command.SourceBranchId, itemDto.ProductUnitId, itemDto.SourceProductBatchId,
                quantityBase, MovementType.TransferOut, reason: null, occurredAtUtc, actorUserId,
                StockMovementReferenceType.StockTransferItem, item.Id));
        }

        _context.StockTransfers.Add(transfer);
        _context.StockMovements.AddRange(newStockMovements);

        await _context.SaveChangesAsync(cancellationToken);
        await _catalogVersionService.IncrementVersionAsync(cancellationToken);

        return Result.Success(new CreateStockTransferResponse(transfer.Id, transfer.TransferNumber));
    }

    private async Task<Domain.Inventory.Stock?> GetTrackedStockAsync(
        Guid productId, Guid branchId, Guid? productBatchId,
        Dictionary<(Guid, Guid?), Domain.Inventory.Stock> cache, CancellationToken cancellationToken)
    {
        var key = (productId, productBatchId);
        if (cache.TryGetValue(key, out var cached))
        {
            return cached;
        }

        var stock = await _context.Stocks.FirstOrDefaultAsync(
            s => s.ProductId == productId && s.BranchId == branchId && s.ProductBatchId == productBatchId, cancellationToken);

        if (stock is not null)
        {
            cache[key] = stock;
        }

        return stock;
    }
}
