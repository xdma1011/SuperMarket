using Microsoft.EntityFrameworkCore;
using SupermarketSystem.Application.Common.Interfaces;
using SupermarketSystem.Application.Common.Results;

namespace SupermarketSystem.Application.Customers.ResolveCustomerQrToken;

public sealed record ResolveCustomerQrTokenQuery(string QrToken);

public sealed record ResolveCustomerQrTokenResponse(Guid CustomerId, string FullName, string? Phone, bool IsBlocked);

/// <summary>يستخدمها الكاشير بعد مسح باركود الزبون - يتحقق من التوقيع أول شيء (راجع QrTokenService)، قبل أي استعلام على قاعدة البيانات.</summary>
public sealed class ResolveCustomerQrTokenHandler
{
    private readonly IApplicationDbContext _context;
    private readonly IQrTokenService _qrTokenService;

    public ResolveCustomerQrTokenHandler(IApplicationDbContext context, IQrTokenService qrTokenService)
    {
        _context = context;
        _qrTokenService = qrTokenService;
    }

    public async Task<Result<ResolveCustomerQrTokenResponse>> HandleAsync(
        ResolveCustomerQrTokenQuery query, CancellationToken cancellationToken)
    {
        var customerId = _qrTokenService.ValidateCustomerQrToken(query.QrToken);
        if (customerId is null)
        {
            return Result.Failure<ResolveCustomerQrTokenResponse>(
                Error.Validation("CustomerQrToken.Invalid", "رمز QR غير صالح أو تم التلاعب به."));
        }

        var customer = await _context.Customers.AsNoTracking()
            .Where(c => c.Id == customerId.Value && !c.IsDeleted)
            .Select(c => new { c.Id, c.FullName, c.Phone, c.IsBlocked })
            .FirstOrDefaultAsync(cancellationToken);

        if (customer is null)
        {
            return Result.Failure<ResolveCustomerQrTokenResponse>(
                Error.NotFound("Customer.NotFound", "الزبون غير موجود."));
        }

        return Result.Success(new ResolveCustomerQrTokenResponse(customer.Id, customer.FullName, customer.Phone, customer.IsBlocked));
    }
}
