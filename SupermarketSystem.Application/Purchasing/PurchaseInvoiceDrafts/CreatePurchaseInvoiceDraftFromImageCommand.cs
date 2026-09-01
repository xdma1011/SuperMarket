using Microsoft.EntityFrameworkCore;
using SupermarketSystem.Application.Common.Interfaces;
using SupermarketSystem.Application.Common.Results;
using SupermarketSystem.Domain.CashManagement;
using SupermarketSystem.Domain.Payments;
using SupermarketSystem.Domain.Purchasing;

namespace SupermarketSystem.Application.Purchasing.PurchaseInvoiceDrafts;

/// <summary>
/// PaidNowAmount/PaidNowPaymentMethodId اختياريان تمامًا - لو حدا دفع
/// كاش (أو أي طريقة تؤثر بالدرج) للمورد لحظة استلام البضاعة، قبل أي
/// مراجعة. هذا هو الحل الحقيقي لمشكلة توقيت كانت موجودة: الكاش يطلع من
/// الدرج بلحظة الاستلام، لا بلحظة اعتماد المراجع للمسودة لاحقًا - لو ما
/// سجّلناها هون فورًا، تقفيل نفس اليوم بيظهر عجز غير مفسَّر.
/// </summary>
public sealed record CreatePurchaseInvoiceDraftFromImageCommand(
    Guid BranchId, byte[] ImageBytes, string MimeType, decimal? PaidNowAmount, Guid? PaidNowPaymentMethodId);

public sealed record CreatePurchaseInvoiceDraftFromImageResponse(
    Guid DraftId,
    string ImageReference,
    string? ProviderName,
    string? RawSupplierName,
    Guid? MatchedSupplierId,
    string? SupplierInvoiceReference,
    string? InvoiceDate,
    string? Currency,
    decimal? ExtractedInvoiceTotal,
    string? ExtractionConfidence,
    IReadOnlyList<string> Warnings,
    IReadOnlyList<PurchaseInvoiceDraftItemDto> Items,
    decimal? PaidNowAmount);

/// <summary>
/// يرفع صورة، يحفظها (تحفظ حتى لو فشل الاستخراج - نفس سلوك المسار
/// القديم)، يشغّل الذكاء الاصطناعي (Gemini → Gemini Flash → Claude)، وبعدين
/// يحاول يطابق كل سطر مستخرَج بمنتج فعلي بالكتالوج ("سماح مع مراجعة" -
/// نفس فلسفة CLAUDE.md §1.6: لا نوقف الرفع لمجرد إن AI ما عرف الصنف، بس
/// نعلّم السطر "غير مطابَق" ليراجعه المستخدم يدويًا).
///
/// المطابقة بسيطة ومقصودة: نقسّم اسم الصنف المستخرَج لكلمات، ونبحث عن
/// منتج اسمه يحتوي *كل* الكلمات (بأي ترتيب) - "سكر 10 كيلو شعبان" بيطابق
/// منتج اسمه "سكر شعبان 10 كيلو" رغم اختلاف الترتيب. لو طابقت النتيجة
/// منتج واحد بالضبط، تُقبل تلقائيًا؛ صفر أو أكتر من واحد = نتركها فاضية
/// للمراجع يختار يدويًا (لا تخمين، لا قرار خاطئ صامت).
/// </summary>
public sealed class CreatePurchaseInvoiceDraftFromImageHandler
{
    private readonly IApplicationDbContext _context;
    private readonly IImageStorageService _imageStorage;
    private readonly IInvoiceExtractionService _extractionService;
    private readonly ICurrentUserContext _currentUser;
    private readonly IDateTimeProvider _dateTimeProvider;

    public CreatePurchaseInvoiceDraftFromImageHandler(
        IApplicationDbContext context, IImageStorageService imageStorage, IInvoiceExtractionService extractionService,
        ICurrentUserContext currentUser, IDateTimeProvider dateTimeProvider)
    {
        _context = context;
        _imageStorage = imageStorage;
        _extractionService = extractionService;
        _currentUser = currentUser;
        _dateTimeProvider = dateTimeProvider;
    }

    public async Task<Result<CreatePurchaseInvoiceDraftFromImageResponse>> HandleAsync(
        CreatePurchaseInvoiceDraftFromImageCommand command, CancellationToken cancellationToken)
    {
        if (command.PaidNowAmount is { } paidAmount)
        {
            if (paidAmount <= 0)
            {
                return Result.Failure<CreatePurchaseInvoiceDraftFromImageResponse>(
                    Error.Validation("PurchaseInvoiceDraft.PaidNowAmountInvalid", "المبلغ المدفوع الآن يجب أن يكون موجبًا."));
            }

            if (command.PaidNowPaymentMethodId is null)
            {
                return Result.Failure<CreatePurchaseInvoiceDraftFromImageResponse>(
                    Error.Validation("PurchaseInvoiceDraft.PaidNowMethodRequired", "حدّد طريقة الدفع لو دفعت مبلغًا الآن."));
            }
        }

        var branchExists = await _context.Branches.AsNoTracking().AnyAsync(b => b.Id == command.BranchId, cancellationToken);
        if (!branchExists)
        {
            return Result.Failure<CreatePurchaseInvoiceDraftFromImageResponse>(
                Error.NotFound("PurchaseInvoiceDraft.BranchNotFound", $"الفرع '{command.BranchId}' غير موجود."));
        }

        PaymentMethod? paidNowMethod = null;
        if (command.PaidNowPaymentMethodId is { } paidMethodId)
        {
            paidNowMethod = await _context.PaymentMethods.AsNoTracking()
                .FirstOrDefaultAsync(pm => pm.Id == paidMethodId && pm.IsActive, cancellationToken);
            if (paidNowMethod is null)
            {
                return Result.Failure<CreatePurchaseInvoiceDraftFromImageResponse>(
                    Error.NotFound("PurchaseInvoiceDraft.PaidNowMethodNotFound", $"طريقة الدفع '{paidMethodId}' غير موجودة أو غير فعّالة."));
            }
        }

        var storageResult = await _imageStorage.SaveAsWebPAsync(command.ImageBytes, cancellationToken);
        if (storageResult.IsFailure)
        {
            return Result.Failure<CreatePurchaseInvoiceDraftFromImageResponse>(storageResult.Error!);
        }

        var extractionResult = await _extractionService.ExtractAsync(command.ImageBytes, command.MimeType, cancellationToken);
        if (extractionResult.IsFailure)
        {
            return Result.Failure<CreatePurchaseInvoiceDraftFromImageResponse>(extractionResult.Error!);
        }

        var extraction = extractionResult.Value.Extraction;
        var providerName = extractionResult.Value.ProviderName;

        Guid? matchedSupplierId = null;
        if (!string.IsNullOrWhiteSpace(extraction.SupplierName))
        {
            matchedSupplierId = await TryMatchSingleSupplierAsync(extraction.SupplierName, cancellationToken);
        }

        var draftItems = new List<PurchaseInvoiceDraftItemDto>();
        foreach (var rawItem in extraction.Items)
        {
            var matched = await TryMatchSingleProductAsync(rawItem.RawProductName, cancellationToken);

            Guid? matchedProductUnitId = null;
            if (matched is not null)
            {
                matchedProductUnitId = await _context.ProductUnits.AsNoTracking()
                    .Where(u => u.ProductId == matched.Id && u.IsBaseUnit)
                    .Select(u => (Guid?)u.Id)
                    .FirstOrDefaultAsync(cancellationToken);
            }

            draftItems.Add(new PurchaseInvoiceDraftItemDto(
                rawItem.RawProductName,
                rawItem.Quantity,
                rawItem.UnitOfMeasure,
                rawItem.UnitCost,
                rawItem.LineTotal,
                matched?.Id,
                matched?.Name,
                matchedProductUnitId,
                matched?.IsBatchTracked ?? false,
                NewBatchNumber: null,
                NewBatchExpiryDate: null));
        }

        var draft = new PurchaseInvoiceDraft(
            command.BranchId,
            storageResult.Value,
            providerName,
            extraction.SupplierName,
            matchedSupplierId,
            extraction.SupplierInvoiceReference,
            extraction.InvoiceDate,
            extraction.Currency,
            extraction.InvoiceTotal,
            extraction.ExtractionConfidence,
            PurchaseInvoiceDraftItemsSerializer.SerializeWarnings(extraction.Warnings),
            PurchaseInvoiceDraftItemsSerializer.Serialize(draftItems),
            command.PaidNowAmount,
            command.PaidNowPaymentMethodId);

        _context.PurchaseInvoiceDrafts.Add(draft);

        // نفس مبدأ CompleteSaleCommand/RecordPurchaseInvoicePaymentCommand:
        // نعتمد على AffectsCashDrawer (سلوك)، لا اسم/كود طريقة الدفع.
        // هون بالذات الفرق الجوهري: الحركة تُكتب لحظة الرفع نفسها، لا
        // لحظة اعتماد المراجع لاحقًا - لأنه الكاش طلع من الدرج فعليًا الآن.
        if (paidNowMethod is { AffectsCashDrawer: true } && command.PaidNowAmount is { } cashOutAmount)
        {
            var actorUserId = _currentUser.UserId ?? Domain.Identity.User.SystemUserId;
            _context.CashDrawerLogs.Add(new CashDrawerLog(
                command.BranchId,
                CashDrawerMovementType.PurchasePaymentCashOut,
                cashOutAmount,
                CashDrawerReferenceType.PurchaseInvoiceDraft,
                draft.Id,
                actorUserId,
                _dateTimeProvider.UtcNow));
        }

        await _context.SaveChangesAsync(cancellationToken);

        return Result.Success(new CreatePurchaseInvoiceDraftFromImageResponse(
            draft.Id,
            draft.ImageReference,
            providerName,
            extraction.SupplierName,
            matchedSupplierId,
            extraction.SupplierInvoiceReference,
            extraction.InvoiceDate?.ToString("yyyy-MM-dd"),
            extraction.Currency,
            extraction.InvoiceTotal,
            extraction.ExtractionConfidence,
            extraction.Warnings,
            draftItems,
            command.PaidNowAmount));
    }

    private static List<string> TokensOf(string rawName) =>
        rawName.Split(' ', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries).ToList();

    /// <summary>
    /// يجيب مرشحين عبر LIKE بأول كلمة (مترجَم للـSQL)، وبعدين يفلتر البقية
    /// بالذاكرة على المجموعة الصغيرة الناتجة - أبسط بكثير من بناء LIKE
    /// متسلسل ديناميكيًا لكل كلمة، وكافٍ تمامًا لحجم كتالوج/فاتورة عادي.
    /// </summary>
    private async Task<(Guid Id, string Name, bool IsBatchTracked)?> TryMatchSingleProductAsync(
        string rawName, CancellationToken cancellationToken)
    {
        var tokens = TokensOf(rawName);
        if (tokens.Count == 0)
        {
            return null;
        }

        var pattern = $"%{tokens[0]}%";
        var candidates = await _context.Products.AsNoTracking()
            .Where(p => EF.Functions.Like(p.Name, pattern))
            .Select(p => new { p.Id, p.Name, p.IsBatchTracked })
            .ToListAsync(cancellationToken);

        var matches = candidates
            .Where(c => tokens.All(t => c.Name.Contains(t, StringComparison.OrdinalIgnoreCase)))
            .ToList();

        return matches.Count == 1 ? (matches[0].Id, matches[0].Name, matches[0].IsBatchTracked) : null;
    }

    private async Task<Guid?> TryMatchSingleSupplierAsync(string rawName, CancellationToken cancellationToken)
    {
        var tokens = TokensOf(rawName);
        if (tokens.Count == 0)
        {
            return null;
        }

        var pattern = $"%{tokens[0]}%";
        var candidates = await _context.Suppliers.AsNoTracking()
            .Where(s => EF.Functions.Like(s.Name, pattern))
            .Select(s => new { s.Id, s.Name })
            .ToListAsync(cancellationToken);

        var matches = candidates
            .Where(c => tokens.All(t => c.Name.Contains(t, StringComparison.OrdinalIgnoreCase)))
            .ToList();

        return matches.Count == 1 ? matches[0].Id : null;
    }
}
