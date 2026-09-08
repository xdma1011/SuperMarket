using Microsoft.EntityFrameworkCore;
using SupermarketSystem.Application.Common.Interfaces;
using SupermarketSystem.Application.Common.Results;
using SupermarketSystem.Application.Ordering.CompleteOrder;
using SupermarketSystem.Application.Sales.CompleteSale;

namespace SupermarketSystem.Application.Ordering.CompleteDelivery;

public sealed record CompleteDeliveryCommand(Guid OrderId, Guid PaymentMethodId, decimal AmountCollected, Guid ClientRequestId);

/// <summary>
/// زر "دفع" بصفحة السائق - يتحقق إنه هو فعلًا السائق المسنَد لهاي
/// الطلبية (مو أي سائق ثاني بس يعرف رقمها، الهوية تجي من ICurrentUserContext
/// لا من الطلب - نفس نمط GetMyDeliveriesHandler)، وبعدين يفوّض بالكامل
/// لنفس CompleteOrderHandler المستخدَم من شاشة الكاشير - صفر تكرار منطق
/// إنشاء الفاتورة/خصم المخزون/تسجيل الكاش (نفس مبدأ CompleteOrderCommand
/// نفسه اللي بيعيد استخدام CompleteSaleHandler).
/// </summary>
public sealed class CompleteDeliveryHandler
{
    private readonly IApplicationDbContext _context;
    private readonly ICurrentUserContext _currentUser;
    private readonly CompleteOrderHandler _completeOrderHandler;

    public CompleteDeliveryHandler(IApplicationDbContext context, ICurrentUserContext currentUser, CompleteOrderHandler completeOrderHandler)
    {
        _context = context;
        _currentUser = currentUser;
        _completeOrderHandler = completeOrderHandler;
    }

    public async Task<Result<CompleteSaleResponse>> HandleAsync(CompleteDeliveryCommand command, CancellationToken cancellationToken)
    {
        var driverId = _currentUser.UserId
            ?? throw new InvalidOperationException("لا يمكن إكمال توصيل بلا هوية مستخدم مصادَق عليها.");

        var order = await _context.Orders.AsNoTracking()
            .FirstOrDefaultAsync(o => o.Id == command.OrderId, cancellationToken);

        if (order is null)
        {
            return Result.Failure<CompleteSaleResponse>(Error.NotFound("Order.NotFound", $"الطلب '{command.OrderId}' غير موجود."));
        }

        if (order.DriverId != driverId)
        {
            return Result.Failure<CompleteSaleResponse>(
                Error.Forbidden("Order.NotAssignedToDriver", "هذا الطلب غير مسنَد لك."));
        }

        var payment = new CompleteOrderPaymentDto(command.PaymentMethodId, command.AmountCollected, ExternalReference: null);
        var completeOrderCommand = new CompleteOrderCommand(command.OrderId, new[] { payment }, command.ClientRequestId);

        return await _completeOrderHandler.HandleAsync(completeOrderCommand, cancellationToken);
    }
}
