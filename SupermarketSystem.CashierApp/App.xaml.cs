using System.IO;
using System.Text;
using System.Windows;
using SupermarketSystem.CashierApp.Local;
using SupermarketSystem.CashierApp.Services;
using SupermarketSystem.CashierApp.Views;

namespace SupermarketSystem.CashierApp;

public partial class App : Application
{
    protected override void OnStartup(StartupEventArgs e)
    {
        base.OnStartup(e);

        // إلزامي قبل أي استخدام لـEncoding.GetEncoding(1256) - .NET Core
        // ما بيشمل ترميزات Windows القديمة (زي العربي 1256) افتراضيًا،
        // ولازم تسجيل المزوّد صراحة مرة وحدة وقت الإقلاع.
        Encoding.RegisterProvider(CodePagesEncodingProvider.Instance);

        var config = AppConfig.Load();

        // %LocalAppData%\SupermarketSystem.CashierApp\local.db - مجلد
        // مضمون الكتابة لأي مستخدم عادي، بخلاف مجلد التثبيت (ممكن يكون
        // Program Files، بلا صلاحية كتابة).
        var dataDir = Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
            "SupermarketSystem.CashierApp");
        Directory.CreateDirectory(dataDir);
        var dbPath = Path.Combine(dataDir, "local.db");

        // Migrate() لا EnsureCreated() - كانت المشكلة إنه EnsureCreated() ما
        // بيطبّق أي تعديل سكيما لاحق على local.db موجودة أصلًا عند مستخدم -
        // جدول جديد كان رح يظل غايب للأبد بدون Migration حقيقية. ما في
        // مستخدمين حاليين على local.db بالإصدار القديم (المشروع لسه قبل
        // الإطلاق)، فما في داعي backfill يدوي.
        using (var db = new LocalDbContext(dbPath))
        {
            db.Database.Migrate();
        }

        var apiClient = new ApiClient(config);
        var authSession = new AuthSession();
        var backgroundSync = new BackgroundSyncService(apiClient, dbPath, config.SyncIntervalSeconds, config.CatalogSyncPageSize);
        var receiptPrinter = new Services.Printing.ReceiptPrinterService(config);

        // Fire-and-forget: اسم المحل تفصيلة تجميلية بالفاتورة، ما تستاهل
        // تأخير فتح شاشة تسجيل الدخول لحد ما تجاوب السيرفر. لو فشل (بلا
        // نت لحظة الإقلاع)، يضل يستخدم آخر نسخة مخزَّنة محليًا (أو لا شي).
        _ = RefreshStoreBrandingCacheAsync(apiClient, dataDir);
        _ = RefreshPaymentSettingsCacheAsync(apiClient, dataDir);

        var loginWindow = new LoginWindow(apiClient, authSession, dbPath, backgroundSync, receiptPrinter, config.AdminScreenPassword);
        loginWindow.Show();
    }

    private static async Task RefreshStoreBrandingCacheAsync(ApiClient apiClient, string dataDir)
    {
        var branding = await apiClient.GetStoreBrandingAsync(CancellationToken.None);
        if (branding is not null)
        {
            StoreBrandingCache.WriteStoreName(dataDir, branding.StoreName);
        }
    }

    private static async Task RefreshPaymentSettingsCacheAsync(ApiClient apiClient, string dataDir)
    {
        var settings = await apiClient.GetPaymentSettingsAsync(CancellationToken.None);
        if (settings is not null)
        {
            PaymentSettingsCache.WriteUsdToJodExchangeRate(dataDir, settings.UsdToJodExchangeRate);
        }
    }
}
