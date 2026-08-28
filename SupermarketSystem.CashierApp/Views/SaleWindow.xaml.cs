using System.Collections.ObjectModel;
using System.Text.Json;
using System.Windows;
using System.Windows.Input;
using Microsoft.EntityFrameworkCore;
using SupermarketSystem.CashierApp.Local;
using SupermarketSystem.CashierApp.Services;

namespace SupermarketSystem.CashierApp.Views;

public partial class SaleWindow : Window
{
    private readonly ApiClient _apiClient;
    private readonly AuthSession _authSession;
    private readonly string _dbPath;
    private readonly Services.Printing.ReceiptPrinterService _receiptPrinter;

    private readonly ObservableCollection<CartLine> _cart = new();
    private List<PaymentMethodDto> _paymentMethods = new();

    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase
    };

    public SaleWindow(ApiClient apiClient, AuthSession authSession, string dbPath, Services.Printing.ReceiptPrinterService receiptPrinter)
    {
        InitializeComponent();
        _apiClient = apiClient;
        _authSession = authSession;
        _dbPath = dbPath;
        _receiptPrinter = receiptPrinter;

        CartGrid.ItemsSource = _cart;
        Loaded += SaleWindow_Loaded;
    }

    private async void SaleWindow_Loaded(object sender, RoutedEventArgs e)
    {
        await LoadPaymentMethodsAsync();
        BarcodeBox.Focus();
    }

    /// <summary>
    /// المحلي أولًا (SQLite، محدَّثة دوريًا من BackgroundSyncService) —
    /// هيك شاشة البيع تشتغل بلا اتصال طالما صارت مزامنة ناجحة وحدة
    /// عالأقل بالسيرفر. الاتصال الحي هون بس تحديث اختياري إضافي، لا
    /// شرط لتشتغل الشاشة.
    /// </summary>
    private async Task LoadPaymentMethodsAsync()
    {
        using (var db = new LocalDbContext(_dbPath))
        {
            var localMethods = db.PaymentMethods
                .OrderBy(m => m.Name)
                .Select(m => new PaymentMethodDto(m.Id, m.Name, m.RequiresExternalReference))
                .ToList();

            if (localMethods.Count > 0)
            {
                _paymentMethods = localMethods;
                PaymentMethodCombo.ItemsSource = _paymentMethods;
                PaymentMethodCombo.SelectedIndex = 0;
            }
        }

        // محاولة تحديث حية بالخلفية - لو نجحت ولقت نتائج، تستبدل القائمة
        // المعروضة بأحدث نسخة (نادر يتغيّر شي، بس لو صار، نعكسه فورًا).
        var liveMethods = await _apiClient.GetPaymentMethodsAsync(CancellationToken.None);
        if (liveMethods.Count > 0)
        {
            _paymentMethods = liveMethods;
            var previousSelection = (PaymentMethodCombo.SelectedItem as PaymentMethodDto)?.Id;
            PaymentMethodCombo.ItemsSource = _paymentMethods;
            var matchIndex = _paymentMethods.FindIndex(m => m.Id == previousSelection);
            PaymentMethodCombo.SelectedIndex = matchIndex >= 0 ? matchIndex : 0;
        }

        ConnectionStatusText.Text = _paymentMethods.Count > 0 ? "متصل" : "بلا اتصال وبلا طرق دفع محفوظة - لازم اتصال أول مرة";
    }

    /// <summary>Enter وTab معًا - قرّائات باركود مختلفة بتستخدم أحدهما كفاصل نهاية المسح، بلا إعداد موحَّد.</summary>
    private void BarcodeBox_KeyDown(object sender, KeyEventArgs e)
    {
        if (e.Key is Key.Enter or Key.Tab)
        {
            e.Handled = true; // يمنع Tab من نقل التركيز لعنصر تاني قبل ما نعالج المسح
            AddScannedItem();
        }
    }

    private void AddButton_Click(object sender, RoutedEventArgs e)
    {
        AddScannedItem();
    }

    /// <summary>
    /// بحث محلي بس (SQLite) — بلا أي استدعاء API هون، هذا بالضبط سبب
    /// وجود الكتالوج المحلي أصلًا: البيع يشتغل حتى لو النت مقطوع كليًا.
    /// </summary>
    private void AddScannedItem()
    {
        try
        {
            var barcodeValue = BarcodeBox.Text.Trim();
            BarcodeBox.Clear();

            if (string.IsNullOrWhiteSpace(barcodeValue))
            {
                return;
            }

            using var db = new LocalDbContext(_dbPath);

            var barcode = db.ProductBarcodes.FirstOrDefault(b => b.BarcodeValue == barcodeValue);
            if (barcode is null)
            {
                ShowError($"الباركود '{barcodeValue}' غير موجود بالكتالوج المحلي.");
                return;
            }

            var unit = db.ProductUnits.FirstOrDefault(u => u.UnitId == barcode.ProductUnitId);
            if (unit is null)
            {
                ShowError("خطأ داخلي - الوحدة المرتبطة بالباركود غير موجودة.");
                return;
            }

            var product = db.Products.FirstOrDefault(p => p.ProductId == unit.ProductId);
            if (product is null || !product.IsAvailableForSale)
            {
                ShowError("هذا الصنف غير متوفر للبيع حاليًا.");
                return;
            }

            if (product.IsBatchTracked)
            {
                AddBatchTrackedItem(db, product, unit);
            }
            else
            {
                AddSimpleItem(product, unit);
            }
        }
        finally
        {
            // يشتغل بكل الأحوال (نجاح، فشل، أي return مبكِّر) - التركيز
            // لازم يرجع لصندوق الباركود دائمًا، هذا أساس استخدام قارئ
            // باركود USB فعلي (بيتصرف كـkeyboard، فمحتاج التركيز
            // الدائم على الحقل الصحيح، وإلا أرقامه بتروح لمكان تاني).
            BarcodeBox.Focus();
            BarcodeBox.SelectAll();
        }
    }

    private void AddSimpleItem(LocalProduct product, LocalProductUnit unit)
    {
        FlashAddedSuccess();

        var existingLine = _cart.FirstOrDefault(l => l.ProductUnitId == unit.UnitId && l.ProductBatchId is null);
        if (existingLine is not null)
        {
            existingLine.Quantity += 1;
            CartGrid.Items.Refresh();
        }
        else
        {
            _cart.Add(new CartLine
            {
                ProductId = product.ProductId,
                ProductUnitId = unit.UnitId,
                ProductName = product.Name,
                UnitName = unit.UnitName,
                Quantity = 1,
                UnitPrice = product.SellingPrice
            });
        }

        UpdateTotal();
    }

    /// <summary>
    /// FIFO حسب تاريخ الصلاحية الأقرب — دفعات بلا تاريخ صلاحية (خام/غير
    /// منتهية الصلاحية بطبيعتها) تُستخدم أخيرًا (DateOnly.MaxValue
    /// بالترتيب). لو نفس الدفعة أصلًا بالسلة، بنزيد كميتها بس بحدود
    /// رصيدها المتوفر فعليًا — منع بيع أكتر من الموجود محليًا، قبل حتى
    /// ما يوصل الطلب للسيرفر (فحص إضافي، لا بديل عن فحص السيرفر نفسه).
    /// </summary>
    private void AddBatchTrackedItem(LocalDbContext db, LocalProduct product, LocalProductUnit unit)
    {
        var existingLine = _cart.FirstOrDefault(l => l.ProductUnitId == unit.UnitId && l.ProductBatchId is not null);

        if (existingLine is not null)
        {
            var currentBatch = db.ProductBatches.FirstOrDefault(b => b.BatchId == existingLine.ProductBatchId);
            if (currentBatch is not null && existingLine.Quantity + 1 <= currentBatch.QuantityAvailable)
            {
                existingLine.Quantity += 1;
                FlashAddedSuccess();
                CartGrid.Items.Refresh();
                UpdateTotal();
                return;
            }

            ShowError($"الكمية المطلوبة تتجاوز الرصيد المتوفر لدفعة '{existingLine.BatchNumber}' ({currentBatch?.QuantityAvailable ?? 0}).");
            return;
        }

        var batch = db.ProductBatches
            .Where(b => b.ProductId == product.ProductId && b.QuantityAvailable >= 1)
            .OrderBy(b => b.ExpiryDate ?? DateOnly.MaxValue)
            .FirstOrDefault();

        if (batch is null)
        {
            ShowError($"لا يوجد رصيد متوفر لهذا الصنف بأي دفعة محليًا - '{product.Name}'.");
            return;
        }

        FlashAddedSuccess();

        _cart.Add(new CartLine
        {
            ProductId = product.ProductId,
            ProductUnitId = unit.UnitId,
            ProductBatchId = batch.BatchId,
            BatchNumber = batch.BatchNumber,
            ProductName = product.Name,
            UnitName = unit.UnitName,
            Quantity = 1,
            UnitPrice = product.SellingPrice
        });

        UpdateTotal();
    }

    private void UpdateTotal()
    {
        var total = _cart.Sum(l => l.LineTotal);
        TotalText.Text = $"الإجمالي: {total:0.00}";
    }

    private async void CompleteSaleButton_Click(object sender, RoutedEventArgs e)
    {
        if (_cart.Count == 0)
        {
            ShowError("السلة فاضية.");
            return;
        }

        if (PaymentMethodCombo.SelectedItem is not PaymentMethodDto selectedMethod)
        {
            ShowError("اختر طريقة دفع.");
            return;
        }

        if (_authSession.BranchId is null)
        {
            ShowError("لا يوجد فرع مرتبط بجلستك - راجع الإدارة.");
            return;
        }

        HideError();
        CompleteSaleButton.IsEnabled = false;

        var total = _cart.Sum(l => l.LineTotal);
        var clientRequestId = Guid.NewGuid();

        // بناء الطلب بنفس شكل CompleteSaleCommand حرفيًا — productBatchId
        // يجي من CartLine (اختيار FIFO تلقائي صار وقت الإضافة للسلة
        // لمنتجات "تتتبّع دفعات"، راجع AddBatchTrackedItem)، null طبيعي
        // لمنتج عادي.
        var payload = new
        {
            branchId = _authSession.BranchId.Value,
            clientRequestId,
            customerId = (Guid?)null,
            invoiceLevelDiscountAmount = 0m,
            items = _cart.Select(l => new
            {
                productId = l.ProductId,
                productUnitId = l.ProductUnitId,
                quantity = l.Quantity,
                manualDiscountAmount = 0m,
                productBatchId = l.ProductBatchId
            }),
            payments = new[]
            {
                new
                {
                    paymentMethodId = selectedMethod.Id,
                    amount = total,
                    externalReference = (string?)null,
                    clientRequestId = Guid.NewGuid()
                }
            }
        };

        var payloadJson = JsonSerializer.Serialize(payload, JsonOptions);

        var pendingSale = new PendingSale
        {
            ClientRequestId = clientRequestId,
            BranchId = _authSession.BranchId.Value,
            RequestPayloadJson = payloadJson,
            CreatedAtLocal = DateTime.UtcNow,
            AttemptCount = 0
        };

        // يُحفظ محليًا فورًا *قبل* أي محاولة إرسال - هذا الضمان الحقيقي
        // ضد ضياع البيع.
        using (var db = new LocalDbContext(_dbPath))
        {
            db.PendingSales.Add(pendingSale);
            await db.SaveChangesAsync();
        }

        // محاولة إرسال فورية (Online-first) - لو نجحت، نحذف الصف المحلي
        // حالًا. لو فشلت، يضل بالطابور لخدمة المزامنة بالخلفية.
        var sendResult = await _apiClient.SendPendingSaleAsync(pendingSale, CancellationToken.None);
        if (sendResult.Success)
        {
            using var db = new LocalDbContext(_dbPath);
            var savedRow = await db.PendingSales.FirstOrDefaultAsync(s => s.ClientRequestId == clientRequestId);
            if (savedRow is not null)
            {
                db.PendingSales.Remove(savedRow);
                await db.SaveChangesAsync();
            }
        }

        // الطباعة تصير من بيانات السلة المحلية مباشرة، بلا انتظار رقم
        // فاتورة من السيرفر — الزبون بده إيصاله فورًا، مش بعد جولة
        // شبكة كاملة. لو أوفلاين، رقم الفاتورة هون هو ClientRequestId
        // المحلي (مرجع مؤقت، لحد ما يتأكد بالسيرفر لاحقًا).
        var receiptData = new Services.Printing.ReceiptData(
            InvoiceNumber: clientRequestId.ToString("N")[..8].ToUpperInvariant(),
            CreatedAtLocal: DateTime.Now,
            CashierName: _authSession.FullName ?? "",
            Lines: _cart.Select(l => new Services.Printing.ReceiptLine(
                l.ProductName, l.UnitName, l.Quantity, l.UnitPrice, l.LineTotal)).ToList(),
            Total: total,
            PaymentMethodName: selectedMethod.Name);

        var printResult = await _receiptPrinter.PrintAsync(receiptData, CancellationToken.None);

        _cart.Clear();
        UpdateTotal();
        CompleteSaleButton.IsEnabled = true;
        BarcodeBox.Focus();

        // رسالتان منفصلتان عمدًا - نجاح/فشل الإرسال للسيرفر شي، ونجاح/فشل
        // الطباعة شي تاني كليًا. فشل الطباعة *أبدًا* ما يعني فشل البيع.
        var saleMessage = sendResult.Success
            ? "تم إتمام البيع وإرساله فورًا."
            : "تم حفظ البيع محليًا - رح يُرسل تلقائيًا أول ما يرجع الاتصال.";
        var printMessage = printResult.Success
            ? "تمت طباعة الفاتورة."
            : $"تعذّرت الطباعة: {printResult.ErrorMessage}";

        MessageBox.Show($"{saleMessage}\n{printMessage}", "تم", MessageBoxButton.OK,
            printResult.Success ? MessageBoxImage.Information : MessageBoxImage.Warning);
    }

    private void ShowError(string message)
    {
        ErrorText.Text = message;
        ErrorText.Visibility = Visibility.Visible;
        FlashBarcodeBox(System.Windows.Media.Brushes.MistyRose);
    }

    private void HideError()
    {
        ErrorText.Visibility = Visibility.Collapsed;
    }

    /// <summary>تُستدعى بعد إضافة صنف للسلة بنجاح فقط - وميض أخضر خاطف، تمييز واضح عن مجرد "صفّرت رسالة خطأ قديمة".</summary>
    private void FlashAddedSuccess()
    {
        HideError();
        FlashBarcodeBox(System.Windows.Media.Brushes.PaleGreen);
    }

    /// <summary>
    /// وميض لوني خاطف (200 مللي ثانية) على حقل الباركود نفسه — تأكيد
    /// بصري فوري بلا ما الكاشير يحتاج يقرأ نص أو يبعد نظره عن الشاشة.
    /// </summary>
    private async void FlashBarcodeBox(System.Windows.Media.Brush flashColor)
    {
        BarcodeBox.Background = flashColor;
        await Task.Delay(200);
        BarcodeBox.Background = System.Windows.Media.Brushes.White;
    }
}
