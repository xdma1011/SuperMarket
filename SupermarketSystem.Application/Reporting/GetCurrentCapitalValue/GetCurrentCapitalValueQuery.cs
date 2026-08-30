using Microsoft.EntityFrameworkCore;
using SupermarketSystem.Application.Common.Interfaces;
using SupermarketSystem.Application.Common.Pagination;
using SupermarketSystem.Domain.Purchasing;

namespace SupermarketSystem.Application.Reporting.GetCurrentCapitalValue;

public sealed record GetCurrentCapitalValueQuery(PagedRequest Paging, Guid? BranchId);

public sealed record CapitalValueItemDto(
    Guid ProductId,
    string ProductName,
    Guid BranchId,
    decimal QuantityOnHand,
    decimal WeightedAverageCost,
    decimal TotalValue);

public sealed record GetCurrentCapitalValueResponse(
    PagedResult<CapitalValueItemDto> Items,
    decimal TotalCapitalValue,
    int ProductsExcludedNoCostHistory);

/// <summary>
/// طريقة التكلفة المعتمدة: متوسط مرجّح (Weighted Average) — قرار صريح،
/// ليس FIFO. السبب: المتوسط المرجّح موحّد الحساب لكل المنتجات (سواء
/// batch-tracked أو لا)، بينما FIFO محتاج تتبّع دقيق لأي دفعة اتخصمت منها
/// كل عملية بيع بترتيب زمني صارم — تعقيد إضافي حقيقي رُفض عمدًا (كان
/// معلّق كقرار منذ Architecture Review §12 الأصلية: "لا نخترع طريقة تكلفة
/// بلا طلب صريح").
///
/// القيمة تُحسب فقط للمخزون الموجب (QuantityOnHand > 0). المخزون السالب
/// (من إعداد AllowNegativeStock) يمثّل "بضاعة بيعت قبل ما تُسجَّل
/// بالمشتريات" — التزام ضمني، لا رأس مال، فمستبعد من هذا الرقم عمدًا.
///
/// TotalCapitalValue وProductsExcludedNoCostHistory محسوبان على *كامل*
/// المجموعة المفلترة، لا الصفحة المعروضة فقط — هذا الرقم اللي صاحب المحل
/// فعليًا بده يعرفه ("قديش رأس مالي كله")، مش مجموع صفحة واحدة.
///
/// ═══════════════════════════════════════════════════════════════════
/// قيد أمني معروف (Trade-off مقصود، لا خطأ كود):
/// ═══════════════════════════════════════════════════════════════════
/// المتوسط المرجّح بطبيعته الرياضية بيمتص التلاعب الصغير المتكرر — لو
/// حد سجّل أسعار شراء مضخّمة شوي على دفعات كتير صغيرة (بدل دفعة كبيرة
/// واحدة واضحة)، رقم WeightedAverageCost الإجمالي هون بيذوّب الفرق ولا
/// يظهره كشيء لافت. هذا التقرير وحده *غير كافٍ* لاكتشاف هالنمط من
/// التلاعب. للفحص الفعلي: لازم مراجعة فاتورة-فاتورة عبر تقرير "مقارنة
/// أسعار الموردين" الموجود أصلًا بالتقارير — لا الاعتماد على هذا الرقم
/// الإجمالي وحده. لا حل كودي لهاي النقطة؛ الحل الوحيد بشري: مراجعة
/// دورية لتقرير مقارنة الأسعار.
/// </summary>
public sealed class GetCurrentCapitalValueHandler
{
    private readonly IApplicationDbContext _context;

    public GetCurrentCapitalValueHandler(IApplicationDbContext context)
    {
        _context = context;
    }

    public async Task<GetCurrentCapitalValueResponse> HandleAsync(
        GetCurrentCapitalValueQuery query, CancellationToken cancellationToken)
    {
        var paging = query.Paging.Normalized();

        var stockQuery = _context.Stocks.AsNoTracking().Where(s => s.QuantityOnHand > 0);
        if (query.BranchId is { } branchId)
        {
            stockQuery = stockQuery.Where(s => s.BranchId == branchId);
        }

        // متوسط التكلفة المرجّح لكل (منتج، فرع) — من فواتير الشراء
        // المكتملة (Received) فقط؛ فواتير Draft لسه مش قرار شراء نهائي.
        var costAverages = _context.PurchaseInvoiceItems.AsNoTracking()
            .Join(
                _context.PurchaseInvoices.AsNoTracking().Where(pi => pi.Status == PurchaseInvoiceStatus.Received),
                i => i.PurchaseInvoiceId, pi => pi.Id,
                (i, pi) => new { i.ProductId, pi.BranchId, i.Quantity, i.UnitCost })
            .GroupBy(x => new { x.ProductId, x.BranchId })
            .Select(g => new
            {
                g.Key.ProductId,
                g.Key.BranchId,
                TotalQuantity = g.Sum(x => x.Quantity),
                TotalCost = g.Sum(x => x.Quantity * x.UnitCost)
            });

        var valued = stockQuery
            .Join(costAverages,
                s => new { s.ProductId, s.BranchId },
                c => new { c.ProductId, c.BranchId },
                (s, c) => new { s.ProductId, s.BranchId, s.QuantityOnHand, AverageCost = c.TotalCost / c.TotalQuantity });

        // الإجمالي والعدد المُستبعَد يُحسبان على المجموعة الكاملة، لا الصفحة.
        var totalCapitalValue = await valued.SumAsync(x => x.QuantityOnHand * x.AverageCost, cancellationToken);

        var stockCount = await stockQuery.CountAsync(cancellationToken);
        var valuedCount = await valued.CountAsync(cancellationToken);
        var excludedCount = stockCount - valuedCount;

        var page = await valued
            .OrderByDescending(x => x.QuantityOnHand * x.AverageCost)
            .Skip(paging.Skip)
            .Take(paging.PageSize)
            .Join(_context.Products.AsNoTracking(),
                x => x.ProductId, p => p.Id,
                (x, p) => new CapitalValueItemDto(
                    p.Id, p.Name, x.BranchId, x.QuantityOnHand, x.AverageCost, x.QuantityOnHand * x.AverageCost))
            .ToListAsync(cancellationToken);

        var pagedItems = new PagedResult<CapitalValueItemDto>(page, valuedCount, paging.PageNumber, paging.PageSize);

        return new GetCurrentCapitalValueResponse(pagedItems, totalCapitalValue, excludedCount);
    }
}
