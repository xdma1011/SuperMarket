using Microsoft.EntityFrameworkCore;
using SupermarketSystem.Application.Common.Interfaces;
using SupermarketSystem.Application.Common.Results;
using SupermarketSystem.Application.Purchasing.CompletePurchaseInvoice;
using SupermarketSystem.Domain.Purchasing;

namespace SupermarketSystem.Application.Purchasing.PurchaseInvoiceDrafts;

public sealed record CompletePurchaseInvoiceDraftCommand(Guid DraftId);

/// <summary>
/// اعتماد نهائي - يحوّل مسودة PendingReview لفاتورة شراء فعلية، بإعادة
/// استخدام CompletePurchaseInvoiceHandler نفسه (نفس منطق المخزون/الدفعات/
/// التسلسل - صفر تكرار منطق مالي). يرفض الاعتماد لو أي سطر لسا بلا
/// منتج مطابَق أو المورد غير محدَّد - المراجعة يجب تكتمل فعليًا، لا
/// اعتماد جزئي بفاتورة ناقصة.
/// </summary>
public sealed class CompletePurchaseInvoiceDraftHandler
{
    private readonly IApplicationDbContext _context;
    private readonly CompletePurchaseInvoiceHandler _completeInvoiceHandler;

    public CompletePurchaseInvoiceDraftHandler(IApplicationDbContext context, CompletePurchaseInvoiceHandler completeInvoiceHandler)
    {
        _context = context;
        _completeInvoiceHandler = completeInvoiceHandler;
    }

    public async Task<Result<CompletePurchaseInvoiceResponse>> HandleAsync(
        CompletePurchaseInvoiceDraftCommand command, CancellationToken cancellationToken)
    {
        var draft = await _context.PurchaseInvoiceDrafts
            .FirstOrDefaultAsync(d => d.Id == command.DraftId, cancellationToken);

        if (draft is null)
        {
            return Result.Failure<CompletePurchaseInvoiceResponse>(
                Error.NotFound("PurchaseInvoiceDraft.NotFound", $"مسودة الفاتورة '{command.DraftId}' غير موجودة."));
        }

        if (draft.Status != PurchaseInvoiceDraftStatus.PendingReview)
        {
            return Result.Failure<CompletePurchaseInvoiceResponse>(Error.BusinessRule(
                "PurchaseInvoiceDraft.NotPendingReview", "لا يمكن اعتماد مسودة فاتورة تم اعتمادها أو تجاهلها مسبقًا."));
        }

        if (draft.MatchedSupplierId is not { } supplierId)
        {
            return Result.Failure<CompletePurchaseInvoiceResponse>(Error.Validation(
                "PurchaseInvoiceDraft.SupplierNotMatched", "لازم تحديد المورد الصحيح قبل الاعتماد."));
        }

        var items = PurchaseInvoiceDraftItemsSerializer.Deserialize(draft.ItemsJson);
        if (items.Count == 0)
        {
            return Result.Failure<CompletePurchaseInvoiceResponse>(
                Error.Validation("PurchaseInvoiceDraft.ItemsRequired", "لازم سطر واحد على الأقل."));
        }

        var unmatchedItem = items.FirstOrDefault(i => i.MatchedProductId is null || i.MatchedProductUnitId is null);
        if (unmatchedItem is not null)
        {
            return Result.Failure<CompletePurchaseInvoiceResponse>(Error.Validation(
                "PurchaseInvoiceDraft.ItemNotMatched",
                $"الصنف \"{unmatchedItem.RawProductName}\" لسا غير مطابَق بمنتج فعلي - اختره يدويًا قبل الاعتماد."));
        }

        var completeItems = items.Select(i =>
        {
            DateOnly? expiry = DateOnly.TryParse(i.NewBatchExpiryDate, out var parsedExpiry) ? parsedExpiry : null;

            return new CompletePurchaseInvoiceItemDto(
                i.MatchedProductId!.Value,
                i.MatchedProductUnitId!.Value,
                i.Quantity,
                i.UnitCost ?? 0,
                ExistingProductBatchId: null,
                NewBatchNumber: i.NewBatchNumber,
                NewBatchExpiryDate: expiry);
        }).ToList();

        var completeCommand = new CompletePurchaseInvoiceCommand(
            draft.BranchId,
            supplierId,
            draft.SupplierInvoiceReference,
            completeItems,
            ImageReferences: new[] { draft.ImageReference });

        var completeResult = await _completeInvoiceHandler.HandleAsync(completeCommand, cancellationToken);
        if (completeResult.IsFailure)
        {
            return completeResult;
        }

        draft.MarkCompleted(completeResult.Value.PurchaseInvoiceId);
        await _context.SaveChangesAsync(cancellationToken);

        return completeResult;
    }
}
