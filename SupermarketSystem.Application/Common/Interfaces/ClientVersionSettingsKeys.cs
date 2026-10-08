namespace SupermarketSystem.Application.Common.Interfaces;

/// <summary>
/// فحص نسخة تطبيق الكاشير (2-أ بند 2، 8/10/2026). تطبيق الكاشير القديم بعد تغيير بالـAPI كان بيحوّل بيعاته لطابور عالق بصمت
/// (رفض نهائي بلا تفسير). هلق الكاشير بيبعت نسخته بترويسة `X-Client-Version`، ولو الإعداد هون أعلى من نسخته بيجي رد 426 برسالة
/// "حدّث البرنامج" واضحة بدل رفض غامض. فاضي (الافتراضي) = بلا فحص.
/// </summary>
public static class ClientVersionSettingsKeys
{
    /// <summary>أقل نسخة كاشير مقبولة (مثلًا 1.2.0). فاضي = أي نسخة.</summary>
    public const string CashierMinimumVersion = "Cashier.MinimumVersion";
}
