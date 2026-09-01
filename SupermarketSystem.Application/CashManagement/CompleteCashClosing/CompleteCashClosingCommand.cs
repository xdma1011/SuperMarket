using Microsoft.EntityFrameworkCore;
using SupermarketSystem.Application.Common.Interfaces;
using SupermarketSystem.Application.Common.Results;
using SupermarketSystem.Domain.CashManagement;
using SupermarketSystem.Domain.Identity;
using SupermarketSystem.Domain.Payments;

namespace SupermarketSystem.Application.CashManagement.CompleteCashClosing;

// المبلغ المعدود فعليًا (يدويًا من الكاشير/المدير) لطريقة دفع معيّنة —
// عادةً هذا بيكون بس لـ"Cash" (العدّ الفعلي للدرج). باقي طرق الدفع
// (فيزا، CliQ) بتُترك بلا عدّ لحد ما يصير تكامل فعلي مع كشف البنك
// أو جهاز الدفع (§16.15 بالتصميم المعتمد) — مش خطأ ولا نقص، قرار مقصود.
public sealed record CompleteCashClosingCountDto(Guid PaymentMethodId, decimal CountedAmount);

public sealed record CompleteCashClosingCommand(
    Guid BranchId,
    // اليوم/الوردية التجارية اللي هذا التقفيل بيغطيها — يُحدَّد صراحة من
    // المستخدم، ما يُشتق من وقت التقفيل نفسه (نفس مبدأ BusinessDate
    // بـCashClosing، شفناه سابقًا بإصلاح unique index).
    DateOnly BusinessDate,
    // إجمالي الكاش المعدود فعليًا بالدرج وقت التقفيل.
    decimal CountedCash,
    // عدّ اختياري لأي طريقة دفع تانية (فارغة = ما تم عدّها، طبيعي لفيزا/CliQ).
    IReadOnlyList<CompleteCashClosingCountDto> CountedDetails);

public sealed record CompleteCashClosingDetailResponseDto(
    Guid PaymentMethodId,
    string PaymentMethodName,
    // المبلغ المتوقع حسب النظام (من فواتير البيع والإرجاع المكتملة خلال الفترة).
    decimal ExpectedAmount,
    // المبلغ المعدود يدويًا، لو انعطى؛ null لو ما تم عدّه.
    decimal? CountedAmount,
    // الفرق (معدود - متوقع)؛ null لو ما في عدّ أصلًا.
    decimal? Variance);

public sealed record CompleteCashClosingResponse(
    Guid CashClosingId,
    Guid BranchId,
    DateOnly BusinessDate,
    // متوقع الكاش الفعلي بالدرج — محسوب من CashDrawerLog (السجل التاريخي
    // الكامل)، مش من فواتير البيع فقط — لأنه في حركات تانية بتأثر عالدرج
    // بلا ما تكون فاتورة بيع أصلًا (سحب/إيداع يدوي PayIn/PayOut، عكس دفعة
    // بسبب إلغاء).
    decimal ExpectedCash,
    decimal CountedCash,
    decimal Variance,
    IReadOnlyList<CompleteCashClosingDetailResponseDto> Details);

public static class CashClosingSettingsKeys
{
    /// <summary>
    /// فرق التقفيل (|CountedCash - ExpectedCash|) اللي فوقه يُرسَل تنبيه —
    /// التقفيل نفسه ما بيتأثر ولا بيُرفض، بس المدير يوصله تنبيه فوري بدل
    /// ما يكتشف الفرق لما يفتح التقرير صدفة. 0 = تعطيل التنبيه (أي فرق
    /// موجود أصلًا بالتقرير، بلا حاجة نضاعف الإشعار).
    /// </summary>
    public const string VarianceAlertThreshold = "CashClosing.VarianceAlertThreshold";
}

public static class CompleteCashClosingValidator
{
    public static Error? Validate(CompleteCashClosingCommand command)
    {
        if (command.BranchId == Guid.Empty)
        {
            return Error.Validation("CashClosing.BranchRequired", "فرع مطلوب.");
        }

        if (command.BusinessDate == default)
        {
            return Error.Validation("CashClosing.BusinessDateRequired", "تاريخ اليوم التجاري مطلوب.");
        }

        if (command.CountedCash < 0)
        {
            return Error.Validation("CashClosing.CountedCashNegative", "المبلغ المعدود لا يمكن أن يكون سالبًا.");
        }

        foreach (var detail in command.CountedDetails)
        {
            if (detail.PaymentMethodId == Guid.Empty)
            {
                return Error.Validation("CashClosing.PaymentMethodRequired", "طريقة الدفع مطلوبة لكل بند عدّ.");
            }

            if (detail.CountedAmount < 0)
            {
                return Error.Validation("CashClosing.CountedAmountNegative", "المبلغ المعدود لا يمكن أن يكون سالبًا.");
            }
        }

        if (command.CountedDetails.Select(d => d.PaymentMethodId).Distinct().Count() != command.CountedDetails.Count)
        {
            return Error.Validation("CashClosing.DuplicatePaymentMethod", "لا يمكن تكرار نفس طريقة الدفع أكثر من مرة بنفس الطلب.");
        }

        return null;
    }
}

/// <summary>
/// تقفيل الصندوق اليومي — يقارن "المتوقع حسب النظام" بـ"المعدود فعليًا"،
/// ويسجّل الفرق (عجز أو زيادة) بدون ما يمنع أي شيء لاحقًا — نفس فلسفة
/// النظام كله: نسجّل، لا نمنع.
///
/// ═══════════════════════════════════════════════════════════════════
/// نقطتان مهمتان قررتهم أثناء البناء، لازم توثقوا:
/// ═══════════════════════════════════════════════════════════════════
///
/// 1) "المتوقع" بالرأس (ExpectedCash) مختلف عن "المتوقع" ببند الكاش
///    داخل Details — عن قصد، مش تكرار غلط:
///
///    - ExpectedCash (الرأس) = مجموع كل حركات CashDrawerLog الموقّعة
///      (بيع كاش +, إرجاع كاش -, عكس دفعة +/-, سحب/إيداع يدوي +/-).
///      هذا هو "قديش المفروض يكون بالدرج فعليًا" — بيشمل كل شي أثّر
///      عالدرج الفعلي، حتى لو ما إله علاقة مباشرة بفاتورة بيع (مثلًا
///      سحب يدوي PayOut لمصروف).
///
///    - بند "Cash" داخل Details = مجموع دفعات البيع بطريقة الدفع "Cash"
///      ناقص دفعات الإرجاع بنفس الطريقة، من SaleInvoicePayment/
///      ReturnInvoicePayment مباشرة. هذا "قديش المفروض دخل كاش من
///      المبيعات تحديدًا" — بلا السحب/الإيداع اليدوي.
///
///    الاثنان ممكن يختلفوا بشكل طبيعي وسليم (لو صار PayOut لمصروف
///    مثلًا)، وهذا مقصود — لو اختلفوا بشكل كبير بلا تفسير واضح
///    (PayIn/PayOut/إلغاء)، هذا بالضبط الإشارة اللي التقفيل موجود
///    عشان يكشفها.
///
/// 2) الفجوة اللي لقيتها ولازم أذكرها: CashDrawerReferenceType ما كان
///    فيه قيمة تشير لـCashClosing نفسها (بس SaleInvoicePayment/
///    ReturnInvoicePayment/ManualAdjustment). يعني ما كان في طريقة
///    نسجّل حركة "سحب الكاش المعدود من الدرج وقت التقفيل" بشكل مربوط
///    فعليًا بالتقفيل. ضفت قيمة جديدة (CashClosing) بالـenum — إضافة
///    بسيطة وآمنة (مش تعديل على قيم موجودة).
/// ═══════════════════════════════════════════════════════════════════
/// </summary>
public sealed class CompleteCashClosingHandler
{
    private readonly IApplicationDbContext _context;
    private readonly ICurrentUserContext _currentUser;
    private readonly IDateTimeProvider _dateTimeProvider;
    private readonly ISettingsProvider _settingsProvider;
    private readonly INotificationDispatcher _notificationDispatcher;

    public CompleteCashClosingHandler(
        IApplicationDbContext context,
        ICurrentUserContext currentUser,
        IDateTimeProvider dateTimeProvider,
        ISettingsProvider settingsProvider,
        INotificationDispatcher notificationDispatcher)
    {
        _context = context;
        _currentUser = currentUser;
        _dateTimeProvider = dateTimeProvider;
        _settingsProvider = settingsProvider;
        _notificationDispatcher = notificationDispatcher;
    }

    public async Task<Result<CompleteCashClosingResponse>> HandleAsync(
        CompleteCashClosingCommand command,
        CancellationToken cancellationToken)
    {
        var validationError = CompleteCashClosingValidator.Validate(command);
        if (validationError is not null)
        {
            return Result.Failure<CompleteCashClosingResponse>(validationError);
        }

        var branchExists = await _context.Branches.AsNoTracking()
            .AnyAsync(b => b.Id == command.BranchId, cancellationToken);
        if (!branchExists)
        {
            return Result.Failure<CompleteCashClosingResponse>(
                Error.NotFound("CashClosing.BranchNotFound", $"الفرع '{command.BranchId}' غير موجود."));
        }

        // فحص أوّلي ودّي — الحارس الحقيقي هو الـunique index على
        // (BranchId, BusinessDate) بقاعدة البيانات؛ هذا الفحص بس لإرجاع
        // خطأ واضح بدل استثناء SQL خام لو صار سباق تزامن نادر.
        var alreadyClosed = await _context.CashClosings.AsNoTracking()
            .AnyAsync(c => c.BranchId == command.BranchId && c.BusinessDate == command.BusinessDate, cancellationToken);
        if (alreadyClosed)
        {
            return Result.Failure<CompleteCashClosingResponse>(
                Error.Conflict("CashClosing.AlreadyClosed", $"يوجد تقفيل مسبق لهذا الفرع بتاريخ {command.BusinessDate}."));
        }

        // === تحديد بداية الفترة: نهاية آخر تقفيل لنفس الفرع (لو وُجد) ===
        // null يعني "أول تقفيل إطلاقًا لهذا الفرع" — الفترة بتشمل كل شيء
        // من البداية.
        var previousClosedAtUtc = await _context.CashClosings.AsNoTracking()
            .Where(c => c.BranchId == command.BranchId)
            .OrderByDescending(c => c.ClosedAtUtc)
            .Select(c => (DateTime?)c.ClosedAtUtc)
            .FirstOrDefaultAsync(cancellationToken);

        // === حساب "متوقع الكاش الفعلي بالدرج" من CashDrawerLog ===
        // بنسحب الصفوف للذاكرة (عدد محدود — حركات وردية وحدة، مش ملايين
        // سطر) ونجمعها بمنطق C# — أوضح وأأمن من ترجمة switch لجملة SQL،
        // وما في مشكلة أداء لأنه هذا استعلام نادر (مرة كل إغلاق وردية).
        var cashMovements = await _context.CashDrawerLogs.AsNoTracking()
            .Where(c => c.BranchId == command.BranchId
                        && (previousClosedAtUtc == null || c.OccurredAtUtc > previousClosedAtUtc))
            .Select(c => new { c.MovementType, c.Amount })
            .ToListAsync(cancellationToken);

        var expectedCash = cashMovements.Sum(m => GetSignedDirection(m.MovementType) * m.Amount);

        // === تحديد طرق الدفع اللي لازم تظهر بالتفصيل (Details) ===
        // الاتحاد بين: (أ) طرق الدفع الفعّالة حاليًا، و(ب) أي طريقة كان
        // فيها حركة فعلية بالفترة حتى لو تم تعطيلها بعدين — عشان لو طريقة
        // انعطّلت بمنتصف الوردية، مبيعاتها ما تختفي من تقرير التقفيل.
        var activeMethodIds = await _context.PaymentMethods.AsNoTracking()
            .Where(pm => pm.IsActive)
            .Select(pm => pm.Id)
            .ToListAsync(cancellationToken);

        var salesQuery = _context.SaleInvoicePayments.AsNoTracking()
            .Where(p => p.BranchId == command.BranchId && p.Status == PaymentStatus.Completed
                        && (previousClosedAtUtc == null || p.CreatedAtUtc > previousClosedAtUtc));

        var refundsQuery = _context.ReturnInvoicePayments.AsNoTracking()
            .Where(p => p.BranchId == command.BranchId && p.Status == PaymentStatus.Completed
                        && (previousClosedAtUtc == null || p.CreatedAtUtc > previousClosedAtUtc));

        var methodIdsWithSales = await salesQuery.Select(p => p.PaymentMethodId).Distinct().ToListAsync(cancellationToken);
        var methodIdsWithRefunds = await refundsQuery.Select(p => p.PaymentMethodId).Distinct().ToListAsync(cancellationToken);

        var relevantMethodIds = activeMethodIds
            .Union(methodIdsWithSales)
            .Union(methodIdsWithRefunds)
            .Distinct()
            .ToList();

        var methodNames = await _context.PaymentMethods.AsNoTracking()
            .Where(pm => relevantMethodIds.Contains(pm.Id))
            .Select(pm => new { pm.Id, pm.Name })
            .ToDictionaryAsync(pm => pm.Id, pm => pm.Name, cancellationToken);

        var countedByMethod = command.CountedDetails.ToDictionary(d => d.PaymentMethodId, d => d.CountedAmount);

        // === بناء رأس التقفيل + التفاصيل ===
        var actorUserId = _currentUser.UserId ?? User.SystemUserId;
        var closedAtUtc = _dateTimeProvider.UtcNow;

        var cashClosing = new CashClosing(
            command.BranchId, actorUserId, command.BusinessDate, closedAtUtc, expectedCash, command.CountedCash);

        var responseDetails = new List<CompleteCashClosingDetailResponseDto>();

        foreach (var methodId in relevantMethodIds)
        {
            var expectedForMethod =
                await salesQuery.Where(p => p.PaymentMethodId == methodId).SumAsync(p => (decimal?)p.Amount, cancellationToken) ?? 0m
                - (await refundsQuery.Where(p => p.PaymentMethodId == methodId).SumAsync(p => (decimal?)p.Amount, cancellationToken) ?? 0m);

            var countedForMethod = countedByMethod.TryGetValue(methodId, out var counted) ? counted : (decimal?)null;

            cashClosing.AddDetail(methodId, expectedForMethod, countedForMethod);

            responseDetails.Add(new CompleteCashClosingDetailResponseDto(
                methodId,
                methodNames.GetValueOrDefault(methodId, "(غير معروف)"),
                expectedForMethod,
                countedForMethod,
                countedForMethod.HasValue ? countedForMethod.Value - expectedForMethod : null));
        }

        _context.CashClosings.Add(cashClosing);

        // === تسجيل حركة "سحب الكاش المعدود من الدرج" — للتدقيق فقط ===
        // بلا هذه الحركة، سجل CashDrawerLog كان رح يضل ناقص خطوة مهمة:
        // الكاش المعدود بينسحب فعليًا من الدرج (لخزنة أو بنك)، والوردية
        // الجاية بتبلش من درج فاضي + إيداع افتتاحي جديد (DrawerOpen،
        // عملية منفصلة، مش جزء من هالتقفيل). لو ما سجّلنا هذا، أي حد رجع
        // يشوف CashDrawerLog بعدين ما رح يلاقي أثر لوين راح الكاش المعدود.
        //
        // يُسجَّل فقط لو في مبلغ فعلي معدود (> 0) — لأنه CashDrawerLog
        // نفسه بيرفض مبلغ صفر أو سالب (قيد موجود بالـDomain أصلًا).
        if (command.CountedCash > 0)
        {
            var drawerCloseLog = new CashDrawerLog(
                command.BranchId,
                CashDrawerMovementType.DrawerClose,
                command.CountedCash,
                CashDrawerReferenceType.CashClosing,
                cashClosing.Id,
                actorUserId,
                closedAtUtc);

            _context.CashDrawerLogs.Add(drawerCloseLog);
        }

        await _context.SaveChangesAsync(cancellationToken);

        // === تنبيه عند فرق تقفيل كبير — بعد نجاح الحفظ فعليًا ===
        var varianceThreshold = await _settingsProvider.GetDecimalAsync(
            CashClosingSettingsKeys.VarianceAlertThreshold, defaultValue: 0m, cancellationToken);

        if (varianceThreshold > 0 && Math.Abs(cashClosing.Variance) > varianceThreshold)
        {
            var direction = cashClosing.Variance < 0 ? "عجز" : "زيادة";
            await _notificationDispatcher.NotifyAsync(
                $"فرق تقفيل صندوق — {direction}",
                $"الفرع: {command.BranchId}\nالتاريخ: {command.BusinessDate}\nالفرق: {cashClosing.Variance:F2} (متوقع {cashClosing.ExpectedCash:F2}، معدود {cashClosing.CountedCash:F2})",
                cancellationToken);
        }

        return Result.Success(new CompleteCashClosingResponse(
            cashClosing.Id,
            cashClosing.BranchId,
            cashClosing.BusinessDate,
            cashClosing.ExpectedCash,
            cashClosing.CountedCash,
            cashClosing.Variance,
            responseDetails));
    }

    /// <summary>
    /// اتجاه كل نوع حركة (+1 دخول كاش للدرج، -1 خروج). Amount بالـ
    /// Domain دايمًا موجب (الاتجاه معبَّر عنه بالـMovementType نفسه لا
    /// بالإشارة) — هون بنترجم النوع لإشارة حسابية.
    /// </summary>
    private static int GetSignedDirection(CashDrawerMovementType movementType) => movementType switch
    {
        CashDrawerMovementType.SaleCashIn => 1,
        CashDrawerMovementType.ReturnCashOut => -1,
        CashDrawerMovementType.PaymentReversalCashOut => -1,
        CashDrawerMovementType.PaymentReversalCashIn => 1,
        CashDrawerMovementType.PayIn => 1,
        CashDrawerMovementType.PayOut => -1,
        CashDrawerMovementType.DrawerOpen => 1,
        CashDrawerMovementType.DrawerClose => -1,
        CashDrawerMovementType.PurchasePaymentCashOut => -1,
        _ => throw new InvalidOperationException($"قيمة CashDrawerMovementType غير معروفة: {movementType}")
    };
}
