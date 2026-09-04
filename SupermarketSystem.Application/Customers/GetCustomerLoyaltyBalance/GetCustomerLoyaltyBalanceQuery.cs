using Microsoft.EntityFrameworkCore;
using SupermarketSystem.Application.Common.Interfaces;
using SupermarketSystem.Application.Common.Results;

namespace SupermarketSystem.Application.Customers.GetCustomerLoyaltyBalance;

public sealed record GetCustomerLoyaltyBalanceQuery(Guid CustomerId);

public sealed record GetCustomerLoyaltyBalanceResponse(int Balance);

/// <summary>الرصيد محسوب حيًّا (SUM) لا مخزَّن - راجع تعليق CustomerLoyaltyPointsEntry.</summary>
public sealed class GetCustomerLoyaltyBalanceHandler
{
    private readonly IApplicationDbContext _context;

    public GetCustomerLoyaltyBalanceHandler(IApplicationDbContext context)
    {
        _context = context;
    }

    public async Task<Result<GetCustomerLoyaltyBalanceResponse>> HandleAsync(
        GetCustomerLoyaltyBalanceQuery query, CancellationToken cancellationToken)
    {
        var exists = await _context.Customers.AsNoTracking()
            .AnyAsync(c => c.Id == query.CustomerId && !c.IsDeleted, cancellationToken);

        if (!exists)
        {
            return Result.Failure<GetCustomerLoyaltyBalanceResponse>(
                Error.NotFound("Customer.NotFound", "الزبون غير موجود."));
        }

        var balance = await _context.CustomerLoyaltyPointsEntries.AsNoTracking()
            .Where(e => e.CustomerId == query.CustomerId)
            .SumAsync(e => (int?)e.Points, cancellationToken) ?? 0;

        return Result.Success(new GetCustomerLoyaltyBalanceResponse(balance));
    }
}
