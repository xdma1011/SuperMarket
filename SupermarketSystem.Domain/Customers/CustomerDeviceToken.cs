using SupermarketSystem.Domain.Common;

namespace SupermarketSystem.Domain.Customers;

public enum DevicePlatform
{
    Android = 1,
    Ios = 2,
    Web = 3
}

/// <summary>
/// توكن جهاز FCM (Firebase Cloud Messaging) - تطبيق الزبائن يسجّله عند
/// أول تشغيل/تسجيل دخول. زبون واحد ممكن يكون عنده أكثر من جهاز، لذا
/// قائمة لا حقل واحد بـCustomer. توكن قديم منتهي/مستبدَل يُستبدل لا
/// يتراكم (راجع Replace) - نفس فكرة TelegramChatLink.Relink.
/// </summary>
public class CustomerDeviceToken : Entity
{
    public Guid CustomerId { get; private set; }
    public string Token { get; private set; } = null!;
    public DevicePlatform Platform { get; private set; }
    public DateTime RegisteredAtUtc { get; private set; }

    private CustomerDeviceToken() { } // EF Core

    public CustomerDeviceToken(Guid customerId, string token, DevicePlatform platform, DateTime registeredAtUtc)
    {
        CustomerId = customerId;
        Token = token;
        Platform = platform;
        RegisteredAtUtc = registeredAtUtc;
    }
}
