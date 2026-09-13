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
        var saleWindow = new SaleWindow(_apiClient, _authSession, _dbPath, _receiptPrinter, _backgroundSync, _adminScreenPassword);
        saleWindow.Show();
        Close();
    }

    /// <summary>
    /// زر صغير غير ملفت (زاوية الشاشة، بلا نص واضح) - الحماية الفعلية
    /// مش إخفاء الزر (أي كاشير فضولي بيلاقيه بثانيتين)، الحماية هي
    /// الباسوورد نفسه. لو غلط، ما في أي أثر أو رسالة تكشف وجود شاشة إدارية أصلًا.
    /// </summary>
    private void UploadInvoiceButton_Click(object sender, RoutedEventArgs e)
    {
        var uploadWindow = new UploadInvoiceWindow(_apiClient, _authSession) { Owner = this };
        uploadWindow.ShowDialog();
    }

    /// <summary>
    /// كانت مفقودة كليًا - راجع تعليق ReturnWindow.xaml.cs لتفاصيل الفجوة
    /// وقرارات التصميم. Modal بلا إغلاق MainWindow (زي UploadInvoiceWindow،
    /// لا زي StartSaleButton) - عملية ثانوية عرضية، لا الشاشة الرئيسية للكاشير.
    /// </summary>
    private void ReturnButton_Click(object sender, RoutedEventArgs e)
    {
        var returnWindow = new ReturnWindow(_apiClient, _authSession) { Owner = this };
        returnWindow.ShowDialog();
    }

    /// <summary>نفس منطق ReturnButton_Click بالضبط - راجع تعليق VoidSaleWindow.xaml.cs.</summary>
    private void VoidSaleButton_Click(object sender, RoutedEventArgs e)
    {
        var voidWindow = new VoidSaleWindow(_apiClient, _authSession) { Owner = this };
        voidWindow.ShowDialog();
    }

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
