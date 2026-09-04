using Microsoft.EntityFrameworkCore;
using SupermarketSystem.Application.Common.Interfaces;
using SupermarketSystem.Application.Common.Results;

namespace SupermarketSystem.Application.Ordering.RejectOrder;

public sealed record RejectOrderCommand(Guid OrderId, string Reason);

/// <summary>سبب إلزامي دائمًا (قرار صاحب المشروع صراحة) - يخلي مراقبة "هل الكاشير يرفض بلا مبرر" ممكنة فعليًا.</summary>
public sealed class RejectOrderHandler
{
    private readonly IApplicationDbContext _context;
    private readonly ICurrentUserContext _currentUser;
    private readonly IDateTimeProvider _dateTimeProvider;
    private readonly ICustomerPushNotifier _pushNotifier;

    public RejectOrderHandler(
        IApplicationDbContext context,
        ICurrentUserContext currentUser,
        IDateTimeProvider dateTimeProvider,
        ICustomerPushNotifier pushNotifier)
    {
        _context = context;
        _currentUser = currentUser;
        _dateTimeProvider = dateTimeProvider;
        _pushNotifier = pushNotifier;
    }

    public async Task<Result> HandleAsync(RejectOrderCommand command, CancellationToken cancellationToken)
    {
        if (string.IsNullOrWhiteSpace(command.Reason))
        {
            return Result.Failure(Error.Validation("Order.RejectionReasonRequired", "سبب الرفض إلزامي."));
        }

        var order = await _context.Orders.FirstOrDefaultAsync(o => o.Id == command.OrderId, cancellationToken);
        if (order is null)
        {
            return Result.Failure(Error.NotFound("Order.NotFound", $"الطلب '{command.OrderId}' غير موجود."));
        }

        var userId = _currentUser.UserId
            ?? throw new InvalidOperationException("لا يمكن رفض طلب بلا هوية مستخدم مصادَق عليها.");

        try
        {
            order.Reject(userId, _dateTimeProvider.UtcNow, command.Reason.Trim());
        }
        catch (Domain.Common.DomainException ex)
        {
            return Result.Failure(Error.Conflict("Order.InvalidTransition", ex.Message));
        }

        await _context.SaveChangesAsync(cancellationToken);

        await _pushNotifier.NotifyOrderStatusChangedAsync(
            order.CustomerId, "تم رفض طلبك", $"السبب: {command.Reason.Trim()}", cancellationToken);

        return Result.Success();
    }
}
