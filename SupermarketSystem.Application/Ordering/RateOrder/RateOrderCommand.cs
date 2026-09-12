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
        // IgnoreQueryFilters إلزامي هون: endpoint هذا AllowAnonymous (زبون
        // بلا توكن)، فمرشِّح الفرع العام بيحجب أي صف Order دائمًا لطلب
        // مجهول. آمن تجاهله لأن OrderId معروف صراحة (نفس منطق GetOrderById
        // بالنسخة الموجَّهة للزبون - رقم الطلب وحده هو "بطاقة الدخول"
        // هون، بلا تحقق هوية حقيقي، وهذا محدود ومقصود مؤقتًا).
        var order = await _context.Orders.IgnoreQueryFilters()
            .FirstOrDefaultAsync(o => o.Id == command.OrderId, cancellationToken);
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
