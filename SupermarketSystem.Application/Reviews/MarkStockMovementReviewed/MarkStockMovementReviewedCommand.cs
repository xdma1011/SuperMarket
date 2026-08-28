using Microsoft.EntityFrameworkCore;
using SupermarketSystem.Application.Common.Interfaces;
using SupermarketSystem.Application.Common.Results;

namespace SupermarketSystem.Application.Reviews.MarkStockMovementReviewed;

public sealed record MarkStockMovementReviewedCommand(Guid StockMovementId);

/// <summary>نفس نمط MarkReturnReviewedHandler بالضبط، بس لحركات المخزون (الضيافة حاليًا).</summary>
public sealed class MarkStockMovementReviewedHandler
{
    private readonly IApplicationDbContext _context;
    private readonly ICurrentUserContext _currentUser;
    private readonly IDateTimeProvider _dateTimeProvider;

    public MarkStockMovementReviewedHandler(
        IApplicationDbContext context, ICurrentUserContext currentUser, IDateTimeProvider dateTimeProvider)
    {
        _context = context;
        _currentUser = currentUser;
        _dateTimeProvider = dateTimeProvider;
    }

    public async Task<Result> HandleAsync(MarkStockMovementReviewedCommand command, CancellationToken cancellationToken)
    {
        var movement = await _context.StockMovements
            .FirstOrDefaultAsync(m => m.Id == command.StockMovementId, cancellationToken);

        if (movement is null)
        {
            return Result.Failure(Error.NotFound("StockMovement.NotFound", $"الحركة '{command.StockMovementId}' غير موجودة."));
        }

        var userId = _currentUser.UserId
            ?? throw new InvalidOperationException("لا يمكن تعليم مراجعة بلا هوية مستخدم مصادَق عليها.");

        try
        {
            movement.MarkReviewed(userId, _dateTimeProvider.UtcNow);
        }
        catch (Domain.Common.DomainException ex)
        {
            return Result.Failure(Error.Conflict("StockMovement.AlreadyReviewed", ex.Message));
        }

        await _context.SaveChangesAsync(cancellationToken);
        return Result.Success();
    }
}
