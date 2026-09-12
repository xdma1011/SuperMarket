using Microsoft.EntityFrameworkCore;
using SupermarketSystem.Application.Common.Interfaces;

namespace SupermarketSystem.Application.Inventory.GetProductBatchesWithStock;

public sealed record GetProductBatchesWithStockQuery(Guid ProductId, Guid BranchId);

public sealed record ProductBatchWithStockDto(Guid ProductBatchId, string BatchNumber, DateOnly? ExpiryDate, decimal QuantityOnHand);

/// <summary>
/// كانت مفقودة - محتاجة لاختيار "أي دفعة بالضبط" وقت إرسال نقل مخزون
/// لمنتج متتبَّع دفعات (راجع StockTransfer.cs). بس الدفعات اللي فيها
/// رصيد فعلي > 0 بهذا الفرع تحديدًا - دفعة رصيدها صفر ما لها معنى تُنقَل.
/// </summary>
public sealed class GetProductBatchesWithStockHandler
{
    private readonly IApplicationDbContext _context;

    public GetProductBatchesWithStockHandler(IApplicationDbContext context)
    {
        _context = context;
    }

    public async Task<IReadOnlyList<ProductBatchWithStockDto>> HandleAsync(GetProductBatchesWithStockQuery query, CancellationToken cancellationToken)
    {
        // خطأ إنتاج حقيقي كان هون: OrderBy بعد Join مباشرة على خاصية
        // بسجل (record) مُنشأ بالـresultSelector - EF Core ما بيقدر
        // يترجمها لـSQL (InvalidOperationException وقت التنفيذ، أي
        // استدعاء فعلي لهذا الـendpoint لمنتج متتبَّع دفعات كان يفشل
        // بـ500 دائمًا). الحل: نرتّب على نوع مجهول (anonymous type) *قبل*
        // بناء الـDTO النهائي بـSelect منفصل - يترجم بشكل طبيعي.
        return await _context.ProductBatches.AsNoTracking()
            .Where(b => b.ProductId == query.ProductId && b.BranchId == query.BranchId)
            .Join(_context.Stocks.AsNoTracking().Where(s => s.QuantityOnHand > 0),
                b => b.Id, s => s.ProductBatchId,
                (b, s) => new { b.Id, b.BatchNumber, b.ExpiryDate, s.QuantityOnHand })
            .OrderBy(x => x.ExpiryDate)
            .Select(x => new ProductBatchWithStockDto(x.Id, x.BatchNumber, x.ExpiryDate, x.QuantityOnHand))
            .ToListAsync(cancellationToken);
    }
}
