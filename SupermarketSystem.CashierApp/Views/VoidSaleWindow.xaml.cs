using System.Globalization;
using System.Text.Json;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using SupermarketSystem.CashierApp.Services;

namespace SupermarketSystem.CashierApp.Views;

/// <summary>
/// كانت مفقودة كليًا — نفس فجوة شاشة الإرجاع بالضبط، بس لإلغاء فاتورة
/// كاملة (POST /api/v1/sales/{id}/void، صلاحية Sales.Void موجودة أصلًا
/// لدور "كاشير" الافتراضي). راجع تعليق ReturnWindow لتفاصيل قرارات
/// التصميم المشتركة (أونلاين فقط، بلا طابور محلي).
///
/// أبسط من الإرجاع بطبيعتها - الإلغاء يشمل الفاتورة كاملة (لا اختيار
/// أصناف أو كميات جزئية)، فالشاشة بس: بحث → عرض ملخّص القراءة فقط →
/// سبب + ملاحظة → تأكيد → تنفيذ.
/// </summary>
public partial class VoidSaleWindow : Window
{
    private readonly ApiClient _apiClient;
    private readonly AuthSession _authSession;

    private Guid? _loadedInvoiceId;
    private string _loadedInvoiceNumber = string.Empty;

    public VoidSaleWindow(ApiClient apiClient, AuthSession authSession)
    {
        InitializeComponent();
        _apiClient = apiClient;
        _authSession = authSession;

        Loaded += VoidSaleWindow_Loaded;
    }

    private void VoidSaleWindow_Loaded(object sender, RoutedEventArgs e)
    {
        ReasonCombo.ItemsSource = new[]
        {
            new ReasonOption(VoidReasonDto.CashierError, "خطأ من الكاشير"),
            new ReasonOption(VoidReasonDto.CustomerCancelled, "الزبون ألغى الشراء"),
            new ReasonOption(VoidReasonDto.SystemError, "خطأ نظام"),
            new ReasonOption(VoidReasonDto.Other, "سبب آخر")
        };
        ReasonCombo.SelectedIndex = 0;

        SearchTermBox.Focus();
    }

    private void CloseWindowButton_Click(object sender, RoutedEventArgs e)
    {
        Close();
    }

    private async void SearchTermBox_KeyDown(object sender, KeyEventArgs e)
    {
        if (e.Key == Key.Enter)
        {
            await RunSearchAsync();
        }
    }

    private async void SearchButton_Click(object sender, RoutedEventArgs e)
    {
        await RunSearchAsync();
    }

    private async Task RunSearchAsync()
    {
        HideError();
        SearchButton.IsEnabled = false;

        var result = await _apiClient.SearchSaleInvoicesAsync(SearchTermBox.Text.Trim(), _authSession.BranchId, CancellationToken.None);

        SearchButton.IsEnabled = true;

        if (!result.Success || result.Response is null)
        {
            ShowError($"تعذّر البحث: {ExtractErrorDetail(result.ErrorMessage)}");
            return;
        }

        var rows = result.Response.Items.Select(i => new SearchResultRow
        {
            Id = i.Id,
            InvoiceNumber = i.InvoiceNumber,
            StatusTitle = i.StatusTitle,
            TotalAmount = i.TotalAmount,
            CreatedAtUtc = i.CreatedAtUtc,
            CustomerDisplay = BuildCustomerDisplay(i.CustomerName, i.CustomerPhone)
        }).ToList();

        SearchResultsList.ItemsSource = rows;
        NoSearchResultsText.Visibility = rows.Count == 0 ? Visibility.Visible : Visibility.Collapsed;
    }

    private static string BuildCustomerDisplay(string? name, string? phone)
    {
        if (string.IsNullOrWhiteSpace(name) && string.IsNullOrWhiteSpace(phone))
        {
            return "بلا زبون مسجَّل";
        }

        return $"{name} {phone}".Trim();
    }

    private async void SearchResultsList_SelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        if (SearchResultsList.SelectedItem is not SearchResultRow selected)
        {
            return;
        }

        await LoadInvoiceAsync(selected.Id);
    }

    private async Task LoadInvoiceAsync(Guid saleInvoiceId)
    {
        HideError();
        _loadedInvoiceId = null;
        _loadedInvoiceNumber = string.Empty;
        ItemsSummaryPanel.Children.Clear();
        InvoiceHeaderText.Text = "جارٍ تحميل تفاصيل الفاتورة...";

        var result = await _apiClient.GetSaleInvoiceByIdAsync(saleInvoiceId, CancellationToken.None);

        if (!result.Success || result.Response is null)
        {
            InvoiceHeaderText.Text = "تعذّر تحميل تفاصيل الفاتورة.";
            ShowError(ExtractErrorDetail(result.ErrorMessage));
            return;
        }

        var invoice = result.Response;
        _loadedInvoiceId = invoice.Id;
        _loadedInvoiceNumber = invoice.InvoiceNumber;

        InvoiceHeaderText.Text =
            $"فاتورة {invoice.InvoiceNumber} — الحالة: {invoice.StatusTitle} — الإجمالي: {invoice.TotalAmount:0.00} — إجمالي مُرجَع سابقًا: {invoice.TotalReturnedAmount:0.00}";

        if (invoice.StatusTitle != "مكتملة")
        {
            var warning = new TextBlock
            {
                Text = $"⚠ حالة الفاتورة الحالية '{invoice.StatusTitle}' - الإلغاء متاح فقط لفاتورة 'مكتملة' بلا إرجاع مسجَّل عليها. السيرفر هو الحكم النهائي.",
                Foreground = (System.Windows.Media.Brush)Application.Current.Resources["RedBrush"],
                FontSize = 12.5,
                TextWrapping = TextWrapping.Wrap,
                Margin = new Thickness(0, 0, 0, 10)
            };
            ItemsSummaryPanel.Children.Add(warning);
        }

        foreach (var item in invoice.Items)
        {
            var line = new TextBlock
            {
                Text = $"{item.ProductName} — الكمية: {item.Quantity:0.###} × {item.UnitPriceSnapshot:0.00} = {item.LineTotal:0.00}",
                FontSize = 13,
                Margin = new Thickness(0, 0, 0, 6)
            };
            ItemsSummaryPanel.Children.Add(line);
        }
    }

    private async void VoidButton_Click(object sender, RoutedEventArgs e)
    {
        HideError();

        if (_loadedInvoiceId is not { } invoiceId)
        {
            ShowError("اختر فاتورة أولًا من نتائج البحث.");
            return;
        }

        if (ReasonCombo.SelectedItem is not ReasonOption selectedReason)
        {
            ShowError("اختر سبب الإلغاء.");
            return;
        }

        var confirm = MessageBox.Show(
            $"إلغاء نهائي لفاتورة '{_loadedInvoiceNumber}' - رح يُعكس المخزون والدفعات وحركة الدرج فورًا. أكيد الإلغاء؟",
            "تأكيد إلغاء الفاتورة", MessageBoxButton.YesNo, MessageBoxImage.Warning, MessageBoxResult.No);

        if (confirm != MessageBoxResult.Yes)
        {
            return;
        }

        VoidButton.IsEnabled = false;

        var request = new VoidSaleRequestDto(
            selectedReason.Value,
            string.IsNullOrWhiteSpace(NotesBox.Text) ? null : NotesBox.Text.Trim());

        var result = await _apiClient.VoidSaleAsync(invoiceId, request, CancellationToken.None);

        VoidButton.IsEnabled = true;

        if (!result.Success || result.Response is null)
        {
            ShowError($"تعذّر إلغاء الفاتورة: {ExtractErrorDetail(result.ErrorMessage)}");
            return;
        }

        var response = result.Response;

        MessageBox.Show(
            $"تم إلغاء الفاتورة '{response.InvoiceNumber}' بنجاح.\n" +
            $"حركات مخزون معكوسة: {response.StockMovementsReversed}\n" +
            $"دفعات معكوسة: {response.PaymentsReversed}\n" +
            $"كاش أُعيد للدرج: {response.CashReturnedToDrawer:0.00}",
            "تم", MessageBoxButton.OK, MessageBoxImage.Information);

        NotesBox.Clear();
        SearchResultsList.ItemsSource = null;
        ItemsSummaryPanel.Children.Clear();
        InvoiceHeaderText.Text = "اختر فاتورة من نتائج البحث أعلاه.";
        _loadedInvoiceId = null;
    }

    /// <summary>نفس منطق SaleWindow.ExtractErrorDetail حرفيًا (نسخ مقصود - SaleWindow ممنوع لمسه بهذه الدفعة).</summary>
    private static string ExtractErrorDetail(string? rawErrorMessage)
    {
        if (string.IsNullOrWhiteSpace(rawErrorMessage))
        {
            return "سبب غير معروف.";
        }

        try
        {
            var jsonStart = rawErrorMessage.IndexOf('{');
            if (jsonStart < 0)
            {
                return rawErrorMessage;
            }

            using var document = JsonDocument.Parse(rawErrorMessage[jsonStart..]);
            if (document.RootElement.TryGetProperty("detail", out var detail) && detail.ValueKind == JsonValueKind.String)
            {
                return detail.GetString() ?? rawErrorMessage;
            }
        }
        catch (JsonException)
        {
            // شكل غير متوقَّع - نرجع النص الخام تحت (fallback بالأسفل).
        }

        return rawErrorMessage;
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

    private sealed class SearchResultRow
    {
        public Guid Id { get; init; }
        public string InvoiceNumber { get; init; } = string.Empty;
        public string StatusTitle { get; init; } = string.Empty;
        public decimal TotalAmount { get; init; }
        public DateTime CreatedAtUtc { get; init; }
        public string CustomerDisplay { get; init; } = string.Empty;
    }

    private sealed record ReasonOption(VoidReasonDto Value, string Label);
}
