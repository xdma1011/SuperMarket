using System.Windows;
using SupermarketSystem.CashierApp.Services;

namespace SupermarketSystem.CashierApp.Views;

public partial class MainWindow : Window
{
    private readonly ApiClient _apiClient;
    private readonly AuthSession _authSession;
    private readonly string _dbPath;
    private readonly Services.Printing.ReceiptPrinterService _receiptPrinter;
    private readonly BackgroundSyncService _backgroundSync;
    private readonly string _adminScreenPassword;

    public MainWindow(
        ApiClient apiClient, AuthSession authSession, string dbPath,
        Services.Printing.ReceiptPrinterService receiptPrinter, BackgroundSyncService backgroundSync,
        string adminScreenPassword)
    {
        InitializeComponent();
        _apiClient = apiClient;
        _authSession = authSession;
        _dbPath = dbPath;
        _receiptPrinter = receiptPrinter;
        _backgroundSync = backgroundSync;
        _adminScreenPassword = adminScreenPassword;
        WelcomeText.Text = $"مرحبًا، {authSession.FullName}";
    }

    private void StartSaleButton_Click(object sender, RoutedEventArgs e)
    {
        var saleWindow = new SaleWindow(_apiClient, _authSession, _dbPath, _receiptPrinter, _backgroundSync);
        saleWindow.Show();
        Close();
    }

    /// <summary>
    /// زر صغير غير ملفت (زاوية الشاشة، بلا نص واضح) - الحماية الفعلية
    /// مش إخفاء الزر (أي كاشير فضولي بيلاقيه بثانيتين)، الحماية هي
    /// الباسوورد نفسه. لو غلط، ما في أي أثر أو رسالة تكشف وجود شاشة إدارية أصلًا.
    /// </summary>
    private void AdminAccessButton_Click(object sender, RoutedEventArgs e)
    {
        var passwordPrompt = new AdminPasswordWindow { Owner = this };
        if (passwordPrompt.ShowDialog() != true)
        {
            return;
        }

        if (passwordPrompt.EnteredPassword != _adminScreenPassword)
        {
            return;
        }

        var queueWindow = new PendingQueueWindow(_dbPath, _backgroundSync) { Owner = this };
        queueWindow.ShowDialog();
    }
}
