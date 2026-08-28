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

        using (var db = new LocalDbContext(dbPath))
        {
            db.Database.EnsureCreated();
        }

        var apiClient = new ApiClient(config);
        var authSession = new AuthSession();
        var backgroundSync = new BackgroundSyncService(apiClient, dbPath, config.SyncIntervalSeconds, config.CatalogSyncPageSize);
        var receiptPrinter = new Services.Printing.ReceiptPrinterService(config);

        var loginWindow = new LoginWindow(apiClient, authSession, dbPath, backgroundSync, receiptPrinter);
        loginWindow.Show();
    }
}
