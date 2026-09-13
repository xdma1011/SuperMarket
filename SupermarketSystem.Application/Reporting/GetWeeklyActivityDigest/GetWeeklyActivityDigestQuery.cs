using Microsoft.EntityFrameworkCore;
using SupermarketSystem.Application.Common.Interfaces;
using SupermarketSystem.Domain.Inventory;
using SupermarketSystem.Domain.Sales;

namespace SupermarketSystem.Application.Reporting.GetWeeklyActivityDigest;

public sealed record GetWeeklyActivityDigestQuery(DateTime SinceUtc);

public sealed record WeeklyActivityDigestResponse(
    int VoidedSalesCount,
    decimal VoidedSalesTotalAmount,
    int ReturnsCount,
    decimal ReturnsTotalAmount,
    int ManualStockMovementsCount,
    decimal ManualStockMovementsTotalQuantity);

/// <summary>
/// ملخّص أسبوعي مجمَّع لأنشطة "حسّاسة" بكل الفروع (إلغاء بيع، إرجاع، حركة
/// مخزون يدوية/ضيافة) — يغذّي WeeklyActivityDigestBackgroundService
/// (راجع تعليقه بـInfrastructure لسبب استخدام BackgroundService بسيط لا
/// مكتبة جدولة).
///
/// IgnoreQueryFilters() على الثلاثة استعلامات هون *مقصود وضروري*، لا
/// إغفال: هذا الـHandler بيُستدعى من BackgroundService (بلا HttpContext
/// إطلاقًا)، وRealCurrentUserContext بيرجّع BranchId=null و
/// IsCrossBranchAccessAllowed=false بغياب الطلب — يعني مرشِّح الفرع العام
/// (AppDbContext.SetBranchFilter: isCrossBranchAccessAllowed ||
/// e.BranchId == currentBranchId) بيرجّع صفر صفوف دايمًا لأي كيان
/// Branch-owned بدون هذا التجاوز الصريح. الملخّص هون مقصود يكون بكل
/// الفروع مجتمعة (بلا تقسيم لكل فرع لحاله) — صاحب المشروع يريد رقم واحد
/// أسبوعي، لا تقرير مفصَّل.
/// </summary>
public sealed class GetWeeklyActivityDigestHandler
{
    private readonly IApplicationDbContext _context;

    public GetWeeklyActivityDigestHandler(IApplicationDbContext context)
    {
        _context = context;
    }

    public async Task<WeeklyActivityDigestResponse> HandleAsync(
        GetWeeklyActivityDigestQuery query, CancellationToken cancellationToken)
    {
        var voidedSalesAmounts = await _context.SaleInvoices.AsNoTracking()
            .IgnoreQueryFilters()
            .Where(s => s.Status == SaleInvoiceStatus.Voided && s.VoidedAtUtc >= query.SinceUtc)
            .Select(s => s.TotalAmount)
            .ToListAsync(cancellationToken);

        var returnAmounts = await _context.ReturnInvoices.AsNoTracking()
            .IgnoreQueryFilters()
            .Where(r => r.CreatedAtUtc >= query.SinceUtc)
            .Select(r => r.TotalAmount)
            .ToListAsync(cancellationToken);

        // "حركات المخزون اليدوية" = ReferenceType.ManualAdjustment، لا
        // MovementType.AdjustmentIn/Out تحديدًا — حاليًا كل حركة ضيافة
        // (RecordComplimentaryIssueHandler) تُسجَّل بهذا الـReferenceType
        // (راجع تعليقه)، وأي نوع تعديل يدوي مستقبلي بنفس الـReferenceType
        // بينضم تلقائيًا هون بلا تغيير.
        var manualMovementQuantities = await _context.StockMovements.AsNoTracking()
            .IgnoreQueryFilters()
            .Where(m => m.ReferenceType == StockMovementReferenceType.ManualAdjustment && m.OccurredAtUtc >= query.SinceUtc)
            .Select(m => m.QuantityBase)
            .ToListAsync(cancellationToken);

        return new WeeklyActivityDigestResponse(
            voidedSalesAmounts.Count,
            voidedSalesAmounts.Sum(),
            returnAmounts.Count,
            returnAmounts.Sum(),
            manualMovementQuantities.Count,
            manualMovementQuantities.Sum());
    }
}
