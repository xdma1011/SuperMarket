using System.IO;
using System.Text.Json;

namespace SupermarketSystem.CashierApp.Services;

/// <summary>نفس نمط StoreBrandingCache بالضبط - ملف JSON مستقل، لا جدول SQLite جديد (راجع تعليق StoreBrandingCache).</summary>
public static class PaymentSettingsCache
{
    private const decimal DefaultUsdToJodExchangeRate = 0.71m;

    private static string GetFilePath(string dataDir) => Path.Combine(dataDir, "payment-settings.json");

    public static decimal ReadUsdToJodExchangeRate(string dataDir)
    {
        try
        {
            var path = GetFilePath(dataDir);
            if (!File.Exists(path))
            {
                return DefaultUsdToJodExchangeRate;
            }

            var json = File.ReadAllText(path);
            var cached = JsonSerializer.Deserialize<CachedPaymentSettings>(json);
            return cached is { UsdToJodExchangeRate: > 0 } ? cached.UsdToJodExchangeRate : DefaultUsdToJodExchangeRate;
        }
        catch
        {
            return DefaultUsdToJodExchangeRate;
        }
    }

    public static void WriteUsdToJodExchangeRate(string dataDir, decimal exchangeRate)
    {
        try
        {
            var path = GetFilePath(dataDir);
            var json = JsonSerializer.Serialize(new CachedPaymentSettings(exchangeRate));
            File.WriteAllText(path, json);
        }
        catch
        {
            // فشل الكتابة ما لازم يوقف التطبيق - القيمة الافتراضية تضل تشتغل.
        }
    }

    private sealed record CachedPaymentSettings(decimal UsdToJodExchangeRate);
}
