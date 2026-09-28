using Microsoft.EntityFrameworkCore;
using SupermarketSystem.Application.Common.Interfaces;
using SupermarketSystem.Domain.Inventory;

namespace SupermarketSystem.Application.Inventory.ReturnedPendingStocktake;

// =====================================================================================
// مواد مرتجعة بانتظار جرد (ملاحظة صاحب المشروع 23/9، انبنت 28/9/2026) - ضد الإرجاع الوهمي: كاشير بيسجّل إرجاع
// ما صار وبياخد المبلغ. الإرجاع بيرجّع البضاعة للمخزون بالسستم، فلو ما في بضاعة فعلًا بيطلع نقص بأول جرد للمادة.
// هون: كل مادة إلها إرجاع **بعد آخر مرة انعدّت فيها** بجرد معتمد بنفس الفرع = "بانتظار جرد" (بتنقترح أول شي
// بالجرد الجزئي)، وبتختفي لحالها لما تنعدّ. واعتماد جرد لقى فرق بمادة من هالقائمة = تنبيه مربوط بالإرجاع والكاشير
// (ApproveStocktakeHandler).
//
// "انعدّت" = StocktakeItem.CountedAtUtc بجرد حالته Approved (وقت العدّ الفعلي، مش الاعتماد: إرجاع بين العدّ
// والاعتماد ما انشمل بالعدّ، فبيضل بانتظار جرد).
// =====================================================================================

public sealed record ReturnedPendingStocktakeReturnDto(
    string ReturnInvoiceNumber,
    string OriginalSaleInvoiceNumber,
    DateTime ReturnedAtUtc,
    decimal QuantityBase,
    string CashierName);

public sealed record ReturnedPendingStocktakeItemDto(
    Guid BranchId,
    string BranchName,
    Guid ProductId,
    string ProductName,
    decimal QuantityReturnedBase,
    int ReturnCount,
    DateTime LastReturnAtUtc,
    DateTime? LastCountedAtUtc,
    IReadOnlyList<ReturnedPendingStocktakeReturnDto> Returns);

public sealed record GetReturnedPendingStocktakeQuery(Guid? BranchId);

public static class ReturnedPendingStocktakeCalculator
{
    /// <summary>
    /// سقف للخلف: الجرد الكامل الشهري بيغطّي كل شي، فإرجاع أقدم من 90 يوم بلا جرد للمادة لحد الآن مش "قريب" -
    /// وبيحمي الاستعلام من يكبر للأبد قبل أول جرد.
    /// </summary>
    public const int LookbackDays = 90;

    /// <summary>آخر وقت انعدّت فيه كل مادة بجرد معتمد بالفرع. excludeStocktakeId: الجرد اللي عم ينعتمد هلق (قبل ما يتسجّل).</summary>
    public static async Task<Dictionary<(Guid BranchId, Guid ProductId), DateTime>> LastCountedAtAsync(
        IApplicationDbContext context, Guid? branchId, IReadOnlyCollection<Guid>? productIds, Guid? excludeStocktakeId, CancellationToken cancellationToken)
    {
        var items = context.StocktakeItems.AsNoTracking();
        if (productIds is not null)
        {
            items = items.Where(i => productIds.Contains(i.ProductId));
        }

        var rows = await (
                from item in items
                join stocktake in context.Stocktakes.AsNoTracking() on item.StocktakeId equals stocktake.Id
                where stocktake.Status == StocktakeStatus.Approved
                      && item.CountedQuantity != null
                      && (branchId == null || stocktake.BranchId == branchId)
                      && (excludeStocktakeId == null || stocktake.Id != excludeStocktakeId)
                group new { item.CountedAtUtc, stocktake.ApprovedAtUtc } by new { stocktake.BranchId, item.ProductId } into g
                select new { g.Key.BranchId, g.Key.ProductId, LastAt = g.Max(x => x.CountedAtUtc ?? x.ApprovedAtUtc) })
            .ToListAsync(cancellationToken);

        return rows.Where(r => r.LastAt != null).ToDictionary(r => (r.BranchId, r.ProductId), r => r.LastAt!.Value);
    }

    /// <summary>
    /// الإرجاعات (بالوحدة الأساسية) لكل (فرع، مادة) بعد آخر عدّ - untilUtc: لحد وقت معيّن (مثلًا وقت عدّ المادة بالجرد
    /// الحالي)، null = لحد الآن.
    /// </summary>
    public static async Task<List<ReturnedPendingStocktakeItemDto>> CalculateAsync(
        IApplicationDbContext context, DateTime nowUtc, Guid? branchId, IReadOnlyCollection<Guid>? productIds,
        Guid? excludeStocktakeId, CancellationToken cancellationToken)
    {
        var sinceUtc = nowUtc.AddDays(-LookbackDays);

        var returnItems = context.ReturnInvoiceItems.AsNoTracking();
        if (productIds is not null)
        {
            returnItems = returnItems.Where(i => productIds.Contains(i.ProductId));
        }

        var returnRows = await (
                from item in returnItems
                join ret in context.ReturnInvoices.AsNoTracking() on item.ReturnInvoiceId equals ret.Id
                join unit in context.ProductUnits.AsNoTracking() on item.ProductUnitId equals unit.Id
                join sale in context.SaleInvoices.AsNoTracking() on ret.OriginalSaleInvoiceId equals sale.Id
                where ret.CreatedAtUtc >= sinceUtc
                      && (branchId == null || ret.BranchId == branchId)
                select new
                {
                    ret.BranchId,
                    item.ProductId,
                    QuantityBase = item.Quantity * unit.ConversionFactorToBase,
                    ret.InvoiceNumber,
                    SaleNumber = sale.InvoiceNumber,
                    ret.CreatedAtUtc,
                    ret.CreatedByUserId
                })
            .ToListAsync(cancellationToken);

        if (returnRows.Count == 0)
        {
            return new List<ReturnedPendingStocktakeItemDto>();
        }

        var involvedProducts = returnRows.Select(r => r.ProductId).Distinct().ToList();
        var lastCounted = await LastCountedAtAsync(context, branchId, involvedProducts, excludeStocktakeId, cancellationToken);

        var pending = returnRows
            .Where(r => !lastCounted.TryGetValue((r.BranchId, r.ProductId), out var countedAt) || r.CreatedAtUtc > countedAt)
            .ToList();
        if (pending.Count == 0)
        {
            return new List<ReturnedPendingStocktakeItemDto>();
        }

        var userIds = pending.Where(r => r.CreatedByUserId != null).Select(r => r.CreatedByUserId!.Value).Distinct().ToList();
        var users = await context.Users.IgnoreQueryFilters().AsNoTracking()
            .Where(u => userIds.Contains(u.Id))
            .ToDictionaryAsync(u => u.Id, u => u.FullName, cancellationToken);
        var productIdsPending = pending.Select(r => r.ProductId).Distinct().ToList();
        var products = await context.Products.IgnoreQueryFilters().AsNoTracking()
            .Where(p => productIdsPending.Contains(p.Id))
            .ToDictionaryAsync(p => p.Id, p => p.Name, cancellationToken);
        var branchIds = pending.Select(r => r.BranchId).Distinct().ToList();
        var branches = await context.Branches.IgnoreQueryFilters().AsNoTracking()
            .Where(b => branchIds.Contains(b.Id))
            .ToDictionaryAsync(b => b.Id, b => b.Name, cancellationToken);

        return pending
            .GroupBy(r => (r.BranchId, r.ProductId))
            .Select(g => new ReturnedPendingStocktakeItemDto(
                g.Key.BranchId,
                branches.GetValueOrDefault(g.Key.BranchId, "فرع غير معروف"),
                g.Key.ProductId,
                products.GetValueOrDefault(g.Key.ProductId, "منتج غير معروف"),
                g.Sum(r => r.QuantityBase),
                g.Select(r => r.InvoiceNumber).Distinct().Count(),
                g.Max(r => r.CreatedAtUtc),
                lastCounted.TryGetValue(g.Key, out var counted) ? counted : null,
                g.OrderByDescending(r => r.CreatedAtUtc)
                    .Select(r => new ReturnedPendingStocktakeReturnDto(
                        r.InvoiceNumber, r.SaleNumber, r.CreatedAtUtc, r.QuantityBase,
                        r.CreatedByUserId is { } id ? users.GetValueOrDefault(id, "غير معروف") : "غير معروف"))
                    .ToList()))
            .OrderByDescending(i => i.LastReturnAtUtc)
            .ToList();
    }
}

public sealed class GetReturnedPendingStocktakeHandler
{
    private readonly IApplicationDbContext _context;
    private readonly IDateTimeProvider _dateTimeProvider;

    public GetReturnedPendingStocktakeHandler(IApplicationDbContext context, IDateTimeProvider dateTimeProvider)
    {
        _context = context;
        _dateTimeProvider = dateTimeProvider;
    }

    public Task<List<ReturnedPendingStocktakeItemDto>> HandleAsync(GetReturnedPendingStocktakeQuery query, CancellationToken cancellationToken) =>
        ReturnedPendingStocktakeCalculator.CalculateAsync(_context, _dateTimeProvider.UtcNow, query.BranchId, productIds: null, excludeStocktakeId: null, cancellationToken);
}
