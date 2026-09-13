using System.Globalization;
using System.Text.Json;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using SupermarketSystem.CashierApp.Services;

namespace SupermarketSystem.CashierApp.Views;

/// <summary>
/// كانت مفقودة كليًا — أخطر فجوة وظيفية بتطبيق الكاشير قبل هذا التعديل:
/// زبون يرجّع صنف، والكاشير ما عنده أي طريقة يسجّلها بالنظام رغم جهوزية
/// POST /api/v1/returns بالباك إند وشمول صلاحية Returns.Process لدور
/// "كاشير" الافتراضي أصلًا.
///
/// ═══════════════════════════════════════════════════════════════════
/// قرارات تصميم متعمَّدة:
/// ═══════════════════════════════════════════════════════════════════
/// 1) عملية أونلاين فقط — بلا طابور PendingSale محلي زي البيع، لأن
///    الإرجاع أقل تكرارًا وأكثر حساسية (يحرس مخزونًا وكاشًا فعليين)،
///    وربطه بنفس آلية إعادة المحاولة التلقائية يعقّد المعالجة بلا فائدة
///    حقيقية. فشل الاتصال يظهر برسالة واضحة، والكاشير يعيد المحاولة يدويًا.
/// 2) أصناف الفاتورة تُعرَض بصفوف مبنية بالكود (زي CashClosingWindow،
///    لا DataGrid قابل للتحرير) — كل صف عنده TextBox حقيقي لكمية
///    الإرجاع، مربوط بحدث TextChanged لتحديث الإجمالي فورًا. تفادينا
///    DataGridTextColumn القابل للتحرير عمدًا: تحديث الإجمالي بعد
///    تعديل خلية بـDataGrid يحتاج توقيت دقيق (CellEditEnding يُطلَق قبل
///    التزام القيمة الفعلي أحيانًا) ما نقدر نتحقق منه فعليًا بالقراءة
///    فقط (بيئة التطوير هون Linux، بلا تشغيل WPF حقيقي) — نمط الـTextBox
///    المباشر أبسط وأثبت صحته بالقراءة وحدها.
/// 3) الاسترجاع دائمًا بطريقة دفع واحدة تغطي كامل مبلغ الإرجاع (زي
///    البيع بـSaleWindow: طريقة دفع واحدة للإجمالي كامل) — لا تقسيم
///    الاسترجاع على أكثر من طريقة. ExternalReference دائمًا null (نفس
///    تبسيط SaleWindow) — لو السيرفر رفض لأن طريقة الاسترجاع تتطلب
///    مرجعًا خارجيًا، رسالة الخطأ توضح ذلك.
/// 4) بلا فحوصات سياسة (حدود، سماح/منع) بجهة العميل إطلاقًا — نفس مبدأ
///    "السيرفر هو الحكم الفعلي" (راجع تعليق AddBatchTrackedItem
///    بـSaleWindow) - نعرض بس الكمية القابلة للإرجاع كما وصلت من
///    GetSaleInvoiceById، ونترك كل قرار سياسي (سماح مع مراجعة، رفض) للباك إند.
/// </summary>
public partial class ReturnWindow : Window
{
    private readonly ApiClient _apiClient;
    private readonly AuthSession _authSession;

    private readonly List<ReturnItemRowState> _itemRows = new();
    private Guid? _loadedInvoiceId;

    public ReturnWindow(ApiClient apiClient, AuthSession authSession)
    {
        InitializeComponent();
        _apiClient = apiClient;
        _authSession = authSession;

        Loaded += ReturnWindow_Loaded;
    }

    private async void ReturnWindow_Loaded(object sender, RoutedEventArgs e)
    {
        ReasonCombo.ItemsSource = new[]
        {
            new ReasonOption(ReturnReasonDto.Defective, "تالف"),
            new ReasonOption(ReturnReasonDto.CustomerChangedMind, "الزبون غيّر رأيه"),
            new ReasonOption(ReturnReasonDto.WrongItem, "صنف خطأ"),
            new ReasonOption(ReturnReasonDto.Expired, "منتهي الصلاحية"),
            new ReasonOption(ReturnReasonDto.Other, "سبب آخر")
        };
        ReasonCombo.SelectedIndex = 0;

        // طرق الدفع هون حيّة فقط (بلا رجوع للـSQLite المحلي) — الإرجاع
        // أصلًا عملية أونلاين فقط (راجع تعليق أعلى الملف)، فما في داعي
        // لتعقيد إضافي بمصدر محلي بديل.
        var methods = await _apiClient.GetPaymentMethodsAsync(CancellationToken.None);
        RefundMethodCombo.ItemsSource = methods;
        if (methods.Count > 0)
        {
            RefundMethodCombo.SelectedIndex = 0;
        }

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
        ItemsPanel.Children.Clear();
        _itemRows.Clear();
        InvoiceHeaderText.Text = "جارٍ تحميل تفاصيل الفاتورة...";
        UpdateTotalRefund();

        var result = await _apiClient.GetSaleInvoiceByIdAsync(saleInvoiceId, CancellationToken.None);

        if (!result.Success || result.Response is null)
        {
            InvoiceHeaderText.Text = "تعذّر تحميل تفاصيل الفاتورة.";
            ShowError(ExtractErrorDetail(result.ErrorMessage));
            return;
        }

        _loadedInvoiceId = result.Response.Id;
        RenderInvoiceItems(result.Response);
    }

    private void RenderInvoiceItems(SaleInvoiceDetailDto invoice)
    {
        ItemsPanel.Children.Clear();
        _itemRows.Clear();

        InvoiceHeaderText.Text =
            $"فاتورة {invoice.InvoiceNumber} — الحالة: {invoice.StatusTitle} — الإجمالي: {invoice.TotalAmount:0.00} — إجمالي مُرجَع سابقًا: {invoice.TotalReturnedAmount:0.00}";

        ItemsPanel.Children.Add(BuildItemsHeaderRow());

        foreach (var item in invoice.Items)
        {
            var returnable = item.Quantity - item.QuantityReturned;

            var row = new Grid { Margin = new Thickness(0, 0, 0, 8) };
            row.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
            row.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(90) });
            row.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(90) });
            row.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(100) });
            row.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(90) });
            row.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(110) });

            AddCell(row, item.ProductName, 0, bold: false, center: false);
            AddCell(row, item.Quantity.ToString("0.###", CultureInfo.InvariantCulture), 1, bold: false, center: true);
            AddCell(row, item.QuantityReturned.ToString("0.###", CultureInfo.InvariantCulture), 2, bold: false, center: true);
            AddCell(row, returnable.ToString("0.###", CultureInfo.InvariantCulture), 3, bold: true, center: true);
            AddCell(row, item.UnitPriceSnapshot.ToString("0.00", CultureInfo.InvariantCulture), 4, bold: false, center: true);

            var qtyBox = new TextBox
            {
                Height = 34,
                Text = "0",
                TextAlignment = TextAlignment.Center,
                VerticalContentAlignment = VerticalAlignment.Center,
                IsEnabled = returnable > 0
            };
            qtyBox.TextChanged += (_, _) => UpdateTotalRefund();
            Grid.SetColumn(qtyBox, 5);
            row.Children.Add(qtyBox);

            ItemsPanel.Children.Add(row);

            _itemRows.Add(new ReturnItemRowState
            {
                SaleInvoiceItemId = item.SaleInvoiceItemId,
                Quantity = item.Quantity,
                QuantityReturned = item.QuantityReturned,
                UnitPriceSnapshot = item.UnitPriceSnapshot,
                QuantityBox = qtyBox
            });
        }

        UpdateTotalRefund();
    }

    private static Grid BuildItemsHeaderRow()
    {
        var header = new Grid { Margin = new Thickness(0, 0, 0, 6) };
        header.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
        header.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(90) });
        header.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(90) });
        header.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(100) });
        header.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(90) });
        header.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(110) });

        AddCell(header, "الصنف", 0, bold: true, center: false, headerStyle: true);
        AddCell(header, "المباعة", 1, bold: true, center: true, headerStyle: true);
        AddCell(header, "مرجعة سابقًا", 2, bold: true, center: true, headerStyle: true);
        AddCell(header, "القابل للإرجاع", 3, bold: true, center: true, headerStyle: true);
        AddCell(header, "سعر الوحدة", 4, bold: true, center: true, headerStyle: true);
        AddCell(header, "كمية الإرجاع", 5, bold: true, center: true, headerStyle: true);

        return header;
    }

    private static void AddCell(Grid grid, string text, int column, bool bold, bool center, bool headerStyle = false)
    {
        var textBlock = new TextBlock
        {
            Text = text,
            VerticalAlignment = VerticalAlignment.Center,
            FontSize = headerStyle ? 11.5 : 13,
            FontWeight = bold ? FontWeights.SemiBold : FontWeights.Normal,
            HorizontalAlignment = center ? HorizontalAlignment.Center : HorizontalAlignment.Left,
            Foreground = headerStyle
                ? (System.Windows.Media.Brush)Application.Current.Resources["Ink2Brush"]
                : (System.Windows.Media.Brush)Application.Current.Resources["InkBrush"]
        };
        Grid.SetColumn(textBlock, column);
        grid.Children.Add(textBlock);
    }

    /// <summary>يُستدعى من TextChanged كل صف كمية إرجاع — لا يفحص صحة الكمية (ذلك بس وقت التنفيذ الفعلي)، بس يجمع القيم الصالحة فورًا للعرض.</summary>
    private void UpdateTotalRefund()
    {
        var total = 0m;

        foreach (var row in _itemRows)
        {
            if (decimal.TryParse(row.QuantityBox.Text.Trim(), NumberStyles.Number, CultureInfo.InvariantCulture, out var qty) && qty > 0)
            {
                total += qty * row.UnitPriceSnapshot;
            }
        }

        TotalRefundText.Text = $"إجمالي الاسترجاع: {total:0.00}";
    }

    private async void SubmitReturnButton_Click(object sender, RoutedEventArgs e)
    {
        HideError();

        if (_loadedInvoiceId is not { } invoiceId)
        {
            ShowError("اختر فاتورة أولًا من نتائج البحث.");
            return;
        }

        if (ReasonCombo.SelectedItem is not ReasonOption selectedReason)
        {
            ShowError("اختر سبب الإرجاع.");
            return;
        }

        if (RefundMethodCombo.SelectedItem is not PaymentMethodDto selectedMethod)
        {
            ShowError("اختر طريقة الاسترجاع - تأكد من الاتصال بالسيرفر (طرق الدفع تُحمَّل حيًّا لهذه الشاشة فقط).");
            return;
        }

        var items = new List<ProcessReturnItemRequestDto>();
        var total = 0m;

        foreach (var row in _itemRows)
        {
            var text = row.QuantityBox.Text.Trim();
            if (text.Length == 0)
            {
                continue;
            }

            if (!decimal.TryParse(text, NumberStyles.Number, CultureInfo.InvariantCulture, out var qty))
            {
                ShowError("إحدى كميات الإرجاع غير صالحة - أدخل رقمًا فقط.");
                return;
            }

            if (qty == 0)
            {
                continue;
            }

            if (qty < 0 || qty > row.Quantity - row.QuantityReturned)
            {
                ShowError("إحدى كميات الإرجاع أكبر من الكمية القابلة للإرجاع لنفس السطر.");
                return;
            }

            items.Add(new ProcessReturnItemRequestDto(row.SaleInvoiceItemId, qty));
            total += qty * row.UnitPriceSnapshot;
        }

        if (items.Count == 0)
        {
            ShowError("حدد كمية إرجاع لصنف واحد على الأقل.");
            return;
        }

        SubmitReturnButton.IsEnabled = false;

        var request = new ProcessReturnRequestDto(
            invoiceId,
            Guid.NewGuid(),
            selectedReason.Value,
            string.IsNullOrWhiteSpace(NotesBox.Text) ? null : NotesBox.Text.Trim(),
            items,
            new List<ProcessReturnRefundRequestDto>
            {
                new(selectedMethod.Id, total, null, Guid.NewGuid())
            });

        var result = await _apiClient.ProcessReturnAsync(request, CancellationToken.None);

        SubmitReturnButton.IsEnabled = true;

        if (!result.Success || result.Response is null)
        {
            ShowError($"تعذّر تنفيذ الإرجاع: {ExtractErrorDetail(result.ErrorMessage)}");
            return;
        }

        var response = result.Response;
        var replaySuffix = response.WasReplay ? "\n(طلب مكرَّر - لم يُسجَّل إرجاع إضافي، هذه نتيجة المحاولة الأصلية)" : "";
        var reviewSuffix = response.ReviewFlags.Count > 0
            ? $"\n\nبانتظار مراجعة إدارية:\n- {string.Join("\n- ", response.ReviewFlags)}"
            : "";

        MessageBox.Show(
            $"تم تنفيذ الإرجاع بنجاح.\nرقم الإرجاع: {response.InvoiceNumber}\nالمبلغ المسترجَع: {response.TotalRefundedAmount:0.00}{replaySuffix}{reviewSuffix}",
            "تم", MessageBoxButton.OK, MessageBoxImage.Information);

        NotesBox.Clear();

        // إعادة تحميل نفس الفاتورة - تعكس الكميات المتبقية المحدَّثة فورًا،
        // تسمح للكاشير يكمل إرجاع أصناف تانية من نفس الفاتورة بنفس الجلسة لو احتاج.
        await LoadInvoiceAsync(invoiceId);
    }

    /// <summary>
    /// نفس منطق SaleWindow.ExtractErrorDetail حرفيًا (نسخ مقصود، لا مشاركة
    /// كود - SaleWindow ممنوع لمسه بهذه الدفعة). رسالة الخطأ الخام شكلها
    /// "422: {json ProblemDetails}"، نطلع حقل "detail" منها.
    /// </summary>
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

    private sealed class ReturnItemRowState
    {
        public Guid SaleInvoiceItemId { get; init; }
        public decimal Quantity { get; init; }
        public decimal QuantityReturned { get; init; }
        public decimal UnitPriceSnapshot { get; init; }
        public TextBox QuantityBox { get; init; } = null!;
    }

    private sealed record ReasonOption(ReturnReasonDto Value, string Label);
}
