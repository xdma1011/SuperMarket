using System.Security.Cryptography;
using System.Text;
using Microsoft.EntityFrameworkCore;
using SupermarketSystem.Application.Common.Interfaces;
using SupermarketSystem.Application.Common.Results;
using SupermarketSystem.Domain.Customers;

namespace SupermarketSystem.Application.CustomerAuth.VerifyCustomerOtp;

public sealed record VerifyCustomerOtpCommand(string Phone, string Code);

public sealed record VerifyCustomerOtpResponse(string AccessToken, DateTime ExpiresAtUtc, Guid CustomerId);

/// <summary>
/// خطوة 2: يتحقق من الكود المُرسَل بـRequestCustomerOtpHandler، وعند
/// النجاح يجلب-أو-ينشئ Customer بنفس نمط PlaceOrderHandler بالضبط، ثم
/// يصدر توكن هوية (ICustomerAuthTokenService). آخر كود صالح غير
/// مستخدم لنفس الرقم فقط يُقبل - لا "أي كود سابق صالح لهالرقم".
/// </summary>
public sealed class VerifyCustomerOtpHandler
{
    private readonly IApplicationDbContext _context;
    private readonly ICustomerAuthTokenService _customerAuthTokenService;
    private readonly IDateTimeProvider _dateTimeProvider;

    public VerifyCustomerOtpHandler(
        IApplicationDbContext context,
        ICustomerAuthTokenService customerAuthTokenService,
        IDateTimeProvider dateTimeProvider)
    {
        _context = context;
        _customerAuthTokenService = customerAuthTokenService;
        _dateTimeProvider = dateTimeProvider;
    }

    public async Task<Result<VerifyCustomerOtpResponse>> HandleAsync(
        VerifyCustomerOtpCommand command, CancellationToken cancellationToken)
    {
        var phone = command.Phone.Trim();
        var codeHash = Convert.ToBase64String(SHA256.HashData(Encoding.UTF8.GetBytes(command.Code.Trim())));
        var now = _dateTimeProvider.UtcNow;

        var otpCode = await _context.CustomerOtpCodes
            .Where(o => o.Phone == phone)
            .OrderByDescending(o => o.CreatedAtUtc)
            .FirstOrDefaultAsync(cancellationToken);

        if (otpCode is null || !otpCode.IsValid(codeHash, now))
        {
            return Result.Failure<VerifyCustomerOtpResponse>(
                Error.Validation("CustomerOtp.InvalidOrExpired", "الكود غير صحيح أو منتهي الصلاحية."));
        }

        otpCode.MarkUsed();

        var customer = await _context.Customers
            .FirstOrDefaultAsync(c => c.Phone == phone && !c.IsDeleted, cancellationToken);

        if (customer is null)
        {
            customer = new Customer(phone, phone, email: null);
            _context.Customers.Add(customer);
        }
        else if (customer.IsBlocked)
        {
            return Result.Failure<VerifyCustomerOtpResponse>(
                Error.Forbidden("CustomerOtp.Blocked", "هذا الرقم محظور من استخدام تطبيق الطلبات."));
        }

        await _context.SaveChangesAsync(cancellationToken);

        var token = _customerAuthTokenService.CreateAccessToken(customer.Id, phone);
        return Result.Success(new VerifyCustomerOtpResponse(token.Token, token.ExpiresAtUtc, customer.Id));
    }
}
