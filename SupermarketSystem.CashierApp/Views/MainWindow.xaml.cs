using System.Windows;
using SupermarketSystem.CashierApp.Services;

namespace SupermarketSystem.CashierApp.Views;

public partial class MainWindow : Window
{
    private readonly ApiClient _apiClient;
    private readonly AuthSession _authSession;
    private readonly string _dbPath;
    private readonly Services.Printing.ReceiptPrinterService _receiptPrinter;

    public MainWindow(ApiClient apiClient, AuthSession authSession, string dbPath, Services.Printing.ReceiptPrinterService receiptPrinter)
    {
        InitializeComponent();
        _apiClient = apiClient;
        _authSession = authSession;
        _dbPath = dbPath;
        _receiptPrinter = receiptPrinter;
        WelcomeText.Text = $"مرحبًا، {authSession.FullName}";
    }

    private void StartSaleButton_Click(object sender, RoutedEventArgs e)
    {
        var saleWindow = new SaleWindow(_apiClient, _authSession, _dbPath, _receiptPrinter);
        saleWindow.Show();
        Close();
    }
}
