using Microsoft.EntityFrameworkCore;
using SupermarketSystem.Application.Common.Interfaces;
using SupermarketSystem.Application.Common.Pagination;

namespace SupermarketSystem.Application.Reporting.GetExpiringBatches;

public sealed record GetExpiringBatchesQuery(PagedRequest Paging, Guid BranchId);

public sealed record ExpiringBatchItemDto(
    Guid ProductBatchId,
    Guid ProductId,
    string ProductName,
    string BatchNumber,
    DateOnly ExpiryDate,
    int DaysRemaining,
    decimal QuantityOnHand);

/// <summary>
/// دفعات (`ProductBatch`) قرب انتهاء الصلاحية بفرع معيّن، مع رصيد فعلي
/// موجب فقط (دفعة نفدت كميتها ما إلها قيمة عملية بهالتقرير). الحد
/// الزمني قابل للتعديل من الإعدادات
/// (`InventorySettingsKeys.ExpiryAlertThresholdDays`، افتراضي 14 يوم).
/// `DaysRemaining` سالب يعني دفعة **انتهت فعليًا** وما زالت برصيد -
/// أولوية أعلى للمراجعة، عمدًا غير مستبعدة من التقرير.
/// </summary>
public sealed class GetExpiringBatchesHandler
{
    private readonly IApplicationDbContext _context;
    private readonly ISettingsProvider _settingsProvider;
    private readonly IDateTimeProvider _dateTimeProvider;

    public GetExpiringBatchesHandler(
        IApplicationDbContext context, ISettingsProvider settingsProvider, IDateTimeProvider dateTimeProvider)
    {
        _context = context;
        _settingsProvider = settingsProvider;
        _dateTimeProvider = dateTimeProvider;
    }

    public async Task<PagedResult<ExpiringBatchItemDto>> HandleAsync(
        GetExpiringBatchesQuery query, CancellationToken cancellationToken)
    {
        var paging = query.Paging.Normalized();

        var thresholdDays = (int)await _settingsProvider.GetDecimalAsync(
            InventorySettingsKeys.ExpiryAlertThresholdDays, defaultValue: 14m, cancellationToken);

        var today = DateOnly.FromDateTime(_dateTimeProvider.UtcNow);
        var cutoff = today.AddDays(thresholdDays);

        var batches = _context.ProductBatches.AsNoTracking()
            .Where(b => b.BranchId == query.BranchId && b.ExpiryDate != null && b.ExpiryDate <= cutoff);

        var withStock = batches
            .Join(_context.Stocks.AsNoTracking().Where(s => s.QuantityOnHand > 0),
                b => (Guid?)b.Id, s => s.ProductBatchId,
                (b, s) => new { Batch = b, s.QuantityOnHand });

        var totalCount = await withStock.CountAsync(cancellationToken);

        var items = await withStock
            .OrderBy(x => x.Batch.ExpiryDate)
            .Skip(paging.Skip)
            .Take(paging.PageSize)
            .Join(_context.Products.AsNoTracking(),
                x => x.Batch.ProductId, p => p.Id,
                (x, p) => new ExpiringBatchItemDto(
                    x.Batch.Id,
                    p.Id,
                    p.Name,
                    x.Batch.BatchNumber,
                    x.Batch.ExpiryDate!.Value,
                    x.Batch.ExpiryDate!.Value.DayNumber - today.DayNumber,
                    x.QuantityOnHand))
            .ToListAsync(cancellationToken);

        return new PagedResult<ExpiringBatchItemDto>(items, totalCount, paging.PageNumber, paging.PageSize);
    }
}
