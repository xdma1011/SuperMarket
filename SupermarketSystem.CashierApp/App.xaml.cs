using System.IO;
using System.Text;
using System.Windows;
using Microsoft.EntityFrameworkCore;
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
        // جدول جديد كان رح يظل غايب للأبد بدون Migration حقيقية.
        using (var db = new LocalDbContext(dbPath))
        {
            BaselineLegacyEnsureCreatedDatabase(db);
            db.Database.Migrate();
        }

        // الساعة الموثوقة (بند 22): آخر مرجع وقت من السيرفر محفوظ محليًا - لازم يتحمّل قبل أي بيع.
        Services.TrustedClock.Instance.Load(dbPath);

        var apiClient = new ApiClient(config);
        var authSession = new AuthSession();
        var backgroundSync = new BackgroundSyncService(apiClient, dbPath, config.SyncIntervalSeconds, config.CatalogSyncPageSize);
        var receiptPrinter = new Services.Printing.ReceiptPrinterService(config);

        // Fire-and-forget: اسم المحل تفصيلة تجميلية بالفاتورة، ما تستاهل
        // تأخير فتح شاشة تسجيل الدخول لحد ما تجاوب السيرفر. لو فشل (بلا
        // نت لحظة الإقلاع)، يضل يستخدم آخر نسخة مخزَّنة محليًا (أو لا شي).
        _ = RefreshStoreBrandingCacheAsync(apiClient, dataDir);
        _ = RefreshPaymentSettingsCacheAsync(apiClient, dataDir);

        // التوكن بيتجدّد تلقائيًا (ApiClient) - الجلسة المحلية لازم تمشي معه (الخروج بيبعت الـrefresh token الجديد).
        apiClient.TokensRefreshed += (_, tokens) => authSession.UpdateTokens(tokens);

        // السيرفر رفض تجديد الجلسة: انلغت من الإدارة، أو المستخدم انوقف، أو الـrefresh خلص - رجوع لشاشة الدخول.
        apiClient.SessionEnded += (_, _) => Dispatcher.InvokeAsync(() =>
            ReturnToLoginAfterSessionEnded(apiClient, authSession, dbPath, backgroundSync, receiptPrinter, config.AdminScreenPassword));

        var loginWindow = new LoginWindow(apiClient, authSession, dbPath, backgroundSync, receiptPrinter, config.AdminScreenPassword);
        loginWindow.Show();
    }

    /// <summary>
    /// الكاشير المطرود (29/9/2026): السلة المفتوحة بتنحفظ بالمعلّقة، المزامنة بتوقف، وبترجع شاشة دخول نظيفة. البيعات
    /// المعلّقة بطابور local.db ما بتنلمس - بتنبعت بعد أول دخول. شاشة الدخول بتنفتح قبل ما تتسكر الباقي (غير هيك
    /// التطبيق بيطفي مع آخر نافذة).
    /// </summary>
    private void ReturnToLoginAfterSessionEnded(
        ApiClient apiClient, AuthSession authSession, string dbPath, BackgroundSyncService backgroundSync,
        Services.Printing.ReceiptPrinterService receiptPrinter, string adminScreenPassword)
    {
        if (!authSession.IsLoggedIn)
        {
            return; // خرج أصلًا
        }

        var heldCart = false;
        foreach (var saleWindow in Windows.OfType<SaleWindow>().ToList())
        {
            heldCart |= saleWindow.HoldCartForSessionEnd();
        }

        backgroundSync.Stop();
        authSession.Clear();
        apiClient.ClearTokens();

        var openWindows = Windows.Cast<Window>().ToList();
        var loginWindow = new LoginWindow(apiClient, authSession, dbPath, backgroundSync, receiptPrinter, adminScreenPassword);
        loginWindow.Show();
        foreach (var window in openWindows)
        {
            window.Close();
        }

        MessageBox.Show(loginWindow,
            "انتهت جلستك من السيرفر (انلغت من الإدارة أو الحساب انوقف) - ادخل من جديد.\n" +
            "البيعات اللي ما انبعتت محفوظة وبتنبعت بعد الدخول." +
            (heldCart ? "\nالفاتورة اللي كانت مفتوحة انحفظت بـ\"المعلّقة\"." : ""),
            "انتهت الجلسة", MessageBoxButton.OK, MessageBoxImage.Information);
    }

    // local.db انعملت بـEnsureCreated() (قبل ما تنضاف الـMigrations) فيها
    // الجداول بس بلا أي سطر بـ__EFMigrationsHistory - فـMigrate() بيحاول
    // ينشئ الجداول من جديد وبيوقع ("table PaymentMethods already exists").
    // سكيما EnsureCreated القديمة مطابقة حرفيًا لـInitialLocalSchema، فبنسجّلها
    // كمطبَّقة بدل ما نحذف الملف (ممكن يكون فيه بيعات معلّقة ما انبعتت).
    private const string InitialLocalSchemaMigrationId = "20260924111858_InitialLocalSchema";

    private static void BaselineLegacyEnsureCreatedDatabase(LocalDbContext db)
    {
        var connection = db.Database.GetDbConnection();
        connection.Open();
        try
        {
            using var check = connection.CreateCommand();
            check.CommandText =
                "SELECT (SELECT COUNT(*) FROM sqlite_master WHERE type='table' AND name='PendingSales'), " +
                "(SELECT COUNT(*) FROM sqlite_master WHERE type='table' AND name='__EFMigrationsHistory')";
            using (var reader = check.ExecuteReader())
            {
                reader.Read();
                var hasLegacyTables = reader.GetInt64(0) > 0;
                var hasHistoryTable = reader.GetInt64(1) > 0;
                if (!hasLegacyTables)
                {
                    return; // قاعدة جديدة فاضية - Migrate() بتتكفّل فيها عادي
                }

                if (hasHistoryTable)
                {
                    reader.Close();
                    using var count = connection.CreateCommand();
                    count.CommandText = "SELECT COUNT(*) FROM \"__EFMigrationsHistory\"";
                    if ((long)count.ExecuteScalar()! > 0)
                    {
                        return; // مسجَّلة أصلًا
                    }
                }
            }

            using var baseline = connection.CreateCommand();
            baseline.CommandText =
                "CREATE TABLE IF NOT EXISTS \"__EFMigrationsHistory\" (" +
                "\"MigrationId\" TEXT NOT NULL CONSTRAINT \"PK___EFMigrationsHistory\" PRIMARY KEY, " +
                "\"ProductVersion\" TEXT NOT NULL); " +
                "INSERT INTO \"__EFMigrationsHistory\" (\"MigrationId\", \"ProductVersion\") VALUES ($id, $ver);";
            var idParam = baseline.CreateParameter();
            idParam.ParameterName = "$id";
            idParam.Value = InitialLocalSchemaMigrationId;
            baseline.Parameters.Add(idParam);
            var verParam = baseline.CreateParameter();
            verParam.ParameterName = "$ver";
            verParam.Value = Microsoft.EntityFrameworkCore.Infrastructure.ProductInfo.GetVersion();
            baseline.Parameters.Add(verParam);
            baseline.ExecuteNonQuery();
        }
        finally
        {
            connection.Close();
        }
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
