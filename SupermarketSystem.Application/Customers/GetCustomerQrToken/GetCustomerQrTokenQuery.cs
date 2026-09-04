using Microsoft.EntityFrameworkCore;
using SupermarketSystem.Application.Common.Interfaces;
using SupermarketSystem.Application.Common.Results;

namespace SupermarketSystem.Application.Customers.GetCustomerQrToken;

public sealed record GetCustomerQrTokenQuery(Guid CustomerId);

public sealed record GetCustomerQrTokenResponse(string QrToken);

/// <summary>
/// ⚠️ مؤقت بلا تحقق هوية حقيقي (نفس تحذير OrderingEndpoints) - يفترض إن
/// customerId المُرسَل صحيح بلا إثبات ملكية. لما تُفعَّل مصادقة الزبون
/// الحقيقية (ICustomerAuthTokenService)، هاي الـquery المفروض تاخذ
/// customerId من التوكن نفسه لا من الطلب.
/// </summary>
public sealed class GetCustomerQrTokenHandler
{
    private readonly IApplicationDbContext _context;
    private readonly IQrTokenService _qrTokenService;

    public GetCustomerQrTokenHandler(IApplicationDbContext context, IQrTokenService qrTokenService)
    {
        _context = context;
        _qrTokenService = qrTokenService;
    }

    public async Task<Result<GetCustomerQrTokenResponse>> HandleAsync(
        GetCustomerQrTokenQuery query, CancellationToken cancellationToken)
    {
        var exists = await _context.Customers.AsNoTracking()
            .AnyAsync(c => c.Id == query.CustomerId && !c.IsDeleted, cancellationToken);

        if (!exists)
        {
            return Result.Failure<GetCustomerQrTokenResponse>(
                Error.NotFound("Customer.NotFound", "الزبون غير موجود."));
        }

        return Result.Success(new GetCustomerQrTokenResponse(_qrTokenService.GenerateCustomerQrToken(query.CustomerId)));
    }
}
