using Microsoft.EntityFrameworkCore;
using SupermarketSystem.Application.Common.Interfaces;
using SupermarketSystem.Application.Common.Results;

namespace SupermarketSystem.Application.Ordering.AcceptOrder;

public sealed record AcceptOrderCommand(Guid OrderId);

/// <summary>
/// "قبول" لا يخصم مخزون ولا ينشئ فاتورة - بس يعلّم الكاشير التزم يجهّز
/// الطلب (راجع تعليق Order.cs عن سبب فصل Accepted عن Completed - دفع
/// وقت التسليم، لا وقت القبول).
/// </summary>
public sealed class AcceptOrderHandler
{
    private readonly IApplicationDbContext _context;
    private readonly ICurrentUserContext _currentUser;
    private readonly IDateTimeProvider _dateTimeProvider;
    private readonly ICustomerPushNotifier _pushNotifier;

    public AcceptOrderHandler(
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

    public async Task<Result> HandleAsync(AcceptOrderCommand command, CancellationToken cancellationToken)
    {
        var order = await _context.Orders.FirstOrDefaultAsync(o => o.Id == command.OrderId, cancellationToken);
        if (order is null)
        {
            return Result.Failure(Error.NotFound("Order.NotFound", $"الطلب '{command.OrderId}' غير موجود."));
        }

        var userId = _currentUser.UserId
            ?? throw new InvalidOperationException("لا يمكن قبول طلب بلا هوية مستخدم مصادَق عليها.");

        try
        {
            order.Accept(userId, _dateTimeProvider.UtcNow);
        }
        catch (Domain.Common.DomainException ex)
        {
            return Result.Failure(Error.Conflict("Order.InvalidTransition", ex.Message));
        }

        await _context.SaveChangesAsync(cancellationToken);

        // فشل إرسال الإشعار ما يفشّل قبول الطلب - راجع تعليق ICustomerPushNotifier.
        await _pushNotifier.NotifyOrderStatusChangedAsync(
            order.CustomerId, "تم قبول طلبك ✅", "جاري تجهيز طلبك الآن.", cancellationToken);

        return Result.Success();
    }
}
