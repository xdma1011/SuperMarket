using Microsoft.EntityFrameworkCore;
using SupermarketSystem.Application.Common.Interfaces;
using SupermarketSystem.Application.Common.Notifications;
using SupermarketSystem.Application.Common.Results;
using SupermarketSystem.Application.Sales.Common;
using SupermarketSystem.Application.Sales.CompleteSale;
using SupermarketSystem.Application.Sales.PreparedOrders;
using SupermarketSystem.Domain.Notifications;

namespace SupermarketSystem.Application.Sales.AtCostWithdrawal;

// =====================================================================================
// سحب بضاعة لصاحب المحل/شريك بسعر التكلفة (28/9/2026، قرار صاحب المشروع): بيضرب اللي أخده من
// تلفونه، بيشوف التكلفة، وبيختار: "بدفع حقها" (المبلغ بيدخل الصندوق وبينحسب بالتقفيل) أو "اخصمها
// مني" (بلا دفع - المبلغ بيضل مفتوح على الفاتورة كدين عليه، لحد ما تنبني وحدة الشركاء وينخصم من
// نصيبه). منتج بلا تكلفة معروفة (بلا فاتورة شراء مستلمة) مرفوض. كل سحب بيوصل كتنبيه "مهم".
// =====================================================================================

public sealed record QuoteAtCostWithdrawalQuery(Guid BranchId, IReadOnlyList<PreparedOrderItemRequest> Items);

public sealed record AtCostQuoteLineDto(
    Guid ProductId, Guid ProductUnitId, Guid? ProductBatchId, string ProductName, string UnitName,
    decimal Quantity, decimal? UnitCost, decimal? LineTotal, decimal SellingUnitPrice);

/// <summary>MissingCost = أسماء المنتجات اللي ما إلها تكلفة معروفة - وجود أي وحدة بيمنع التأكيد.</summary>
public sealed record AtCostQuoteDto(
    IReadOnlyList<AtCostQuoteLineDto> Lines, decimal Total, decimal SellingValue, IReadOnlyList<string> MissingCost);

public sealed record AtCostItemRequest(Guid ProductId, Guid ProductUnitId, decimal Quantity, Guid? ProductBatchId = null);

public sealed record CompleteAtCostWithdrawalCommand(
    Guid BranchId,
    Guid ClientRequestId,
    IReadOnlyList<AtCostItemRequest> Items,
    bool DeductFromShare,
    // مطلوب لـ"بدفع حقها" بس (طريقة دفع بلا رقم مرجعي - كاش عادةً).
    Guid? PaymentMethodId);

public sealed record CompleteAtCostWithdrawalResponse(
    Guid SaleInvoiceId, string InvoiceNumber, decimal TotalAmount, decimal TotalPaidAmount, bool DeductFromShare, bool WasReplay);

/// <summary>
/// حساب التكلفة بدون تسجيل شي - نفس الحساب اللي بيعمله البيع بالضبط (SaleUnitCosts). منتج بدفعات
/// بياخد أقرب دفعة بتنتهي فيها رصيد بالفرع (FEFO)، والصفحة بتبعتها مع التأكيد.
/// </summary>
public sealed class QuoteAtCostWithdrawalHandler
{
    private readonly IApplicationDbContext _context;
    private readonly IDateTimeProvider _dateTimeProvider;

    public QuoteAtCostWithdrawalHandler(IApplicationDbContext context, IDateTimeProvider dateTimeProvider)
    {
        _context = context;
        _dateTimeProvider = dateTimeProvider;
    }

    public async Task<Result<AtCostQuoteDto>> HandleAsync(QuoteAtCostWithdrawalQuery query, CancellationToken cancellationToken) =>
        await QuoteAsync(query.BranchId, query.Items.Select(i => new AtCostItemRequest(i.ProductId, i.ProductUnitId, i.Quantity)).ToList(), cancellationToken);

    internal async Task<Result<AtCostQuoteDto>> QuoteAsync(Guid branchId, IReadOnlyList<AtCostItemRequest> items, CancellationToken cancellationToken)
    {
        if (branchId == Guid.Empty)
        {
            return Result.Failure<AtCostQuoteDto>(Error.Validation("AtCost.BranchRequired", "الفرع مطلوب."));
        }

        if (items.Count == 0)
        {
            return Result.Failure<AtCostQuoteDto>(Error.Validation("AtCost.ItemsRequired", "اضرب صنف واحد على الأقل."));
        }

        if (items.Any(i => i.ProductId == Guid.Empty || i.ProductUnitId == Guid.Empty || i.Quantity <= 0))
        {
            return Result.Failure<AtCostQuoteDto>(
                Error.Validation("AtCost.ItemInvalid", "كل سطر لازم يكون إله منتج ووحدة وكمية أكبر من صفر."));
        }

        var productIds = items.Select(i => i.ProductId).Distinct().ToList();
        var unitIds = items.Select(i => i.ProductUnitId).Distinct().ToList();

        var products = await _context.Products.AsNoTracking()
            .Where(p => productIds.Contains(p.Id))
            .Select(p => new { p.Id, p.Name, p.IsBatchTracked })
            .ToDictionaryAsync(p => p.Id, cancellationToken);
        var units = await _context.ProductUnits.AsNoTracking()
            .Where(u => unitIds.Contains(u.Id))
            .Select(u => new { u.Id, u.ProductId, u.UnitName, u.ConversionFactorToBase })
            .ToDictionaryAsync(u => u.Id, cancellationToken);
        var prices = await _context.ProductBranches.AsNoTracking()
            .Where(pb => pb.BranchId == branchId && productIds.Contains(pb.ProductId))
            .ToDictionaryAsync(pb => pb.ProductId, pb => pb.SellingPrice, cancellationToken);

        // FEFO لمنتجات الدفعات اللي ما إجت دفعتها محدّدة: أقرب انتهاء، فيها رصيد بالفرع.
        var batchTrackedWithoutBatch = items
            .Where(i => i.ProductBatchId is null && products.TryGetValue(i.ProductId, out var p) && p.IsBatchTracked)
            .Select(i => i.ProductId).Distinct().ToList();
        var fefoBatches = new Dictionary<Guid, Guid>();
        if (batchTrackedWithoutBatch.Count > 0)
        {
            var candidates = await _context.ProductBatches.AsNoTracking()
                .Where(b => b.BranchId == branchId && batchTrackedWithoutBatch.Contains(b.ProductId))
                .Join(_context.Stocks.AsNoTracking().Where(s => s.BranchId == branchId && s.QuantityOnHand > 0),
                    b => b.Id, s => s.ProductBatchId, (b, s) => new { b.Id, b.ProductId, b.ExpiryDate, b.CreatedAtUtc })
                .ToListAsync(cancellationToken);
            foreach (var group in candidates.GroupBy(c => c.ProductId))
            {
                var first = group.OrderBy(c => c.ExpiryDate is null).ThenBy(c => c.ExpiryDate).ThenBy(c => c.CreatedAtUtc).First();
                fefoBatches[group.Key] = first.Id;
            }
        }

        var resolved = new List<(AtCostItemRequest Item, Guid? BatchId)>();
        foreach (var item in items)
        {
            if (!products.TryGetValue(item.ProductId, out var product)
                || !units.TryGetValue(item.ProductUnitId, out var unit) || unit.ProductId != item.ProductId)
            {
                return Result.Failure<AtCostQuoteDto>(Error.NotFound("AtCost.ProductNotFound", "في صنف مش موجود (أو وحدته غلط)."));
            }

            if (!prices.ContainsKey(item.ProductId))
            {
                return Result.Failure<AtCostQuoteDto>(
                    Error.BusinessRule("AtCost.ProductNotAtBranch", $"'{product.Name}' مش مربوط بهالفرع."));
            }

            Guid? batchId = item.ProductBatchId;
            if (product.IsBatchTracked && batchId is null)
            {
                if (!fefoBatches.TryGetValue(item.ProductId, out var fefo))
                {
                    return Result.Failure<AtCostQuoteDto>(
                        Error.BusinessRule("AtCost.NoBatchInStock", $"'{product.Name}' ما إله دفعة فيها رصيد بهالفرع."));
                }

                batchId = fefo;
            }
            else if (!product.IsBatchTracked)
            {
                batchId = null;
            }

            resolved.Add((item, batchId));
        }

        var (batchCosts, averageCosts) = await SaleUnitCosts.LoadAsync(
            _context, resolved.Select(r => (r.Item.ProductId, r.BatchId)).ToList(), _dateTimeProvider.UtcNow, cancellationToken);

        var lines = new List<AtCostQuoteLineDto>();
        var missing = new List<string>();
        foreach (var (item, batchId) in resolved)
        {
            var product = products[item.ProductId];
            var unit = units[item.ProductUnitId];
            decimal? baseCost = batchId is { } b
                ? (batchCosts.TryGetValue(b, out var bc) ? bc : null)
                : (averageCosts.TryGetValue(item.ProductId, out var ac) ? ac : null);

            decimal? unitCost = baseCost is { } c ? SaleUnitCosts.AtCostUnitPrice(c, unit.ConversionFactorToBase) : null;
            if (unitCost is null && !missing.Contains(product.Name))
            {
                missing.Add(product.Name);
            }

            lines.Add(new AtCostQuoteLineDto(
                item.ProductId, item.ProductUnitId, batchId, product.Name, unit.UnitName, item.Quantity,
                unitCost, unitCost * item.Quantity, prices[item.ProductId] * unit.ConversionFactorToBase));
        }

        return Result.Success(new AtCostQuoteDto(
            lines,
            lines.Sum(l => l.LineTotal ?? 0m),
            lines.Sum(l => l.SellingUnitPrice * l.Quantity),
            missing));
    }
}

public sealed class CompleteAtCostWithdrawalHandler
{
    private readonly IApplicationDbContext _context;
    private readonly QuoteAtCostWithdrawalHandler _quoteHandler;
    private readonly CompleteSaleHandler _saleHandler;
    private readonly ICurrentUserContext _currentUser;
    private readonly INotificationDispatcher _notificationDispatcher;

    public CompleteAtCostWithdrawalHandler(
        IApplicationDbContext context,
        QuoteAtCostWithdrawalHandler quoteHandler,
        CompleteSaleHandler saleHandler,
        ICurrentUserContext currentUser,
        INotificationDispatcher notificationDispatcher)
    {
        _context = context;
        _quoteHandler = quoteHandler;
        _saleHandler = saleHandler;
        _currentUser = currentUser;
        _notificationDispatcher = notificationDispatcher;
    }

    public async Task<Result<CompleteAtCostWithdrawalResponse>> HandleAsync(
        CompleteAtCostWithdrawalCommand command, CancellationToken cancellationToken)
    {
        if (command.ClientRequestId == Guid.Empty)
        {
            return Result.Failure<CompleteAtCostWithdrawalResponse>(
                Error.Validation("AtCost.ClientRequestIdRequired", "A client request id is required."));
        }

        if (!command.DeductFromShare && command.PaymentMethodId is null)
        {
            return Result.Failure<CompleteAtCostWithdrawalResponse>(
                Error.Validation("AtCost.PaymentMethodRequired", "اختار طريقة الدفع، أو \"اخصمها مني\"."));
        }

        var quoteResult = await _quoteHandler.QuoteAsync(command.BranchId, command.Items, cancellationToken);
        if (quoteResult.IsFailure)
        {
            return Result.Failure<CompleteAtCostWithdrawalResponse>(quoteResult.Error!);
        }

        var quote = quoteResult.Value;
        if (quote.MissingCost.Count > 0)
        {
            return Result.Failure<CompleteAtCostWithdrawalResponse>(Error.BusinessRule(
                "Sale.AtCostNoCostHistory",
                $"ما إلهم تكلفة معروفة (ما في فاتورة شراء مستلمة): {string.Join("، ", quote.MissingCost)} - شيلهم وكمّل."));
        }

        var saleCommand = new CompleteSaleCommand(
            command.BranchId,
            command.ClientRequestId,
            CustomerId: null,
            InvoiceLevelDiscountAmount: 0m,
            quote.Lines.Select(l => new CompleteSaleItemDto(l.ProductId, l.ProductUnitId, l.Quantity, 0m, l.ProductBatchId)).ToList(),
            command.DeductFromShare
                ? Array.Empty<CompleteSalePaymentDto>()
                : new[] { new CompleteSalePaymentDto(command.PaymentMethodId!.Value, quote.Total, null, Guid.NewGuid()) });

        var saleResult = await _saleHandler.HandleAtCostWithdrawalAsync(saleCommand, command.DeductFromShare, cancellationToken);
        if (saleResult.IsFailure)
        {
            return Result.Failure<CompleteAtCostWithdrawalResponse>(saleResult.Error!);
        }

        var sale = saleResult.Value;
        if (!sale.WasReplay)
        {
            var who = await AlertText.UserNameAsync(_context, _currentUser.UserId, cancellationToken);
            var branch = await AlertText.BranchNameAsync(_context, command.BranchId, cancellationToken);
            var items = string.Join("\n", quote.Lines.Select(l => $"- {l.ProductName} × {l.Quantity:0.###} = {l.LineTotal:0.000}"));
            await _notificationDispatcher.NotifyAsync(
                $"سحب بسعر التكلفة — {who}",
                $"{who} أخد بضاعة من فرع {branch} بسعر التكلفة (فاتورة {sale.InvoiceNumber}):\n{items}\n" +
                $"المجموع بالتكلفة {sale.TotalAmount:0.000} د.أ (بسعر البيع {quote.SellingValue:0.000}).\n" +
                (command.DeductFromShare ? "طريقة التسوية: تنخصم من نصيبه (مسجّلة عليه)." : "طريقة التسوية: دفع حقها."),
                cancellationToken,
                NotificationSeverity.Warning);
        }

        return Result.Success(new CompleteAtCostWithdrawalResponse(
            sale.SaleInvoiceId, sale.InvoiceNumber, sale.TotalAmount, sale.TotalPaidAmount, command.DeductFromShare, sale.WasReplay));
    }
}
