using System.Globalization;
using System.Windows;
using System.Windows.Input;
using SupermarketSystem.CashierApp.Services;

namespace SupermarketSystem.CashierApp.Views;

/// <summary>
/// سحب شريك من شاشة البيع (قرار صاحب المشروع 21/9/2026): الشريك بيكبس الزر بنفسه ويثبت هويته لحظيًا (الكاشير
/// الموظف داخل بحسابه)، والمبلغ بيطلع من الصندوق (بينقص المتوقع بالتقفيل). أونلاين بس. كل سحب بيوصل تنبيه لصاحب المحل.
/// طرق التحقق (29/9/2026): يوزر وكلمة سر (دايمًا - محاولات غلط بتنعدّ بنفس قفل الدخول)، وحسب الإعدادات: كود تلغرام
/// (بينطلب لمبلغ محدد وما بيمشي لغيره) أو باركود الشريك الشخصي (كرت مطبوع من صفحة الشركاء).
/// </summary>
public partial class PartnerWithdrawalWindow : Window
{
    private readonly ApiClient _apiClient;
    private readonly Guid _branchId;

    /// <summary>مفتاح عدم التكرار - ثابت لهالنافذة، فكبسة مرتين أو إعادة محاولة بعد انقطاع ما بتسحب مرتين.</summary>
    private readonly Guid _clientRequestId = Guid.NewGuid();

    private Guid? _otpChallengeId;
    private decimal? _otpAmount;

    public string? ResultMessage { get; private set; }

    public PartnerWithdrawalWindow(ApiClient apiClient, Guid branchId)
    {
        InitializeComponent();
        _apiClient = apiClient;
        _branchId = branchId;
        Loaded += async (_, _) =>
        {
            AmountBox.Focus();
            await LoadVerificationOptionsAsync();
        };
    }

    /// <summary>بلا نت أو فشل: يوزر وكلمة سر بس (السحب نفسه بدّه نت أصلًا).</summary>
    private async Task LoadVerificationOptionsAsync()
    {
        var options = await _apiClient.GetPartnerVerificationOptionsAsync(_branchId, CancellationToken.None);
        if (options is null)
        {
            return;
        }

        if (options.TelegramOtpEnabled)
        {
            OtpMethodRadio.Visibility = Visibility.Visible;
            OtpPartnerCombo.ItemsSource = options.OtpPartners;
            if (options.OtpPartners.Count > 0)
            {
                OtpPartnerCombo.SelectedIndex = 0;
            }
            else
            {
                OtpStatusText.Text = "ما في شريك رقمه مربوط ببوت تلغرام - الرقم بينكتب من صفحة الشركاء، والشريك لازم يفتح البوت ويشارك رقمه.";
            }
        }

        if (options.BarcodeEnabled)
        {
            BarcodeMethodRadio.Visibility = Visibility.Visible;
        }
    }

    private void MethodRadio_Checked(object sender, RoutedEventArgs e)
    {
        // بينطلق أول مرة جوّا InitializeComponent قبل ما تنبني اللوحات.
        if (PasswordPanel is null || OtpPanel is null || BarcodePanel is null)
        {
            return;
        }

        PasswordPanel.Visibility = PasswordMethodRadio.IsChecked == true ? Visibility.Visible : Visibility.Collapsed;
        OtpPanel.Visibility = OtpMethodRadio.IsChecked == true ? Visibility.Visible : Visibility.Collapsed;
        BarcodePanel.Visibility = BarcodeMethodRadio.IsChecked == true ? Visibility.Visible : Visibility.Collapsed;
        ErrorText.Visibility = Visibility.Collapsed;

        if (BarcodeMethodRadio.IsChecked == true)
        {
            BarcodeBox.Focus();
        }
    }

    private bool TryReadAmount(out decimal amount)
    {
        if (!decimal.TryParse(AmountBox.Text.Trim().Replace(',', '.'), NumberStyles.Number, CultureInfo.InvariantCulture, out amount) || amount <= 0)
        {
            ShowError("المبلغ لازم يكون رقم أكبر من صفر.");
            AmountBox.Focus();
            return false;
        }

        amount = decimal.Round(amount, 3);
        return true;
    }

    private async void SendOtpButton_Click(object sender, RoutedEventArgs e)
    {
        ErrorText.Visibility = Visibility.Collapsed;
        if (!TryReadAmount(out var amount))
        {
            return;
        }

        if (OtpPartnerCombo.SelectedItem is not OtpPartnerDto partner)
        {
            ShowError("اختر الشريك.");
            return;
        }

        SendOtpButton.IsEnabled = false;
        var (challengeId, error) = await _apiClient.RequestPartnerOtpAsync(_branchId, partner.PartnerId, amount, CancellationToken.None);
        SendOtpButton.IsEnabled = true;
        if (challengeId is null)
        {
            ShowError(error ?? "ما انبعت الكود.");
            return;
        }

        _otpChallengeId = challengeId;
        _otpAmount = amount;
        OtpStatusText.Text = $"انبعت كود لـ{partner.FullName} على تلغرام لسحب {amount:0.000} د.أ - صالح 5 دقايق.";
        OtpCodeBox.Clear();
        OtpCodeBox.Focus();
    }

    private async void ConfirmButton_Click(object sender, RoutedEventArgs e) => await ConfirmAsync();

    private async Task ConfirmAsync()
    {
        ErrorText.Visibility = Visibility.Collapsed;
        if (!TryReadAmount(out var amount))
        {
            return;
        }

        string? username = null, password = null, otpCode = null, barcode = null;
        Guid? otpChallengeId = null;
        if (OtpMethodRadio.IsChecked == true)
        {
            if (_otpChallengeId is null)
            {
                ShowError("اكبس \"ابعت كود\" أول.");
                return;
            }

            if (_otpAmount != amount)
            {
                ShowError($"الكود انطلب لسحب {_otpAmount:0.000} د.أ - رجّع المبلغ أو اطلب كود جديد.");
                return;
            }

            otpCode = ToAsciiDigits(OtpCodeBox.Text);
            if (otpCode.Length != 6)
            {
                ShowError("اكتب الكود (6 أرقام) اللي وصل على تلغرام الشريك.");
                OtpCodeBox.Focus();
                return;
            }

            otpChallengeId = _otpChallengeId;
        }
        else if (BarcodeMethodRadio.IsChecked == true)
        {
            barcode = ToAsciiDigits(BarcodeBox.Password);
            if (barcode.Length == 0)
            {
                ShowError("امسح كرت الشريك.");
                BarcodeBox.Focus();
                return;
            }
        }
        else
        {
            if (string.IsNullOrWhiteSpace(UsernameBox.Text) || PasswordBox.Password.Length == 0)
            {
                ShowError("اكتب اسم المستخدم وكلمة السر تبع الشريك.");
                return;
            }

            username = UsernameBox.Text.Trim();
            password = PasswordBox.Password;
        }

        var confirm = MessageBox.Show(
            $"سحب {amount:0.000} د.أ من الصندوق؟", "تأكيد", MessageBoxButton.YesNo, MessageBoxImage.Question, MessageBoxResult.No);
        if (confirm != MessageBoxResult.Yes)
        {
            return;
        }

        ConfirmButton.IsEnabled = false;
        var (success, partnerName, newBalance, error) = await _apiClient.RecordVerifiedPartnerWithdrawalAsync(
            _branchId, username, password, amount,
            string.IsNullOrWhiteSpace(NotesBox.Text) ? null : NotesBox.Text.Trim(), _clientRequestId, CancellationToken.None,
            otpChallengeId, otpCode, barcode);
        ConfirmButton.IsEnabled = true;
        PasswordBox.Clear();
        BarcodeBox.Clear();

        if (!success)
        {
            ShowError(error ?? "ما انسجّل السحب.");
            return;
        }

        ResultMessage = $"💼 {partnerName} سحب {amount:0.000} د.أ من الصندوق - رصيده {newBalance:0.000}";
        DialogResult = true;
    }

    /// <summary>أرقام بس - الأرقام الهندية (٠-٩) بتتحوّل، وأي مسافة من الماسح بتنشال.</summary>
    private static string ToAsciiDigits(string raw) =>
        new(raw.Select(ch => ch is >= '٠' and <= '٩' ? (char)('0' + (ch - '٠')) : ch).Where(char.IsAsciiDigit).ToArray());

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
