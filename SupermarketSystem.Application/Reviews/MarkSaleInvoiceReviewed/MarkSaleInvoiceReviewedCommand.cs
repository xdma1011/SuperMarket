using Microsoft.EntityFrameworkCore;
using SupermarketSystem.Application.Common.Interfaces;
using SupermarketSystem.Application.Common.Results;

namespace SupermarketSystem.Application.Reviews.MarkSaleInvoiceReviewed;

public sealed record MarkSaleInvoiceReviewedCommand(Guid SaleInvoiceId);

/// <summary>نفس نمط MarkStockMovementReviewedHandler بالضبط، بس لفواتير البيع الملغاة (VoidSale) بصفحة المراجعات.</summary>
public sealed class MarkSaleInvoiceReviewedHandler
{
    private readonly IApplicationDbContext _context;
    private readonly ICurrentUserContext _currentUser;
    private readonly IDateTimeProvider _dateTimeProvider;

    public MarkSaleInvoiceReviewedHandler(
        IApplicationDbContext context, ICurrentUserContext currentUser, IDateTimeProvider dateTimeProvider)
    {
        _context = context;
        _currentUser = currentUser;
        _dateTimeProvider = dateTimeProvider;
    }

    public async Task<Result> HandleAsync(MarkSaleInvoiceReviewedCommand command, CancellationToken cancellationToken)
    {
        var saleInvoice = await _context.SaleInvoices
            .FirstOrDefaultAsync(s => s.Id == command.SaleInvoiceId, cancellationToken);

        if (saleInvoice is null)
        {
            return Result.Failure(Error.NotFound("SaleInvoice.NotFound", $"فاتورة البيع '{command.SaleInvoiceId}' غير موجودة."));
        }

        var userId = _currentUser.UserId
            ?? throw new InvalidOperationException("لا يمكن تعليم مراجعة بلا هوية مستخدم مصادَق عليها.");

        try
        {
            saleInvoice.MarkReviewed(userId, _dateTimeProvider.UtcNow);
        }
        catch (Domain.Common.DomainException ex)
        {
            return Result.Failure(Error.Conflict("SaleInvoice.AlreadyReviewed", ex.Message));
        }

        await _context.SaveChangesAsync(cancellationToken);
        return Result.Success();
    }
}
