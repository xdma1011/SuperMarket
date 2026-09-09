using System.IO;
using System.Text.Json;

namespace SupermarketSystem.CashierApp.Services;

/// <summary>
/// اسم المحل مخزَّن بملف JSON بسيط جنب local.db - عمدًا مش جدول SQLite
/// جديد: الكاشير حاليًا يستخدم EnsureCreated() لا Migrations حقيقية
/// (راجع نقاش صاحب المشروع)، فجدول جديد ما رح يظهر أبدًا بأي قاعدة
/// محلية موجودة أصلًا عند زبون. ملف مستقل بسيط يتفادى هالمشكلة كليًا،
/// ويسمح طباعة اسم المحل حتى بلا اتصال (آخر نسخة معروفة).
/// </summary>
public static class StoreBrandingCache
{
    private static string GetFilePath(string dataDir) => Path.Combine(dataDir, "store-branding.json");

    public static string? ReadStoreName(string dataDir)
    {
        try
        {
            var path = GetFilePath(dataDir);
            if (!File.Exists(path))
            {
                return null;
            }

            var json = File.ReadAllText(path);
            var cached = JsonSerializer.Deserialize<CachedBranding>(json);
            return cached?.StoreName;
        }
        catch
        {
            return null;
        }
    }

    public static void WriteStoreName(string dataDir, string? storeName)
    {
        try
        {
            var path = GetFilePath(dataDir);
            var json = JsonSerializer.Serialize(new CachedBranding(storeName));
            File.WriteAllText(path, json);
        }
        catch
        {
            // فشل الكتابة (قرص ممتلئ، صلاحيات، إلخ) ما لازم يوقف التطبيق -
            // اسم المحل تفصيلة تجميلية بالفاتورة، لا وظيفة حرجة.
        }
    }

    private sealed record CachedBranding(string? StoreName);
}
