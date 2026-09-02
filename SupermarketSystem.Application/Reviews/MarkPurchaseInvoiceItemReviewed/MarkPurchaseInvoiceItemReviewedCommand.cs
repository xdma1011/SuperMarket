using Microsoft.EntityFrameworkCore;
using SupermarketSystem.Application.Common.Interfaces;
using SupermarketSystem.Application.Common.Results;

namespace SupermarketSystem.Application.Reviews.MarkPurchaseInvoiceItemReviewed;

public sealed record MarkPurchaseInvoiceItemReviewedCommand(Guid PurchaseInvoiceItemId);

/// <summary>نفس نمط MarkStockMovementReviewedHandler/MarkReturnReviewedHandler بالضبط، بس لسطر شراء عليه NeedsReview (سعر مرتفع بشكل ملحوظ).</summary>
public sealed class MarkPurchaseInvoiceItemReviewedHandler
{
    private readonly IApplicationDbContext _context;
    private readonly ICurrentUserContext _currentUser;
    private readonly IDateTimeProvider _dateTimeProvider;

    public MarkPurchaseInvoiceItemReviewedHandler(
        IApplicationDbContext context, ICurrentUserContext currentUser, IDateTimeProvider dateTimeProvider)
    {
        _context = context;
        _currentUser = currentUser;
        _dateTimeProvider = dateTimeProvider;
    }

    public async Task<Result> HandleAsync(MarkPurchaseInvoiceItemReviewedCommand command, CancellationToken cancellationToken)
    {
        var item = await _context.PurchaseInvoiceItems
            .FirstOrDefaultAsync(i => i.Id == command.PurchaseInvoiceItemId, cancellationToken);

        if (item is null)
        {
            return Result.Failure(Error.NotFound("PurchaseInvoiceItem.NotFound", $"سطر الفاتورة '{command.PurchaseInvoiceItemId}' غير موجود."));
        }

        var userId = _currentUser.UserId
            ?? throw new InvalidOperationException("لا يمكن تعليم مراجعة بلا هوية مستخدم مصادَق عليها.");

        try
        {
            item.MarkReviewed(userId, _dateTimeProvider.UtcNow);
        }
        catch (Domain.Common.DomainException ex)
        {
            return Result.Failure(Error.Conflict("PurchaseInvoiceItem.AlreadyReviewed", ex.Message));
        }

        await _context.SaveChangesAsync(cancellationToken);
        return Result.Success();
    }
}
