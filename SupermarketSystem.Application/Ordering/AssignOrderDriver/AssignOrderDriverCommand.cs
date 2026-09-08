using Microsoft.EntityFrameworkCore;
using SupermarketSystem.Application.Common.Interfaces;
using SupermarketSystem.Application.Common.Results;

namespace SupermarketSystem.Application.Ordering.AssignOrderDriver;

public sealed record AssignOrderDriverCommand(Guid OrderId, Guid DriverId);

/// <summary>يسندها الكاشير/الفرع (Sales.Create) للطلب بعد قبوله - يحدّد مين طلع فيها للتوصيل. الطلب لازم يكون Accepted (راجع Order.AssignDriver).</summary>
public sealed class AssignOrderDriverHandler
{
    private readonly IApplicationDbContext _context;
    private readonly IDateTimeProvider _dateTimeProvider;

    public AssignOrderDriverHandler(IApplicationDbContext context, IDateTimeProvider dateTimeProvider)
    {
        _context = context;
        _dateTimeProvider = dateTimeProvider;
    }

    public async Task<Result> HandleAsync(AssignOrderDriverCommand command, CancellationToken cancellationToken)
    {
        var order = await _context.Orders.FirstOrDefaultAsync(o => o.Id == command.OrderId, cancellationToken);
        if (order is null)
        {
            return Result.Failure(Error.NotFound("Order.NotFound", $"الطلب '{command.OrderId}' غير موجود."));
        }

        var driverExists = await _context.Users.AsNoTracking()
            .AnyAsync(u => u.Id == command.DriverId && u.IsActive && !u.IsDeleted, cancellationToken);

        if (!driverExists)
        {
            return Result.Failure(Error.NotFound("Driver.NotFound", "السائق غير موجود أو غير فعّال."));
        }

        try
        {
            order.AssignDriver(command.DriverId, _dateTimeProvider.UtcNow);
        }
        catch (Domain.Common.DomainException ex)
        {
            return Result.Failure(Error.Conflict("Order.InvalidTransition", ex.Message));
        }

        await _context.SaveChangesAsync(cancellationToken);
        return Result.Success();
    }
}
