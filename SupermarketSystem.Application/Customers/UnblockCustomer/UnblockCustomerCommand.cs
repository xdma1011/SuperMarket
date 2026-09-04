using Microsoft.EntityFrameworkCore;
using SupermarketSystem.Application.Common.Interfaces;
using SupermarketSystem.Application.Common.Results;

namespace SupermarketSystem.Application.Customers.UnblockCustomer;

public sealed record UnblockCustomerCommand(Guid CustomerId);

public sealed class UnblockCustomerHandler
{
    private readonly IApplicationDbContext _context;

    public UnblockCustomerHandler(IApplicationDbContext context)
    {
        _context = context;
    }

    public async Task<Result> HandleAsync(UnblockCustomerCommand command, CancellationToken cancellationToken)
    {
        var customer = await _context.Customers.FirstOrDefaultAsync(c => c.Id == command.CustomerId, cancellationToken);
        if (customer is null)
        {
            return Result.Failure(Error.NotFound("Customer.NotFound", $"الزبون '{command.CustomerId}' غير موجود."));
        }

        customer.Unblock();
        await _context.SaveChangesAsync(cancellationToken);
        return Result.Success();
    }
}
