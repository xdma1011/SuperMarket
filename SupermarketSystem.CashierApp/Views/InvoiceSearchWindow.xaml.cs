using System.Globalization;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using SupermarketSystem.CashierApp.Services;

namespace SupermarketSystem.CashierApp.Views;

/// <summary>
/// بحث الفواتير بالتاريخ والساعة والدقيقة (28/9/2026، طلب صاحب المشروع): بيرجع لتسجيل الكاميرا، بيشوف الزبون
/// طلع الساعة 14:32 وبيدوّر بين 14:25 و14:40 - وممكن يكتب اسم الصنف (أو باركوده) عشان يتأكد هل فات بالفاتورة
/// ولا ما انضرب أصلًا. الوقت المدخل بتوقيت الجهاز، وبينبعت للسيرفر UTC. أونلاين (الفواتير عند السيرفر)، وفرعك بس.
/// </summary>
public partial class InvoiceSearchWindow : Window
{
    private readonly ApiClient _apiClient;
    private readonly AuthSession _authSession;

    public InvoiceSearchWindow(ApiClient apiClient, AuthSession authSession)
    {
        InitializeComponent();
        _apiClient = apiClient;
        _authSession = authSession;
        DayPicker.SelectedDate = DateTime.Today;

        // افتراضيًا: آخر نص ساعة - الحالة الأشيع (زبون لسه طالع).
        var now = DateTime.Now;
        FromTimeBox.Text = now.AddMinutes(-30).Date == now.Date ? now.AddMinutes(-30).ToString("HH:mm") : "00:00";
        ToTimeBox.Text = now.ToString("HH:mm");

        Loaded += (_, _) => FromTimeBox.Focus();
    }

    private static bool TryParseTime(string text, out TimeSpan time)
    {
        var formats = new[] { @"h\:mm", @"hh\:mm", @"h\:mm\:ss", @"hh\:mm\:ss", "hhmm", "hh" };
        return TimeSpan.TryParseExact(text.Trim(), formats, CultureInfo.InvariantCulture, out time)
               && time >= TimeSpan.Zero && time < TimeSpan.FromDays(1);
    }

    private async Task SearchAsync()
    {
        if (DayPicker.SelectedDate is not { } day)
        {
            StatusText.Text = "اختار اليوم.";
            return;
        }

        if (!TryParseTime(FromTimeBox.Text, out var from) || !TryParseTime(ToTimeBox.Text, out var to))
        {
            StatusText.Text = "الوقت لازم يكون ساعة:دقيقة، مثلًا 14:25.";
            return;
        }

        if (to < from)
        {
            StatusText.Text = "وقت \"لـ\" قبل وقت \"من\".";
            return;
        }

        // "لـ 14:40" بتشمل الدقيقة كلها (لحد 14:40:59).
        var fromLocal = DateTime.SpecifyKind(day.Date + from, DateTimeKind.Local);
        var toLocal = DateTime.SpecifyKind(day.Date + to, DateTimeKind.Local);
        if (to.Seconds == 0)
        {
            toLocal = toLocal.AddSeconds(59.999);
        }

        SearchButton.IsEnabled = false;
        StatusText.Text = "جاري البحث...";
        ItemsGrid.ItemsSource = null;
        DetailHeaderText.Text = "اختار فاتورة من فوق لتشوف أصنافها.";

        var result = await _apiClient.SearchSaleInvoicesByTimeAsync(
            _authSession.BranchId, fromLocal.ToUniversalTime(), toLocal.ToUniversalTime(), ProductBox.Text, CancellationToken.None);
        SearchButton.IsEnabled = true;

        if (!result.Success || result.Response is null)
        {
            StatusText.Text = $"تعذّر البحث: {result.ErrorMessage}";
            ResultsGrid.ItemsSource = null;
            return;
        }

        var invoices = result.Response.Items.OrderBy(i => i.CreatedAtUtc).ToList();
        ResultsGrid.ItemsSource = invoices;

        var product = ProductBox.Text.Trim();
        StatusText.Text = invoices.Count switch
        {
            0 when product.Length > 0 => $"ما في ولا فاتورة فيها \"{product}\" بين {from:hh\\:mm} و{to:hh\\:mm} - يعني الصنف ما انضرب بهالوقت.",
            0 => $"ما في فواتير بين {from:hh\\:mm} و{to:hh\\:mm}.",
            _ when result.Response.TotalCount > invoices.Count => $"{result.Response.TotalCount} فاتورة (معروض أول {invoices.Count}) - ضيّق الوقت.",
            _ when product.Length > 0 => $"{invoices.Count} فاتورة فيها \"{product}\".",
            _ => $"{invoices.Count} فاتورة."
        };

        if (invoices.Count > 0)
        {
            ResultsGrid.SelectedIndex = 0;
        }
    }

    private async void ResultsGrid_SelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        if (ResultsGrid.SelectedItem is not SaleInvoiceListItemDto invoice)
        {
            return;
        }

        DetailHeaderText.Text = $"فاتورة {invoice.InvoiceNumber} · {invoice.CreatedAtLocal:HH:mm:ss} · جاري التحميل...";
        var detail = await _apiClient.GetSaleInvoiceByIdAsync(invoice.Id, CancellationToken.None);

        // المستخدم ممكن يكون نقل لفاتورة تانية وإحنا بنستنى.
        if (!ReferenceEquals(ResultsGrid.SelectedItem, invoice))
        {
            return;
        }

        if (!detail.Success || detail.Response is null)
        {
            DetailHeaderText.Text = $"تعذّر جلب الأصناف: {detail.ErrorMessage}";
            ItemsGrid.ItemsSource = null;
            return;
        }

        DetailHeaderText.Text =
            $"فاتورة {invoice.InvoiceNumber} · {invoice.CreatedAtLocal:HH:mm:ss} · {invoice.CashierName} · " +
            $"{detail.Response.Items.Count} صنف · {detail.Response.TotalAmount:0.000} د.أ ({detail.Response.StatusTitle})";
        ItemsGrid.ItemsSource = detail.Response.Items;
    }

    private async void SearchButton_Click(object sender, RoutedEventArgs e) => await SearchAsync();

    private async void Window_KeyDown(object sender, KeyEventArgs e)
    {
        if (e.Key == Key.Enter && !ResultsGrid.IsKeyboardFocusWithin)
        {
            e.Handled = true;
            await SearchAsync();
        }
        else if (e.Key == Key.Escape)
        {
            e.Handled = true;
            Close();
        }
    }

    private void CloseWindowButton_Click(object sender, RoutedEventArgs e) => Close();
}
