using System.Windows;
using System.Windows.Input;
using SupermarketSystem.CashierApp.Services;

namespace SupermarketSystem.CashierApp.Views;

public partial class LoginWindow : Window
{
    private readonly ApiClient _apiClient;
    private readonly AuthSession _authSession;

    private readonly string _dbPath;
    private readonly BackgroundSyncService _backgroundSync;
    private readonly Services.Printing.ReceiptPrinterService _receiptPrinter;
    private readonly string _adminScreenPassword;

    public LoginWindow(
        ApiClient apiClient, AuthSession authSession, string dbPath, BackgroundSyncService backgroundSync,
        Services.Printing.ReceiptPrinterService receiptPrinter, string adminScreenPassword)
    {
        InitializeComponent();
        _apiClient = apiClient;
        _authSession = authSession;
        _dbPath = dbPath;
        _backgroundSync = backgroundSync;
        _receiptPrinter = receiptPrinter;
        _adminScreenPassword = adminScreenPassword;
    }

    private async void LoginButton_Click(object sender, RoutedEventArgs e)
    {
        await AttemptLoginAsync();
    }

    private async void PasswordBox_KeyDown(object sender, KeyEventArgs e)
    {
        if (e.Key == Key.Enter)
        {
            await AttemptLoginAsync();
        }
    }

    private async Task AttemptLoginAsync()
    {
        var username = UsernameBox.Text.Trim();
        var password = PasswordBox.Password;

        if (string.IsNullOrWhiteSpace(username) || string.IsNullOrWhiteSpace(password))
        {
            ShowError("اسم المستخدم وكلمة السر مطلوبان.");
            return;
        }

        LoginButton.IsEnabled = false;
        StatusText.Text = "جارٍ تسجيل الدخول…";
        HideError();

        var result = await _apiClient.LoginAsync(username, password, CancellationToken.None);

        LoginButton.IsEnabled = true;
        StatusText.Text = "";

        if (!result.Success || result.Response is null)
        {
            ShowError(result.ErrorMessage ?? "تعذّر تسجيل الدخول.");
            return;
        }

        _authSession.SetSession(result.Response);
        _apiClient.SetAccessToken(result.Response.AccessToken);

        // بدء المزامنة التلقائية بالخلفية - أول لحظة عندنا فيها BranchId
        // وتوكن صالح. لو المستخدم بلا فرع افتراضي (حالة استثنائية جدًا)،
        // نتجاهل بدء المزامنة بدل ما نفجّر - البيع نفسه هيرفض لاحقًا
        // بوضوح بشاشة البيع لو صار هيك (فحص _authSession.BranchId موجود هناك).
        if (result.Response.BranchId is not null)
        {
            _backgroundSync.Start(result.Response.BranchId.Value);
        }

        // نافذة رئيسية مؤقتة (جلسة لاحقة رح تستبدلها بشاشة البيع
        // الفعلية) - الهدف هلق إثبات إن تسجيل الدخول شغّال كاملًا.
        var main = new MainWindow(_apiClient, _authSession, _dbPath, _receiptPrinter, _backgroundSync, _adminScreenPassword);
        main.Show();
        Close();
    }

    private void ShowError(string message)
    {
        ErrorText.Text = message;
        ErrorText.Visibility = Visibility.Visible;
    }

    private void HideError()
    {
        ErrorText.Visibility = Visibility.Collapsed;
    }
}
