using Microsoft.EntityFrameworkCore;
using SupermarketSystem.Application.Common.Interfaces;

namespace SupermarketSystem.Application.Payments.GetPaymentMethods;

public sealed record PaymentMethodDto(Guid Id, string Name, bool RequiresExternalReference);

/// <summary>
/// كانت ناقصة بالكامل — ProcessReturnCommand.Refunds وCompleteSaleCommand
/// كلاهما يحتاج PaymentMethodId، بس ما في endpoint يرجّع القائمة أصلًا
/// للفرونت إند يبني منه قائمة اختيار.
/// </summary>
public sealed class GetPaymentMethodsHandler
{
    private readonly IApplicationDbContext _context;

    public GetPaymentMethodsHandler(IApplicationDbContext context)
    {
        _context = context;
    }

    public async Task<IReadOnlyList<PaymentMethodDto>> HandleAsync(CancellationToken cancellationToken)
    {
        return await _context.PaymentMethods.AsNoTracking()
            .Where(pm => pm.IsActive)
            .OrderBy(pm => pm.Name)
            .Select(pm => new PaymentMethodDto(pm.Id, pm.Name, pm.RequiresExternalReference))
            .ToListAsync(cancellationToken);
    }
}
