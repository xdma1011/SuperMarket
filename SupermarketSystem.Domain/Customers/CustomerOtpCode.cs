using SupermarketSystem.Domain.Common;

namespace SupermarketSystem.Domain.Customers;

/// <summary>
/// كود تحقق مؤقت لتسجيل دخول الزبون (تطبيق الطلبات) عبر رقم الهاتف +
/// تلغرام. يُخزَّن مجزَّأ (CodeHash) لا نصًا صريحًا - نفس مبدأ RefreshToken
/// بالمصادقة الإدارية (ITokenService.HashRefreshToken)، فتسرّب قاعدة
/// البيانات وحدها ما يكفي لانتحال زبون.
/// </summary>
public class CustomerOtpCode : Entity
{
    public string Phone { get; private set; } = null!;
    public string CodeHash { get; private set; } = null!;
    public DateTime ExpiresAtUtc { get; private set; }
    public bool IsUsed { get; private set; }
    public DateTime CreatedAtUtc { get; private set; }

    private CustomerOtpCode() { } // EF Core

    public CustomerOtpCode(string phone, string codeHash, DateTime createdAtUtc, DateTime expiresAtUtc)
    {
        Phone = phone;
        CodeHash = codeHash;
        CreatedAtUtc = createdAtUtc;
        ExpiresAtUtc = expiresAtUtc;
    }

    public bool IsValid(string codeHash, DateTime nowUtc)
        => !IsUsed && nowUtc <= ExpiresAtUtc && CodeHash == codeHash;

    public void MarkUsed() => IsUsed = true;
}
