using Microsoft.EntityFrameworkCore;
using SupermarketSystem.Application.Common.Interfaces;
using SupermarketSystem.Application.Common.Results;
using SupermarketSystem.Domain.Purchasing;

namespace SupermarketSystem.Application.Purchasing.PurchaseInvoiceDrafts;

public sealed record UpdatePurchaseInvoiceDraftCommand(
    Guid DraftId,
    Guid? MatchedSupplierId,
    string? SupplierInvoiceReference,
    IReadOnlyList<PurchaseInvoiceDraftItemDto> Items);

/// <summary>
/// هذا هو "التعديل" اللي المراجع بيسويه قبل الاعتماد - يختار المنتج
/// الصحيح لكل سطر ما انطابق تلقائيًا، يصحح كمية/سعر، يضيف رقم دفعة لو
/// الصنف متتبَّع. لا يلمس المخزون ولا التكلفة إطلاقًا - بس تحديث نص JSON
/// بمسودة لسا PendingReview.
/// </summary>
public sealed class UpdatePurchaseInvoiceDraftHandler
{
    private readonly IApplicationDbContext _context;

    public UpdatePurchaseInvoiceDraftHandler(IApplicationDbContext context)
    {
        _context = context;
    }

    public async Task<Result> HandleAsync(UpdatePurchaseInvoiceDraftCommand command, CancellationToken cancellationToken)
    {
        var draft = await _context.PurchaseInvoiceDrafts
            .FirstOrDefaultAsync(d => d.Id == command.DraftId, cancellationToken);

        if (draft is null)
        {
            return Result.Failure(Error.NotFound("PurchaseInvoiceDraft.NotFound", $"مسودة الفاتورة '{command.DraftId}' غير موجودة."));
        }

        if (draft.Status != PurchaseInvoiceDraftStatus.PendingReview)
        {
            return Result.Failure(Error.BusinessRule(
                "PurchaseInvoiceDraft.NotPendingReview", "لا يمكن تعديل مسودة فاتورة تم اعتمادها أو تجاهلها."));
        }

        if (command.Items.Count == 0)
        {
            return Result.Failure(Error.Validation("PurchaseInvoiceDraft.ItemsRequired", "لازم سطر واحد على الأقل."));
        }

        try
        {
            draft.UpdateForReview(
                command.MatchedSupplierId,
                command.SupplierInvoiceReference,
                PurchaseInvoiceDraftItemsSerializer.Serialize(command.Items));
        }
        catch (Domain.Common.DomainException ex)
        {
            return Result.Failure(Error.BusinessRule("PurchaseInvoiceDraft.UpdateFailed", ex.Message));
        }

        await _context.SaveChangesAsync(cancellationToken);

        return Result.Success();
    }
}
