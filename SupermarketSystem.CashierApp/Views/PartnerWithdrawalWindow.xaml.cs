using System.Globalization;
using System.Windows;
using System.Windows.Input;
using SupermarketSystem.CashierApp.Services;

namespace SupermarketSystem.CashierApp.Views;

/// <summary>
/// سحب شريك من شاشة البيع (قرار صاحب المشروع 21/9/2026): الشريك بيكبس الزر بنفسه، بيكتب يوزره وكلمة سره
/// (تحقق هوية لحظي - الكاشير الموظف داخل بحسابه)، والمبلغ بيطلع من الصندوق (بينقص المتوقع بالتقفيل). محاولات
/// كلمة سر غلط بتنعدّ بنفس قفل الدخول. أونلاين بس. كل سحب بيوصل تنبيه لصاحب المحل.
/// </summary>
public partial class PartnerWithdrawalWindow : Window
{
    private readonly ApiClient _apiClient;
    private readonly Guid _branchId;

    /// <summary>مفتاح عدم التكرار - ثابت لهالنافذة، فكبسة مرتين أو إعادة محاولة بعد انقطاع ما بتسحب مرتين.</summary>
    private readonly Guid _clientRequestId = Guid.NewGuid();

    public string? ResultMessage { get; private set; }

    public PartnerWithdrawalWindow(ApiClient apiClient, Guid branchId)
    {
        InitializeComponent();
        _apiClient = apiClient;
        _branchId = branchId;
        Loaded += (_, _) => UsernameBox.Focus();
    }

    private async void ConfirmButton_Click(object sender, RoutedEventArgs e) => await ConfirmAsync();

    private async Task ConfirmAsync()
    {
        ErrorText.Visibility = Visibility.Collapsed;

        if (string.IsNullOrWhiteSpace(UsernameBox.Text) || PasswordBox.Password.Length == 0)
        {
            ShowError("اكتب اسم المستخدم وكلمة السر تبع الشريك.");
            return;
        }

        if (!decimal.TryParse(AmountBox.Text.Trim().Replace(',', '.'), NumberStyles.Number, CultureInfo.InvariantCulture, out var amount) || amount <= 0)
        {
            ShowError("المبلغ لازم يكون رقم أكبر من صفر.");
            AmountBox.Focus();
            return;
        }

        var confirm = MessageBox.Show(
            $"سحب {amount:0.000} د.أ من الصندوق؟", "تأكيد", MessageBoxButton.YesNo, MessageBoxImage.Question, MessageBoxResult.No);
        if (confirm != MessageBoxResult.Yes)
        {
            return;
        }

        ConfirmButton.IsEnabled = false;
        var (success, partnerName, newBalance, error) = await _apiClient.RecordVerifiedPartnerWithdrawalAsync(
            _branchId, UsernameBox.Text.Trim(), PasswordBox.Password, amount,
            string.IsNullOrWhiteSpace(NotesBox.Text) ? null : NotesBox.Text.Trim(), _clientRequestId, CancellationToken.None);
        ConfirmButton.IsEnabled = true;
        PasswordBox.Clear();

        if (!success)
        {
            ShowError(error ?? "ما انسجّل السحب.");
            return;
        }

        ResultMessage = $"💼 {partnerName} سحب {amount:0.000} د.أ من الصندوق - رصيده {newBalance:0.000}";
        DialogResult = true;
    }

    private void ShowError(string message)
    {
        ErrorText.Text = message;
        ErrorText.Visibility = Visibility.Visible;
    }

    private void CancelButton_Click(object sender, RoutedEventArgs e) => DialogResult = false;

    private async void Window_KeyDown(object sender, KeyEventArgs e)
    {
        if (e.Key == Key.Escape)
        {
            e.Handled = true;
            DialogResult = false;
        }
        else if (e.Key == Key.Enter && ConfirmButton.IsEnabled)
        {
            e.Handled = true;
            await ConfirmAsync();
        }
    }
}
