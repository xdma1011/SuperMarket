using Microsoft.EntityFrameworkCore;
using SupermarketSystem.Application.Common.Interfaces;
using SupermarketSystem.Application.Common.Results;

namespace SupermarketSystem.Application.Ordering.RateOrder;

/// <summary>⚠️ نفس تحذير PlaceOrderCommand - بلا تحقق هوية حقيقي بعد. مؤشر رضا سريع بس، لا يؤثر على أي عملية أخرى.</summary>
public sealed record RateOrderCommand(Guid OrderId, int Rating, string? Comment);

public sealed class RateOrderHandler
{
    private readonly IApplicationDbContext _context;

    public RateOrderHandler(IApplicationDbContext context)
    {
        _context = context;
    }

    public async Task<Result> HandleAsync(RateOrderCommand command, CancellationToken cancellationToken)
    {
        var order = await _context.Orders.FirstOrDefaultAsync(o => o.Id == command.OrderId, cancellationToken);
        if (order is null)
        {
            return Result.Failure(Error.NotFound("Order.NotFound", $"الطلب '{command.OrderId}' غير موجود."));
        }

        try
        {
            order.Rate(command.Rating, command.Comment?.Trim());
        }
        catch (Domain.Common.DomainException ex)
        {
            return Result.Failure(Error.Validation("Order.InvalidRating", ex.Message));
        }

        await _context.SaveChangesAsync(cancellationToken);
        return Result.Success();
    }
}
