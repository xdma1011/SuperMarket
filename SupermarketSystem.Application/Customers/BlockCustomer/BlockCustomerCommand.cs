using Microsoft.EntityFrameworkCore;
using SupermarketSystem.Application.Common.Interfaces;
using SupermarketSystem.Application.Common.Results;

namespace SupermarketSystem.Application.Customers.BlockCustomer;

public sealed record BlockCustomerCommand(Guid CustomerId);

/// <summary>حظر يمنع تقديم طلبات جديدة (PlaceOrderHandler بيفحصه) - ما بيمنع بيع POS عادي، ولا يحذف تاريخ الزبون.</summary>
public sealed class BlockCustomerHandler
{
    private readonly IApplicationDbContext _context;

    public BlockCustomerHandler(IApplicationDbContext context)
    {
        _context = context;
    }

    public async Task<Result> HandleAsync(BlockCustomerCommand command, CancellationToken cancellationToken)
    {
        var customer = await _context.Customers.FirstOrDefaultAsync(c => c.Id == command.CustomerId, cancellationToken);
        if (customer is null)
        {
            return Result.Failure(Error.NotFound("Customer.NotFound", $"الزبون '{command.CustomerId}' غير موجود."));
        }

        customer.Block();
        await _context.SaveChangesAsync(cancellationToken);
        return Result.Success();
    }
}
