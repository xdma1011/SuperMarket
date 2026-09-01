using System.IO;
using System.Windows;
using System.Windows.Media.Imaging;
using Microsoft.Win32;
using SupermarketSystem.CashierApp.Services;

namespace SupermarketSystem.CashierApp.Views;

/// <summary>
/// رفع فاتورة عبر الذكاء الاصطناعي من تطبيق الكاشير مباشرة - يستخدم
/// نفس صلاحية Purchasing.CreateDraft اللي عند دور الكاشير افتراضيًا
/// بالباك إند؛ لا مراجعة هون، بس رفع وحفظ كمسودة (راجع CLAUDE.md ونقاش
/// صاحب المشروع: "الكاشير يقدر يضيفها بس بلا قبول أو موافقة"). المسح
/// الضوئي اختياري بالكامل - لو ما في سكانر متصل، يبقى "اختيار ملف" (يشمل
/// صور محوّلة من الموبايل لهالجهاز) الطريقة العادية.
///
/// خانة "دفعت الآن" اختيارية - لو الكاشير سلّم كاش فعليًا للمورد لحظة
/// الاستلام، المبلغ ينكتب فورًا بدرج الكاش (لا ينتظر مراجعة لاحقة) - هذا
/// يحل مشكلة توقيت حقيقية بتقفيل الصندوق اليومي (راجع نقاش صاحب
/// المشروع).
/// </summary>
public partial class UploadInvoiceWindow : Window
{
    private readonly ApiClient _apiClient;
    private readonly Guid? _branchId;

    private byte[]? _selectedImageBytes;
    private string _selectedFileName = "invoice.jpg";
    private string _selectedContentType = "image/jpeg";

    public UploadInvoiceWindow(ApiClient apiClient, AuthSession authSession)
    {
        InitializeComponent();
        _apiClient = apiClient;
        _branchId = authSession.BranchId;

        if (_branchId is null)
        {
            StatusText.Text = "لا يوجد فرع مرتبط بحسابك - لا يمكن رفع فاتورة.";
            BrowseButton.IsEnabled = false;
            ScanButton.IsEnabled = false;
        }

        _ = LoadPaymentMethodsAsync();
    }

    private async Task LoadPaymentMethodsAsync()
    {
        var methods = await _apiClient.GetPaymentMethodsAsync(CancellationToken.None);
        PaymentMethodComboBox.ItemsSource = methods;
        if (methods.Count > 0)
        {
            PaymentMethodComboBox.SelectedIndex = 0;
        }
    }

    private void PaidNowCheckBox_Changed(object sender, RoutedEventArgs e)
    {
        PaidNowFieldsGrid.Visibility = PaidNowCheckBox.IsChecked == true ? Visibility.Visible : Visibility.Collapsed;
    }

    private void BrowseButton_Click(object sender, RoutedEventArgs e)
    {
        var dialog = new OpenFileDialog
        {
            Filter = "صور (*.jpg;*.jpeg;*.png;*.webp)|*.jpg;*.jpeg;*.png;*.webp",
            Title = "اختر صورة الفاتورة"
        };

        if (dialog.ShowDialog(this) != true)
        {
            return;
        }

        try
        {
            var bytes = File.ReadAllBytes(dialog.FileName);
            SetSelectedImage(bytes, Path.GetFileName(dialog.FileName), GuessContentType(dialog.FileName));
            StatusText.Text = string.Empty;
        }
        catch (Exception ex)
        {
            StatusText.Text = $"تعذّر قراءة الملف: {ex.Message}";
        }
    }

    private void ScanButton_Click(object sender, RoutedEventArgs e)
    {
        StatusText.Text = "جارٍ فتح حوار المسح الضوئي…";

        var result = WiaScannerService.TryScan();
        if (!result.Success || result.ImageBytes is null)
        {
            StatusText.Text = result.ErrorMessage ?? "تعذّر المسح الضوئي.";
            return;
        }

        SetSelectedImage(result.ImageBytes, result.FileName ?? "scan.jpg", "image/jpeg");
        StatusText.Text = string.Empty;
    }

    private void SetSelectedImage(byte[] bytes, string fileName, string contentType)
    {
        _selectedImageBytes = bytes;
        _selectedFileName = fileName;
        _selectedContentType = contentType;

        var bitmap = new BitmapImage();
        using (var stream = new MemoryStream(bytes))
        {
            bitmap.BeginInit();
            bitmap.CacheOption = BitmapCacheOption.OnLoad;
            bitmap.StreamSource = stream;
            bitmap.EndInit();
        }
        bitmap.Freeze();

        PreviewImage.Source = bitmap;
        PreviewImage.Visibility = Visibility.Visible;
        NoImageText.Visibility = Visibility.Collapsed;
        UploadButton.IsEnabled = _branchId is not null;
    }

    private static string GuessContentType(string fileName) => Path.GetExtension(fileName).ToLowerInvariant() switch
    {
        ".png" => "image/png",
        ".webp" => "image/webp",
        _ => "image/jpeg"
    };

    private async void UploadButton_Click(object sender, RoutedEventArgs e)
    {
        if (_selectedImageBytes is null || _branchId is null)
        {
            return;
        }

        decimal? paidNowAmount = null;
        Guid? paidNowPaymentMethodId = null;

        if (PaidNowCheckBox.IsChecked == true)
        {
            if (!decimal.TryParse(PaidNowAmountText.Text, out var amount) || amount <= 0)
            {
                StatusText.Text = "أدخل مبلغًا موجبًا صحيحًا لخانة \"دفعت الآن\".";
                return;
            }

            if (PaymentMethodComboBox.SelectedItem is not PaymentMethodDto selectedMethod)
            {
                StatusText.Text = "اختر طريقة الدفع لخانة \"دفعت الآن\".";
                return;
            }

            paidNowAmount = amount;
            paidNowPaymentMethodId = selectedMethod.Id;
        }

        UploadButton.IsEnabled = false;
        BrowseButton.IsEnabled = false;
        ScanButton.IsEnabled = false;
        StatusText.Text = "جارٍ الرفع والقراءة الآلية… قد تستغرق لحظات.";

        var result = await _apiClient.UploadPurchaseInvoiceDraftAsync(
            _branchId.Value, _selectedImageBytes, _selectedFileName, _selectedContentType,
            paidNowAmount, paidNowPaymentMethodId, CancellationToken.None);

        if (result.Success)
        {
            StatusText.Text = paidNowAmount is not null
                ? "تم رفع الفاتورة وقراءتها، وسُجّل المبلغ المدفوع بدرج الكاش فورًا - بانتظار مراجعة الإدارة."
                : "تم رفع الفاتورة وقراءتها - بانتظار مراجعة الإدارة قبل اعتمادها.";
            _selectedImageBytes = null;
            PaidNowCheckBox.IsChecked = false;
            PaidNowAmountText.Text = string.Empty;
        }
        else
        {
            StatusText.Text = $"تعذّر رفع الفاتورة: {result.ErrorMessage}";
        }

        BrowseButton.IsEnabled = true;
        ScanButton.IsEnabled = true;
        UploadButton.IsEnabled = _selectedImageBytes is not null;
    }

    private void CloseButton_Click(object sender, RoutedEventArgs e) => Close();
}
