using Microsoft.EntityFrameworkCore;
using SupermarketSystem.Application.Common.Interfaces;
using SupermarketSystem.Application.Common.Results;
using SupermarketSystem.Domain.Customers;

namespace SupermarketSystem.Application.Customers.RedeemLoyaltyPoints;

public sealed record RedeemLoyaltyPointsCommand(Guid CustomerId, int Points);

/// <summary>
/// يسجّل استبدال نقاط بسطر سالب بالدفتر فقط - ما بيربطها تلقائيًا بخصم
/// فعلي على فاتورة (هذا يحتاج تصميم منفصل لتطبيق الخصم وقت الدفع، غير
/// مبني بعد). رصيد غير كافٍ يُرفض صراحة - هذا تحقق حسابي بسيط، لا قرار
/// حسّاس يحتاج "سماح مع مراجعة" (راجع CLAUDE.md §1.6، ينطبق على قرارات
/// تقديرية لا صحة أرقام).
/// </summary>
public sealed class RedeemLoyaltyPointsHandler
{
    private readonly IApplicationDbContext _context;
    private readonly IDateTimeProvider _dateTimeProvider;

    public RedeemLoyaltyPointsHandler(IApplicationDbContext context, IDateTimeProvider dateTimeProvider)
    {
        _context = context;
        _dateTimeProvider = dateTimeProvider;
    }

    public async Task<Result> HandleAsync(RedeemLoyaltyPointsCommand command, CancellationToken cancellationToken)
    {
        if (command.Points <= 0)
        {
            return Result.Failure(Error.Validation("Loyalty.InvalidPoints", "عدد النقاط المطلوب استبدالها لازم يكون أكبر من صفر."));
        }

        var exists = await _context.Customers.AsNoTracking()
            .AnyAsync(c => c.Id == command.CustomerId && !c.IsDeleted, cancellationToken);

        if (!exists)
        {
            return Result.Failure(Error.NotFound("Customer.NotFound", "الزبون غير موجود."));
        }

        var balance = await _context.CustomerLoyaltyPointsEntries.AsNoTracking()
            .Where(e => e.CustomerId == command.CustomerId)
            .SumAsync(e => (int?)e.Points, cancellationToken) ?? 0;

        if (balance < command.Points)
        {
            return Result.Failure(Error.BusinessRule("Loyalty.InsufficientBalance", $"رصيد النقاط الحالي ({balance}) أقل من المطلوب استبداله ({command.Points})."));
        }

        _context.CustomerLoyaltyPointsEntries.Add(new CustomerLoyaltyPointsEntry(
            command.CustomerId, -command.Points, LoyaltyPointsReason.Redeemed, orderId: null, _dateTimeProvider.UtcNow));

        await _context.SaveChangesAsync(cancellationToken);
        return Result.Success();
    }
}
