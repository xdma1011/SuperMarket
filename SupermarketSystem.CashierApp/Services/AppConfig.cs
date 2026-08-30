using System.IO;
using System.Text.Json;

namespace SupermarketSystem.CashierApp.Services;

public sealed class AppConfig
{
    public string ApiBaseUrl { get; set; } = "https://localhost:56024/api/v1";
    public int SyncIntervalSeconds { get; set; } = 60;
    public int CatalogSyncPageSize { get; set; } = 200;

    /// <summary>Bearer Token الحالي - يُملأ بعد تسجيل الدخول (جلسة لاحقة).</summary>
    public string? AccessToken { get; set; }

    /// <summary>"Usb" أو "Network" - يُحدَّد لما تتوفر الطابعة الفعلية، بلا أي تعديل كود.</summary>
    public string PrinterConnectionType { get; set; } = "Usb";

    /// <summary>لاتصال USB: اسم الطابعة كما يظهر بويندوز (Devices and Printers). لاتصال الشبكة: غير مستخدم.</summary>
    public string? PrinterUsbName { get; set; }

    /// <summary>لاتصال الشبكة: عنوان IP الثابت للطابعة. لاتصال USB: غير مستخدم.</summary>
    public string? PrinterNetworkIpAddress { get; set; }

    /// <summary>منفذ الطباعة القياسي لأغلب طابعات الشبكة الحرارية (RAW/JetDirect) - نادرًا ما يحتاج تغيير.</summary>
    public int PrinterNetworkPort { get; set; } = 9100;

    /// <summary>
    /// باسوورد شاشة "الطابور المعلَّق" (Admin) - محلي بالكامل، بلا أي علاقة
    /// بحساب المستخدم أو صلاحياته بالسيرفر. الهدف بسيط: يمنع الكاشير من
    /// فتحها بالصدفة، لا حماية أمنية جدّية (محفوظ نص صريح بملف الإعدادات
    /// المحلي، زي كل إعداد آخر بهالملف). صاحب المحل يقدر يغيّره من هون
    /// مباشرة بأي وقت.
    /// </summary>
    public string AdminScreenPassword { get; set; } = "1234";

    private static readonly string ConfigPath = Path.Combine(AppContext.BaseDirectory, "appsettings.json");

    public static AppConfig Load()
    {
        try
        {
            if (!File.Exists(ConfigPath))
            {
                return new AppConfig();
            }

            var json = File.ReadAllText(ConfigPath);
            var config = JsonSerializer.Deserialize<AppConfig>(json, new JsonSerializerOptions
            {
                PropertyNameCaseInsensitive = true
            });

            return config ?? new AppConfig();
        }
        catch
        {
            return new AppConfig();
        }
    }

    public void Save()
    {
        var json = JsonSerializer.Serialize(this, new JsonSerializerOptions { WriteIndented = true });
        File.WriteAllText(ConfigPath, json);
    }
}
