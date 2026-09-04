using System.Security.Cryptography;
using System.Text;
using Microsoft.Extensions.Options;
using SupermarketSystem.Application.Common.Interfaces;

namespace SupermarketSystem.Infrastructure.Services;

/// <summary>
/// نفس أسلوب CustomerAuthTokenService (HMAC-SHA256، Base64Url) بس بسياق
/// توقيع مختلف ("qr:" بادئة بالـpayload) عمدًا - يمنع استخدام توكن جلسة
/// الزبون كـQR أو العكس حتى لو نفس مفتاح التوقيع (JwtOptions.SigningKey).
/// </summary>
public sealed class QrTokenService : IQrTokenService
{
    private const string ContextPrefix = "qr:";

    private readonly byte[] _signingKey;

    public QrTokenService(IOptions<JwtOptions> jwtOptions)
    {
        _signingKey = Encoding.UTF8.GetBytes(jwtOptions.Value.SigningKey);
    }

    public string GenerateCustomerQrToken(Guid customerId)
    {
        var payloadBytes = Encoding.UTF8.GetBytes($"{ContextPrefix}{customerId}");
        var signature = HMACSHA256.HashData(_signingKey, payloadBytes);
        return $"{Base64UrlEncode(payloadBytes)}.{Base64UrlEncode(signature)}";
    }

    public Guid? ValidateCustomerQrToken(string token)
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

        var payload = Encoding.UTF8.GetString(payloadBytes);
        if (!payload.StartsWith(ContextPrefix, StringComparison.Ordinal))
        {
            return null;
        }

        return Guid.TryParse(payload[ContextPrefix.Length..], out var customerId) ? customerId : null;
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
