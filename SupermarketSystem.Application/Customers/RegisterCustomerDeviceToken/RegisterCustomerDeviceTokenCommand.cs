using Microsoft.EntityFrameworkCore;
using SupermarketSystem.Application.Common.Interfaces;
using SupermarketSystem.Application.Common.Results;
using SupermarketSystem.Domain.Customers;

namespace SupermarketSystem.Application.Customers.RegisterCustomerDeviceToken;

public sealed record RegisterCustomerDeviceTokenCommand(Guid CustomerId, string Token, DevicePlatform Platform);

/// <summary>
/// ⚠️ نفس تحذير OrderingEndpoints - بلا تحقق هوية حقيقي بعد (customerId
/// موثوق من الطلب). يُستدعى من تطبيق الزبائن بعد نجاح تسجيل الدخول
/// (verify-otp) لتفعيل استقبال إشعارات Push.
/// </summary>
public sealed class RegisterCustomerDeviceTokenHandler
{
    private readonly IApplicationDbContext _context;
    private readonly IDateTimeProvider _dateTimeProvider;

    public RegisterCustomerDeviceTokenHandler(IApplicationDbContext context, IDateTimeProvider dateTimeProvider)
    {
        _context = context;
        _dateTimeProvider = dateTimeProvider;
    }

    public async Task<Result> HandleAsync(RegisterCustomerDeviceTokenCommand command, CancellationToken cancellationToken)
    {
        var customerExists = await _context.Customers.AsNoTracking()
            .AnyAsync(c => c.Id == command.CustomerId && !c.IsDeleted, cancellationToken);

        if (!customerExists)
        {
            return Result.Failure(Error.NotFound("Customer.NotFound", "الزبون غير موجود."));
        }

        // نفس التوكن ممكن يتسجّل سابقًا لزبون آخر (جهاز مشترك/إعادة تثبيت) -
        // الفهرس الفريد على Token يمنع التكرار، فنحدّث السطر الموجود بدل ما نضيف.
        var existing = await _context.CustomerDeviceTokens.FirstOrDefaultAsync(t => t.Token == command.Token, cancellationToken);
        if (existing is not null)
        {
            _context.CustomerDeviceTokens.Remove(existing);
        }

        _context.CustomerDeviceTokens.Add(
            new CustomerDeviceToken(command.CustomerId, command.Token, command.Platform, _dateTimeProvider.UtcNow));

        await _context.SaveChangesAsync(cancellationToken);
        return Result.Success();
    }
}
