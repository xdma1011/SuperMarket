using System.Security.Cryptography;
using System.Text;
using Microsoft.Extensions.Options;
using SupermarketSystem.Application.Common.Interfaces;

namespace SupermarketSystem.Infrastructure.Services;

/// <summary>
/// توكن مضغوط موقَّع بـHMAC-SHA256 (نفس مفتاح JwtOptions.SigningKey - لا
/// داعي لمفتاح ثانٍ منفصل، المفتاح أصلًا سرّي وموجود وقت الإقلاع). ليس
/// JWT قياسي عمدًا: الزبون ليس له وسيط JwtBearer مسجَّل بالـpipeline
/// (راجع تعليق ICustomerAuthTokenService) - endpoint الزبون يتحقق من
/// التوكن يدويًا عبر ValidateAccessToken لما يُفعَّل مستقبلًا.
///
/// الصيغة: base64url(customerId|phone|expiryUnixSeconds) + "." + base64url(HMAC).
/// عمر التوكن طويل نسبيًا (30 يوم) بعكس توكن الموظف قصير العمر - لا
/// يوجد مفهوم refresh token هون بعد، ولا صلاحيات تُفحص، فقصر العمر
/// أقل أهمية؛ الخطر الوحيد هو تسرّب توكن مسروق، وهذا معالج بإمكانية
/// إبطال لاحق (غير مبني بعد) عبر تغيير المفتاح أو قائمة سوداء.
/// </summary>
public sealed class CustomerAuthTokenService : ICustomerAuthTokenService
{
    private const int TokenLifetimeDays = 30;

    private readonly byte[] _signingKey;

    public CustomerAuthTokenService(IOptions<JwtOptions> jwtOptions)
    {
        _signingKey = Encoding.UTF8.GetBytes(jwtOptions.Value.SigningKey);
    }

    public CustomerAccessTokenResult CreateAccessToken(Guid customerId, string phone)
    {
        var expiresAtUtc = DateTime.UtcNow.AddDays(TokenLifetimeDays);
        var payload = $"{customerId}|{phone}|{new DateTimeOffset(expiresAtUtc).ToUnixTimeSeconds()}";
        var payloadBytes = Encoding.UTF8.GetBytes(payload);
        var signature = HMACSHA256.HashData(_signingKey, payloadBytes);

        var token = $"{Base64UrlEncode(payloadBytes)}.{Base64UrlEncode(signature)}";
        return new CustomerAccessTokenResult(token, expiresAtUtc);
    }

    public (Guid CustomerId, string Phone)? ValidateAccessToken(string token)
    {
        var parts = token.Split('.', 2);
        if (parts.Length != 2)
        {
            return null;
        }

        byte[] payloadBytes;
        byte[] signature;
        try
        {
            payloadBytes = Base64UrlDecode(parts[0]);
            signature = Base64UrlDecode(parts[1]);
        }
        catch (FormatException)
        {
            return null;
        }

        var expectedSignature = HMACSHA256.HashData(_signingKey, payloadBytes);
        if (!CryptographicOperations.FixedTimeEquals(signature, expectedSignature))
        {
            return null;
        }

        var payloadParts = Encoding.UTF8.GetString(payloadBytes).Split('|', 3);
        if (payloadParts.Length != 3 ||
            !Guid.TryParse(payloadParts[0], out var customerId) ||
            !long.TryParse(payloadParts[2], out var expiryUnixSeconds))
        {
            return null;
        }

        var expiresAtUtc = DateTimeOffset.FromUnixTimeSeconds(expiryUnixSeconds).UtcDateTime;
        if (DateTime.UtcNow > expiresAtUtc)
        {
            return null;
        }

        return (customerId, payloadParts[1]);
    }

    private static string Base64UrlEncode(byte[] bytes)
        => Convert.ToBase64String(bytes).Replace('+', '-').Replace('/', '_').TrimEnd('=');

    private static byte[] Base64UrlDecode(string value)
    {
        var base64 = value.Replace('-', '+').Replace('_', '/');
        switch (base64.Length % 4)
        {
            case 2: base64 += "=="; break;
            case 3: base64 += "="; break;
        }
        return Convert.FromBase64String(base64);
    }
}
