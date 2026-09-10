using System.Globalization;
using System.Windows;
using System.Windows.Controls;
using SupermarketSystem.CashierApp.Services;

namespace SupermarketSystem.CashierApp.Views;

/// <summary>
/// كانت مفقودة كليًا بتطبيق الكاشير - CompleteCashClosing كان جاهزًا
/// بالباك إند من قبل، ومتاح من Admin Web، بس ما في طريقة تقفّل الصندوق
/// من جهاز الكاشير نفسه فعليًا نهاية الوردية. محمية بباسوورد الأدمن
/// المحلي (راجع SaleWindow.CashClosingButton_Click) لأن الصلاحية الفعلية
/// (CashClosing.Manage) أصلًا مو من صلاحيات دور "كاشير" الافتراضية.
/// </summary>
public partial class CashClosingWindow : Window
{
    private readonly ApiClient _apiClient;
    private readonly Guid _branchId;
    private readonly Dictionary<Guid, TextBox> _detailBoxes = new();

    public CashClosingWindow(ApiClient apiClient, Guid branchId, List<PaymentMethodDto> paymentMethods)
    {
        InitializeComponent();
        _apiClient = apiClient;
        _branchId = branchId;

        BusinessDateBox.Text = DateTime.Now.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture);

        foreach (var method in paymentMethods)
        {
            var row = new Grid { Margin = new Thickness(0, 0, 0, 8) };
            row.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
            row.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(10) });
            row.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(140) });

            var label = new TextBlock
            {
                Text = $"{method.Name} (اختياري)",
                FontSize = 12.5,
                VerticalAlignment = VerticalAlignment.Center
            };
            Grid.SetColumn(label, 0);

            var box = new TextBox { Height = 34, VerticalContentAlignment = VerticalAlignment.Center, Padding = new Thickness(6, 0, 6, 0) };
            Grid.SetColumn(box, 2);

            row.Children.Add(label);
            row.Children.Add(box);
            PaymentMethodsPanel.Children.Add(row);

            _detailBoxes[method.Id] = box;
        }
    }

    private async void SubmitButton_Click(object sender, RoutedEventArgs e)
    {
        if (!DateOnly.TryParse(BusinessDateBox.Text.Trim(), CultureInfo.InvariantCulture, DateTimeStyles.None, out var businessDate))
        {
            ResultText.Text = "تاريخ غير صالح - استخدم الصيغة yyyy-MM-dd.";
            return;
        }

        if (!decimal.TryParse(CountedCashBox.Text.Trim(), NumberStyles.Number, CultureInfo.InvariantCulture, out var countedCash) || countedCash < 0)
        {
            ResultText.Text = "المبلغ المعدود غير صالح - رقم موجب أو صفر.";
            return;
        }

        var countedDetails = new List<CompleteCashClosingCountedDetailDto>();
        foreach (var (paymentMethodId, box) in _detailBoxes)
        {
            var text = box.Text.Trim();
            if (text.Length == 0)
            {
                continue;
            }

            if (!decimal.TryParse(text, NumberStyles.Number, CultureInfo.InvariantCulture, out var amount) || amount < 0)
            {
                ResultText.Text = "أحد مبالغ العدّ الإضافي غير صالح.";
                return;
            }

            countedDetails.Add(new CompleteCashClosingCountedDetailDto(paymentMethodId, amount));
        }

        SubmitButton.IsEnabled = false;
        ResultText.Text = "جارٍ الحفظ…";

        var result = await _apiClient.CompleteCashClosingAsync(_branchId, businessDate, countedCash, countedDetails, CancellationToken.None);

        SubmitButton.IsEnabled = true;

        if (!result.Success || result.Response is null)
        {
            ResultText.Text = $"تعذّر إتمام التقفيل: {result.ErrorMessage}";
            return;
        }

        var response = result.Response;
        ResultText.Text =
            $"تم التقفيل بنجاح.\nالمتوقَّع: {response.ExpectedCash:0.00}\nالمعدود: {response.CountedCash:0.00}\nالفرق: {response.Variance:0.00}";
    }

    private void CloseButton_Click(object sender, RoutedEventArgs e)
    {
        Close();
    }
}
