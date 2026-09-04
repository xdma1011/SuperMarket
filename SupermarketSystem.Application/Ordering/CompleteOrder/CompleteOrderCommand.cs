using Microsoft.EntityFrameworkCore;
using SupermarketSystem.Application.Common.Interfaces;
using SupermarketSystem.Application.Common.Results;
using SupermarketSystem.Application.Sales.CompleteSale;
using SupermarketSystem.Domain.Ordering;

namespace SupermarketSystem.Application.Ordering.CompleteOrder;

public sealed record CompleteOrderPaymentDto(Guid PaymentMethodId, decimal Amount, string? ExternalReference);

/// <summary>
/// لحظة التسليم الفعلية والكاش يترجع (Cash on Delivery) - هون بس
/// تُنشأ الفاتورة الحقيقية وينخصم المخزون. أسطر الطلب تُنقَل *كما هي*
/// بلا تعديل (قرار صاحب المشروع - تعديل أصناف بيبطّئ العملية)، عدا
/// اختيار الدفعة (Batch) للأصناف المتتبَّعة - يُحسَم هون تلقائيًا
/// (أقرب صلاحية أولًا) لأنه الزبون/الطلب الأصلي ما بيعرف رقم الدفعة
/// أصلًا.
/// </summary>
public sealed record CompleteOrderCommand(Guid OrderId, IReadOnlyList<CompleteOrderPaymentDto> Payments, Guid ClientRequestId);

public sealed class CompleteOrderHandler
{
    private readonly IApplicationDbContext _context;
    private readonly CompleteSaleHandler _completeSaleHandler;

    public CompleteOrderHandler(IApplicationDbContext context, CompleteSaleHandler completeSaleHandler)
    {
        _context = context;
        _completeSaleHandler = completeSaleHandler;
    }

    public async Task<Result<CompleteSaleResponse>> HandleAsync(CompleteOrderCommand command, CancellationToken cancellationToken)
    {
        var order = await _context.Orders.FirstOrDefaultAsync(o => o.Id == command.OrderId, cancellationToken);
        if (order is null)
        {
            return Result.Failure<CompleteSaleResponse>(Error.NotFound("Order.NotFound", $"الطلب '{command.OrderId}' غير موجود."));
        }

        if (order.Status != OrderStatus.Accepted)
        {
            return Result.Failure<CompleteSaleResponse>(Error.BusinessRule(
                "Order.NotAccepted", "لازم تقبل الطلب أولًا قبل ما تكمّله."));
        }

        var productIds = order.Items.Select(i => i.ProductId).Distinct().ToList();
        var batchTrackedProducts = await _context.Products.AsNoTracking()
            .Where(p => productIds.Contains(p.Id) && p.IsBatchTracked)
            .Select(p => p.Id)
            .ToListAsync(cancellationToken);

        var saleItems = new List<CompleteSaleItemDto>();
        foreach (var item in order.Items)
        {
            Guid? productBatchId = null;

            if (batchTrackedProducts.Contains(item.ProductId))
            {
                // أقرب صلاحية أولًا (FEFO) بين الدفعات اللي عندها مخزون فعلي -
                // الطلب الأصلي ما بيحدد دفعة، فهاي أفضل قاعدة افتراضية آمنة.
                var earliestBatch = await (
                    from stock in _context.Stocks.AsNoTracking()
                    join batch in _context.ProductBatches.AsNoTracking() on stock.ProductBatchId equals batch.Id
                    where stock.ProductId == item.ProductId && stock.BranchId == order.BranchId
                          && stock.ProductBatchId != null && stock.QuantityOnHand >= item.Quantity
                    orderby batch.ExpiryDate ?? DateOnly.MaxValue
                    select batch.Id)
                    .FirstOrDefaultAsync(cancellationToken);

                if (earliestBatch == default)
                {
                    return Result.Failure<CompleteSaleResponse>(Error.BusinessRule(
                        "Order.NoBatchAvailable", $"لا توجد دفعة كافية للمنتج '{item.ProductId}' لإكمال الطلب."));
                }

                productBatchId = earliestBatch;
            }

            saleItems.Add(new CompleteSaleItemDto(item.ProductId, item.ProductUnitId, item.Quantity, ManualDiscountAmount: 0, productBatchId));
        }

        var salePayments = command.Payments
            .Select(p => new CompleteSalePaymentDto(p.PaymentMethodId, p.Amount, p.ExternalReference, Guid.NewGuid()))
            .ToList();

        var completeSaleCommand = new CompleteSaleCommand(
            order.BranchId, command.ClientRequestId, order.CustomerId,
            InvoiceLevelDiscountAmount: 0, saleItems, salePayments);

        var saleResult = await _completeSaleHandler.HandleAsync(completeSaleCommand, cancellationToken);
        if (saleResult.IsFailure)
        {
            return saleResult;
        }

        order.Complete(saleResult.Value.SaleInvoiceId);
        await _context.SaveChangesAsync(cancellationToken);

        return saleResult;
    }
}
