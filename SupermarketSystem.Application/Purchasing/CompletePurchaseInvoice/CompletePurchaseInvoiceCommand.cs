using Microsoft.EntityFrameworkCore;
using SupermarketSystem.Application.Common.Interfaces;
using SupermarketSystem.Application.Common.Policies;
using SupermarketSystem.Application.Common.Results;
using SupermarketSystem.Domain.Common;
using SupermarketSystem.Domain.Inventory;
using SupermarketSystem.Domain.Purchasing;

namespace SupermarketSystem.Application.Purchasing.CompletePurchaseInvoice;

public sealed record CompletePurchaseInvoiceItemDto(
    Guid ProductId,
    Guid ProductUnitId,
    decimal Quantity,
    decimal UnitCost,
    // Receive into an already-existing batch. Mutually exclusive with NewBatchNumber.
    Guid? ExistingProductBatchId,
    // Create a new batch as part of this receipt. Mutually exclusive with ExistingProductBatchId.
    string? NewBatchNumber,
    DateOnly? NewBatchExpiryDate);

public sealed record CompletePurchaseInvoiceCommand(
    Guid BranchId,
    Guid SupplierId,
    string? SupplierInvoiceReference,
    IReadOnlyList<CompletePurchaseInvoiceItemDto> Items,
    // مراجع صور محفوظة مسبقًا عبر POST /purchase-invoices/extract-from-image
    // (قراءة الفاتورة بالذكاء الاصطناعي ترفع الصورة وتحفظها كـWebP قبل ما
    // يصير عندنا فاتورة أصلًا — هذا الحقل يربط الصور المحفوظة تلك بالفاتورة
    // فعليًا وقت إتمامها). اختياري تمامًا — الإدخال اليدوي العادي بلا صور
    // يضل يشتغل بلا أي تغيير.
    IReadOnlyList<string>? ImageReferences = null);

public sealed record CompletePurchaseInvoiceResponse(Guid PurchaseInvoiceId, string InvoiceNumber, decimal TotalAmount);

public static class CompletePurchaseInvoiceValidator
{
    public static Error? Validate(CompletePurchaseInvoiceCommand command)
    {
        if (command.BranchId == Guid.Empty)
        {
            return Error.Validation("PurchaseInvoice.BranchRequired", "A branch is required.");
        }

        if (command.SupplierId == Guid.Empty)
        {
            return Error.Validation("PurchaseInvoice.SupplierRequired", "A supplier is required.");
        }

        if (command.Items.Count == 0)
        {
            return Error.Validation("PurchaseInvoice.ItemsRequired", "At least one item is required.");
        }

        foreach (var item in command.Items)
        {
            if (item.ProductId == Guid.Empty || item.ProductUnitId == Guid.Empty)
            {
                return Error.Validation("PurchaseInvoice.ItemProductRequired", "Every item requires a product and unit.");
            }

            if (item.Quantity <= 0)
            {
                return Error.Validation("PurchaseInvoice.ItemQuantityInvalid", "Every item's quantity must be positive.");
            }

            if (item.UnitCost < 0)
            {
                return Error.Validation("PurchaseInvoice.ItemUnitCostInvalid", "Every item's unit cost cannot be negative.");
            }

            if (item.ExistingProductBatchId is not null && !string.IsNullOrWhiteSpace(item.NewBatchNumber))
            {
                return Error.Validation(
                    "PurchaseInvoice.AmbiguousBatch",
                    "An item cannot both reference an existing batch and specify a new batch number.");
            }
        }

        return null;
    }
}

/// <summary>
/// The first atomic, inventory-affecting transaction in the system:
/// reserves the invoice number, records the purchase, and — in the SAME
/// SaveChangesAsync call — writes one StockMovement (PurchaseIn) and
/// increases Stock per item. Architecture Review §11/§12: PurchaseInvoice +
/// Items + StockMovement + Stock increment must commit together or not at
/// all; a single SaveChangesAsync is exactly that (EF Core wraps one call in
/// one implicit transaction covering every tracked change).
///
/// Direction matters: this is an INCREASE, not a decrement, so the normal
/// EF-tracked Stock.Increase() method is used rather than the raw atomic
/// conditional UPDATE the sale-completion path requires (Stock.cs remarks) —
/// there is no "oversell" race to guard against when adding stock, only the
/// standard RowVersion optimistic-concurrency check EF already performs.
///
/// Invoice number reservation is NOT wrapped in the same transaction as the
/// rest of this method, deliberately: DocumentNumberGenerator commits its
/// reservation immediately and independently. If everything after it fails,
/// the reserved number is burned — a gap, never reused — which is the
/// explicitly accepted behaviour from Architecture Review §4, identical to
/// how a SQL Server IDENTITY/SEQUENCE behaves on a rolled-back transaction.
/// </summary>
public sealed class CompletePurchaseInvoiceHandler
{
    private readonly IApplicationDbContext _context;
    private readonly IDocumentNumberGenerator _documentNumberGenerator;
    private readonly ICurrentUserContext _currentUser;
    private readonly IDateTimeProvider _dateTimeProvider;
    private readonly ISettingsProvider _settingsProvider;

    public CompletePurchaseInvoiceHandler(
        IApplicationDbContext context,
        IDocumentNumberGenerator documentNumberGenerator,
        ICurrentUserContext currentUser,
        IDateTimeProvider dateTimeProvider,
        ISettingsProvider settingsProvider)
    {
        _context = context;
        _documentNumberGenerator = documentNumberGenerator;
        _currentUser = currentUser;
        _dateTimeProvider = dateTimeProvider;
        _settingsProvider = settingsProvider;
    }

    public async Task<Result<CompletePurchaseInvoiceResponse>> HandleAsync(
        CompletePurchaseInvoiceCommand command,
        CancellationToken cancellationToken)
    {
        var validationError = CompletePurchaseInvoiceValidator.Validate(command);
        if (validationError is not null)
        {
            return Result.Failure<CompletePurchaseInvoiceResponse>(validationError);
        }

        var branchExists = await _context.Branches.AsNoTracking().AnyAsync(b => b.Id == command.BranchId, cancellationToken);
        if (!branchExists)
        {
            return Result.Failure<CompletePurchaseInvoiceResponse>(
                Error.NotFound("PurchaseInvoice.BranchNotFound", $"Branch '{command.BranchId}' was not found."));
        }

        var supplierExists = await _context.Suppliers.AsNoTracking().AnyAsync(s => s.Id == command.SupplierId, cancellationToken);
        if (!supplierExists)
        {
            return Result.Failure<CompletePurchaseInvoiceResponse>(
                Error.NotFound("PurchaseInvoice.SupplierNotFound", $"Supplier '{command.SupplierId}' was not found."));
        }

        // --- Resolve and validate every product/unit/batch referenced, up front ---

        var productIds = command.Items.Select(i => i.ProductId).Distinct().ToList();
        var products = await _context.Products
            .AsNoTracking()
            .Where(p => productIds.Contains(p.Id))
            .Select(p => new { p.Id, p.IsBatchTracked })
            .ToDictionaryAsync(p => p.Id, cancellationToken);

        var missingProduct = productIds.FirstOrDefault(id => !products.ContainsKey(id));
        if (missingProduct != default)
        {
            return Result.Failure<CompletePurchaseInvoiceResponse>(
                Error.NotFound("PurchaseInvoice.ProductNotFound", $"Product '{missingProduct}' was not found."));
        }

        var unitIds = command.Items.Select(i => i.ProductUnitId).Distinct().ToList();
        var units = await _context.ProductUnits
            .AsNoTracking()
            .Where(u => unitIds.Contains(u.Id))
            .Select(u => new { u.Id, u.ProductId, u.ConversionFactorToBase })
            .ToDictionaryAsync(u => u.Id, cancellationToken);

        foreach (var item in command.Items)
        {
            if (!units.TryGetValue(item.ProductUnitId, out var unit))
            {
                return Result.Failure<CompletePurchaseInvoiceResponse>(
                    Error.NotFound("PurchaseInvoice.UnitNotFound", $"Unit '{item.ProductUnitId}' was not found."));
            }

            if (unit.ProductId != item.ProductId)
            {
                return Result.Failure<CompletePurchaseInvoiceResponse>(
                    Error.Validation(
                        "PurchaseInvoice.UnitProductMismatch",
                        $"Unit '{item.ProductUnitId}' does not belong to product '{item.ProductId}'."));
            }
        }

        var existingBatchIds = command.Items
            .Where(i => i.ExistingProductBatchId is not null)
            .Select(i => i.ExistingProductBatchId!.Value)
            .Distinct()
            .ToList();

        var existingBatches = existingBatchIds.Count == 0
            ? new Dictionary<Guid, (Guid ProductId, Guid BranchId)>()
            : await _context.ProductBatches
                .AsNoTracking()
                .Where(b => existingBatchIds.Contains(b.Id))
                .Select(b => new { b.Id, b.ProductId, b.BranchId })
                .ToDictionaryAsync(b => b.Id, b => (b.ProductId, b.BranchId), cancellationToken);

        foreach (var item in command.Items)
        {
            var product = products[item.ProductId];

            if (item.ExistingProductBatchId is { } existingBatchId)
            {
                if (!existingBatches.TryGetValue(existingBatchId, out var batch))
                {
                    return Result.Failure<CompletePurchaseInvoiceResponse>(
                        Error.NotFound("PurchaseInvoice.BatchNotFound", $"Batch '{existingBatchId}' was not found."));
                }

                if (batch.ProductId != item.ProductId || batch.BranchId != command.BranchId)
                {
                    return Result.Failure<CompletePurchaseInvoiceResponse>(
                        Error.Validation(
                            "PurchaseInvoice.BatchMismatch",
                            $"Batch '{existingBatchId}' does not belong to product '{item.ProductId}' at branch '{command.BranchId}'."));
                }
            }
            else if (product.IsBatchTracked && string.IsNullOrWhiteSpace(item.NewBatchNumber))
            {
                return Result.Failure<CompletePurchaseInvoiceResponse>(
                    Error.Validation(
                        "PurchaseInvoice.BatchRequired",
                        $"Product '{item.ProductId}' is batch-tracked; supply ExistingProductBatchId or NewBatchNumber."));
            }
            else if (!product.IsBatchTracked && (item.ExistingProductBatchId is not null || !string.IsNullOrWhiteSpace(item.NewBatchNumber)))
            {
                return Result.Failure<CompletePurchaseInvoiceResponse>(
                    Error.Validation(
                        "PurchaseInvoice.BatchNotApplicable",
                        $"Product '{item.ProductId}' is not batch-tracked; no batch may be supplied."));
            }
        }

        // --- "سماح مع مراجعة": سعر أعلى بنسبة ملحوظة عن متوسط آخر 5 عمليات
        // شراء لنفس المنتج يُعلَّم للمراجعة، بلا ما يوقف الفاتورة إطلاقًا
        // (CLAUDE.md §1.6). صنف جديد بلا تاريخ شراء = صفر مقارنة ممكنة،
        // فما في تعليم بالتأكيد (مش "نسمح بحذر"، فعليًا ولا معنى للمقارنة).

        var priceThresholdPercent = await _settingsProvider.GetDecimalAsync(
            PurchasingPolicyKeys.PriceIncreaseWarningThresholdPercent, 15m, cancellationToken);

        var averageRecentCostByProduct = new Dictionary<Guid, decimal>();
        foreach (var productId in productIds)
        {
            var recentCosts = await (
                from item in _context.PurchaseInvoiceItems.AsNoTracking()
                join invoice in _context.PurchaseInvoices.AsNoTracking() on item.PurchaseInvoiceId equals invoice.Id
                where item.ProductId == productId
                orderby invoice.CreatedAtUtc descending
                select item.UnitCost)
                .Take(5)
                .ToListAsync(cancellationToken);

            if (recentCosts.Count > 0)
            {
                averageRecentCostByProduct[productId] = recentCosts.Average();
            }
        }

        // --- Reserve the invoice number (own independent commit — see class remarks) ---

        var invoiceNumber = await _documentNumberGenerator.GetNextNumberAsync(
            command.BranchId, DocumentType.PurchaseInvoice, cancellationToken);

        // --- Build the aggregate + its inventory effects, all in one graph ---

        var purchaseInvoice = new Domain.Purchasing.PurchaseInvoice(
            command.BranchId, command.SupplierId, invoiceNumber, command.SupplierInvoiceReference);

        var actorUserId = _currentUser.UserId ?? Domain.Identity.User.SystemUserId;
        var occurredAtUtc = _dateTimeProvider.UtcNow;
        var stockCache = new Dictionary<(Guid ProductId, Guid? ProductBatchId), Stock>();
        var newStockMovements = new List<StockMovement>();

        foreach (var itemDto in command.Items)
        {
            var unit = units[itemDto.ProductUnitId];

            Guid? productBatchId = itemDto.ExistingProductBatchId;
            if (productBatchId is null && !string.IsNullOrWhiteSpace(itemDto.NewBatchNumber))
            {
                var newBatch = new ProductBatch(
                    itemDto.ProductId, command.BranchId, itemDto.NewBatchNumber, itemDto.NewBatchExpiryDate, itemDto.UnitCost);
                _context.ProductBatches.Add(newBatch);
                productBatchId = newBatch.Id;
            }

            var needsReview = averageRecentCostByProduct.TryGetValue(itemDto.ProductId, out var averageRecentCost)
                && itemDto.UnitCost > averageRecentCost * (1 + priceThresholdPercent / 100m);

            var invoiceItem = purchaseInvoice.AddItem(
                itemDto.ProductId, itemDto.ProductUnitId, productBatchId, itemDto.Quantity, itemDto.UnitCost, needsReview);

            // Normalized to the product's base unit (Architecture Review
            // §12) so StockMovement/Stock stay consistent regardless of
            // which unit this line was purchased in.
            var quantityBase = itemDto.Quantity * unit.ConversionFactorToBase;

            var stock = await GetOrCreateStockAsync(itemDto.ProductId, command.BranchId, productBatchId, stockCache, cancellationToken);
            stock.Increase(quantityBase);

            newStockMovements.Add(new StockMovement(
                itemDto.ProductId,
                command.BranchId,
                itemDto.ProductUnitId,
                productBatchId,
                quantityBase,
                MovementType.PurchaseIn,
                reason: null,
                occurredAtUtc,
                actorUserId,
                StockMovementReferenceType.PurchaseInvoiceItem,
                invoiceItem.Id));
        }

        // ربط أي صور محفوظة مسبقًا (من مسار قراءة الفاتورة بالذكاء
        // الاصطناعي) — AddImage موجودة أصلًا بالـDomain من Phase C، بلا
        // أي تعديل عليها؛ هذا استخدام إضافي لها، لا ميزة جديدة بالـDomain.
        foreach (var imageReference in command.ImageReferences ?? Array.Empty<string>())
        {
            purchaseInvoice.AddImage(imageReference);
        }

        purchaseInvoice.MarkReceived();

        _context.PurchaseInvoices.Add(purchaseInvoice);
        _context.StockMovements.AddRange(newStockMovements);

        await _context.SaveChangesAsync(cancellationToken);

        return Result.Success(new CompletePurchaseInvoiceResponse(purchaseInvoice.Id, purchaseInvoice.InvoiceNumber, purchaseInvoice.TotalAmount));
    }

    /// <summary>
    /// Local cache first, then the database — two items in the same invoice
    /// for the same (product, batch) must accumulate onto the same Stock
    /// instance rather than each independently deciding "no row exists yet"
    /// and both trying to insert one, which would violate the unique index.
    /// </summary>
    private async Task<Stock> GetOrCreateStockAsync(
        Guid productId,
        Guid branchId,
        Guid? productBatchId,
        Dictionary<(Guid, Guid?), Stock> cache,
        CancellationToken cancellationToken)
    {
        var key = (productId, productBatchId);
        if (cache.TryGetValue(key, out var cached))
        {
            return cached;
        }

        // Tracked query, deliberately no AsNoTracking — this entity is
        // mutated and must be part of the change tracker for SaveChangesAsync
        // to persist the increase.
        var existing = await _context.Stocks.FirstOrDefaultAsync(
            s => s.ProductId == productId && s.BranchId == branchId && s.ProductBatchId == productBatchId,
            cancellationToken);

        var stock = existing ?? new Stock(productId, branchId, productBatchId);

        if (existing is null)
        {
            _context.Stocks.Add(stock);
        }

        cache[key] = stock;
        return stock;
    }
}
