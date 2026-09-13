using Microsoft.EntityFrameworkCore;
using SupermarketSystem.Application.Common.Interfaces;
using SupermarketSystem.Domain.Inventory;
using SupermarketSystem.Domain.Sales;

namespace SupermarketSystem.Application.Reviews.GetPendingReviews;

public enum PendingReviewType
{
    Return = 1,
    ComplimentaryIssue = 2,
    HighPurchasePrice = 3,
    Complaint = 4
}

public sealed record PendingReviewItemDto(
    PendingReviewType Type,
    string TypeTitle,
    Guid ReferenceId,
    string Title,
    string Detail,
    decimal? Amount,
    Guid BranchId,
    DateTime OccurredAtUtc);

/// <summary>
/// قسم مستقل عن Items - فاتورة بيع ملغاة (VoidSale) بانتظار المراجعة.
/// مش جزء من Items العامة (PendingReviewItemDto) لأنها محتاجة حقول
/// خاصة بيها (سبب الإلغاء، منفّذ الإلغاء) ما بتنلبس منطقيًا على شكل
/// Title/Detail الحر المستخدم لباقي الأنواع - قرار تصميم مقصود، راجع
/// تقرير التسليم. TotalCount بردّ الاستعلام يشملها زائد Items.Count.
/// </summary>
public sealed record VoidedSaleReviewDto(
    Guid SaleInvoiceId,
    string InvoiceNumber,
    string VoidReasonTitle,
    string? VoidNotes,
    decimal Amount,
    DateTime VoidedAtUtc,
    string VoidedByName,
    Guid BranchId);

public sealed record GetPendingReviewsResponse(
    IReadOnlyList<PendingReviewItemDto> Items,
    int TotalCount,
    IReadOnlyList<VoidedSaleReviewDto> VoidedSales);

/// <summary>
/// نقطة تجميع واحدة لكل شي "بانتظار مراجعة إدارية" — نفس فلسفة
/// AllowWithReview المطبَّقة بكل النظام. خمسة مصادر حاليًا: ReturnInvoice
/// (كل إرجاع غير مُراجَع بعد)، StockMovement.ManualAdjustment غير
/// المُراجَع (يشمل الضيافة اللي تجاوزت الحد اليومي - NeedsReview بالـDTO
/// بس مش بالفلتر، راجع التعليق تحت - وأي تعديل مخزون يدوي تاني)،
/// PurchaseInvoiceItem.NeedsReview (سعر شراء أعلى بنسبة ملحوظة عن متوسط
/// آخر 5 عمليات شراء لنفس المنتج)، Complaint (شكوى زبون عبر تطبيق
/// الزبائن)، وSaleInvoice بحالة Voided غير المُراجَعة (قسم VoidedSales
/// المنفصل).
///
/// إضافة مصدر إضافي لاحقًا تعني إضافة استعلام موازٍ هون بس، بلا أي تغيير
/// جوهري على شكل الرد أو الفرونت إند.
/// </summary>
public sealed class GetPendingReviewsHandler
{
    private readonly IApplicationDbContext _context;

    public GetPendingReviewsHandler(IApplicationDbContext context)
    {
        _context = context;
    }

    /// <summary>
    /// ignoreBranchFilter=true حصرًا لخدمة الخلفية PendingReviewEscalationBackgroundService -
    /// خدمة خلفية بلا HttpContext، فـRealCurrentUserContext.BranchId تِرجع null
    /// دايمًا وIsCrossBranchAccessAllowed تِرجع false دايمًا، ففلتر الفرع
    /// العالمي (_isCrossBranchAccessAllowed || e.BranchId == _currentBranchId)
    /// بيصير false دايمًا لكل الكيانات IBranchOwned - يعني صفر نتيجة صفر تصعيد
    /// أبدًا، بصمت، بغض النظر عن الوضع الفعلي (فجوة حقيقية كانت موجودة وغير
    /// مكتشفة بميزة "مشحونة أصلًا"). endpoint المراجعات بالويب يستدعي بلا هالمعامل
    /// (القيمة الافتراضية false) فيضل يحترم فرع الأدمن المسجّل دخول بالضبط
    /// زي قبل - هذا التغيير ما بيأثر على سلوك الويب إطلاقًا.
    /// </summary>
    public async Task<GetPendingReviewsResponse> HandleAsync(CancellationToken cancellationToken, bool ignoreBranchFilter = false)
    {
        var returnInvoices = ignoreBranchFilter
            ? _context.ReturnInvoices.IgnoreQueryFilters().AsNoTracking()
            : _context.ReturnInvoices.AsNoTracking();

        var pendingReturns = await returnInvoices
            .Where(r => r.ReviewedAtUtc == null)
            .Select(r => new PendingReviewItemDto(
                PendingReviewType.Return,
                "إرجاع",
                r.Id,
                r.InvoiceNumber,
                "إرجاع بانتظار المراجعة",
                r.TotalAmount,
                r.BranchId,
                r.CreatedAtUtc))
            .ToListAsync(cancellationToken);

        var stockMovements = ignoreBranchFilter
            ? _context.StockMovements.IgnoreQueryFilters().AsNoTracking()
            : _context.StockMovements.AsNoTracking();

        // كل تعديل مخزون يدوي (ManualAdjustment) غير مُراجَع - لا NeedsReview
        // بالفلتر عمدًا (كانت هيك سابقًا، وهذا سبب فجوة حقيقية: الضيافة
        // تحت الحد اليومي، أو أي تعديل يدوي تاني، ما كانت تظهر أبدًا هون
        // حتى لو صاحب المحل فتح الصفحة كل يوم). NeedsReview ضلّت بالـDTO
        // (مو بالفلتر) عشان الفرونت إند يميّز "العاجل" (تجاوز الحد) عن
        // "العادي" بصريًا لو حاب - راجع تقرير التسليم. TypeTitle/Title
        // "ضيافة" ثابت هون لأنه RecordComplimentaryIssueHandler هو
        // المستخدم الوحيد فعليًا لـManualAdjustment اليوم (تحقّقنا
        // بـgrep) - لو انضافت ميزة تعديل مخزون يدوي عامة مستقبلًا بنفس
        // ReferenceType، هاي التسمية لازم تصير مشروطة بـMovementType.
        var pendingComplimentary = await stockMovements
            .Where(m => m.ReferenceType == StockMovementReferenceType.ManualAdjustment && m.ReviewedAtUtc == null)
            .Join(_context.Products.AsNoTracking(), m => m.ProductId, p => p.Id, (m, p) => new { Movement = m, ProductName = p.Name })
            .Select(x => new PendingReviewItemDto(
                PendingReviewType.ComplimentaryIssue,
                "ضيافة",
                x.Movement.Id,
                x.ProductName,
                x.Movement.NeedsReview
                    ? "تجاوزت الحد اليومي المسموح للضيافة"
                    : "تعديل مخزون يدوي بانتظار المراجعة",
                x.Movement.QuantityBase,
                x.Movement.BranchId,
                x.Movement.OccurredAtUtc))
            .ToListAsync(cancellationToken);

        // نفس مبدأ CLAUDE.md §3.1: لا تنسيق نص (:F2) جوّا Select() مترجَم
        // لـSQL - نجيب الحقول الخام أول، ونبني نص التفاصيل بالذاكرة.
        var purchaseInvoices = ignoreBranchFilter
            ? _context.PurchaseInvoices.IgnoreQueryFilters().AsNoTracking()
            : _context.PurchaseInvoices.AsNoTracking();

        var highPriceRows = await _context.PurchaseInvoiceItems.AsNoTracking()
            .Where(i => i.NeedsReview && i.ReviewedAtUtc == null)
            .Join(purchaseInvoices, i => i.PurchaseInvoiceId, p => p.Id,
                (i, p) => new { i.Id, i.UnitCost, p.InvoiceNumber, p.BranchId, p.CreatedAtUtc, i.ProductId })
            .Join(_context.Products.AsNoTracking(), x => x.ProductId, prod => prod.Id,
                (x, prod) => new { x.Id, x.UnitCost, x.InvoiceNumber, x.BranchId, x.CreatedAtUtc, ProductName = prod.Name })
            .ToListAsync(cancellationToken);

        var pendingHighPrices = highPriceRows
            .Select(x => new PendingReviewItemDto(
                PendingReviewType.HighPurchasePrice,
                "ارتفاع سعر شراء",
                x.Id,
                x.ProductName,
                $"سعر الوحدة {x.UnitCost:F2} بفاتورة {x.InvoiceNumber} - أعلى من المعتاد",
                x.UnitCost,
                x.BranchId,
                x.CreatedAtUtc))
            .ToList();

        // شكوى ممكن ما تكون مرتبطة بطلب (OrderId فاضي) - Order مافيها فرع
        // ثابت بهالحالة، فـBranchId يضل Guid.Empty (مش مستخدَم أصلًا
        // بواجهة صفحة المراجعات الحالية - راجع reviews.component.ts).
        var complaintRows = await _context.Complaints.AsNoTracking()
            .Where(c => !c.IsResolved)
            .Join(_context.Customers.AsNoTracking(), c => c.CustomerId, cust => cust.Id,
                (c, cust) => new { c.Id, c.Text, c.OrderId, CustomerName = cust.FullName, c.CreatedAtUtc })
            .ToListAsync(cancellationToken);

        var orders = ignoreBranchFilter
            ? _context.Orders.IgnoreQueryFilters().AsNoTracking()
            : _context.Orders.AsNoTracking();

        var orderBranchByOrderId = await orders
            .Where(o => complaintRows.Select(c => c.OrderId).Contains(o.Id))
            .Select(o => new { o.Id, o.BranchId })
            .ToDictionaryAsync(o => o.Id, o => o.BranchId, cancellationToken);

        var pendingComplaints = complaintRows
            .Select(x => new PendingReviewItemDto(
                PendingReviewType.Complaint,
                "شكوى",
                x.Id,
                x.CustomerName,
                x.Text,
                Amount: null,
                x.OrderId is { } orderId && orderBranchByOrderId.TryGetValue(orderId, out var branchId) ? branchId : Guid.Empty,
                x.CreatedAtUtc))
            .ToList();

        var combined = pendingReturns.Concat(pendingComplimentary).Concat(pendingHighPrices).Concat(pendingComplaints)
            .OrderByDescending(x => x.OccurredAtUtc)
            .ToList();

        // نفس مبدأ CLAUDE.md §3.1: نجيب الحقول الخام أول (بلا أي enum
        // .ToString() مترجَم لـSQL)، ثم نبني نص سبب الإلغاء العربي
        // بالذاكرة عبر switch صريح.
        var saleInvoices = ignoreBranchFilter
            ? _context.SaleInvoices.IgnoreQueryFilters().AsNoTracking()
            : _context.SaleInvoices.AsNoTracking();

        var voidedSaleRows = await saleInvoices
            .Where(s => s.Status == SaleInvoiceStatus.Voided && s.ReviewedAtUtc == null)
            .Select(s => new
            {
                s.Id,
                s.InvoiceNumber,
                s.TotalAmount,
                s.BranchId,
                s.VoidedAtUtc,
                s.VoidedByUserId,
                s.VoidReason,
                s.VoidNotes
            })
            .ToListAsync(cancellationToken);

        var voidedByUserIds = voidedSaleRows
            .Where(s => s.VoidedByUserId.HasValue)
            .Select(s => s.VoidedByUserId!.Value)
            .Distinct()
            .ToList();

        var voidedByNameById = await _context.Users.AsNoTracking()
            .Where(u => voidedByUserIds.Contains(u.Id))
            .ToDictionaryAsync(u => u.Id, u => u.FullName, cancellationToken);

        var pendingVoidedSales = voidedSaleRows
            .Select(s => new VoidedSaleReviewDto(
                s.Id,
                s.InvoiceNumber,
                s.VoidReason switch
                {
                    VoidReason.CashierError => "خطأ بالكاشير",
                    VoidReason.CustomerCancelled => "الزبون ألغى الطلب",
                    VoidReason.SystemError => "خطأ نظام",
                    VoidReason.Other => "سبب آخر",
                    _ => "غير محدَّد"
                },
                s.VoidNotes,
                s.TotalAmount,
                // Void() بالـDomain (SaleInvoice.cs) بيضبط VoidedAtUtc دايمًا
                // مع Status = Voided بنفس العملية - ثابت مضمون، لا قيمة افتراضية حقيقية هون.
                s.VoidedAtUtc!.Value,
                s.VoidedByUserId.HasValue && voidedByNameById.TryGetValue(s.VoidedByUserId.Value, out var voidedByName)
                    ? voidedByName
                    : "غير معروف",
                s.BranchId))
            .OrderByDescending(x => x.VoidedAtUtc)
            .ToList();

        return new GetPendingReviewsResponse(combined, combined.Count + pendingVoidedSales.Count, pendingVoidedSales);
    }
}
