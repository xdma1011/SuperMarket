using System.Collections.ObjectModel;
using System.IO;
using System.Text.Json;
using System.Windows;
using System.Windows.Input;
using System.Windows.Threading;
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
    private readonly BackgroundSyncService _backgroundSync;
    private readonly string _adminScreenPassword;

    private readonly ObservableCollection<CartLine> _cart = new();
    private List<PaymentMethodDto> _paymentMethods = new();

    /// <summary>كم دينار يساوي الدولار الواحد - من الإعدادات (راجع PaymentSettingsCache)، تُقرأ مرة وقت فتح الشاشة.</summary>
    private readonly decimal _usdToJodExchangeRate;

    /// <summary>آخر سطر انضاف للسلة - يُفحص بـCartGrid_LoadingRow لتمييزه بصريًا (وميض خلفية) لحظة توليد صفّه فعليًا بالـDataGrid.</summary>
    private CartLine? _lastAddedLine;

    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase
    };

    // فحص اتصال دوري خفيف - يحدّد بس تفعيل/تعطيل زر "مزامنة الآن"، لا علاقة
    // له بتحميل بيانات الشاشة نفسها (تلك محلية بالكامل، راجع LoadPaymentMethodsAsync).
    // 10 ثواني: كافية تلتقط انقطاع/عودة نت بسرعة معقولة، بلا ضغط زائد على السيرفر.
    private readonly DispatcherTimer _connectivityTimer;

    public SaleWindow(
        ApiClient apiClient, AuthSession authSession, string dbPath,
        Services.Printing.ReceiptPrinterService receiptPrinter, BackgroundSyncService backgroundSync,
        string adminScreenPassword)
    {
        InitializeComponent();
        _apiClient = apiClient;
        _authSession = authSession;
        _dbPath = dbPath;
        _receiptPrinter = receiptPrinter;
        _backgroundSync = backgroundSync;
        _adminScreenPassword = adminScreenPassword;
        _usdToJodExchangeRate = PaymentSettingsCache.ReadUsdToJodExchangeRate(Path.GetDirectoryName(_dbPath) ?? "");

        CartGrid.ItemsSource = _cart;
        Loaded += SaleWindow_Loaded;

        _connectivityTimer = new DispatcherTimer { Interval = TimeSpan.FromSeconds(10) };
        _connectivityTimer.Tick += async (_, _) => await RefreshConnectivityStateAsync();
        Closed += (_, _) => _connectivityTimer.Stop();
    }

    private async void SaleWindow_Loaded(object sender, RoutedEventArgs e)
    {
        await LoadPaymentMethodsAsync();
        BarcodeBox.Focus();

        _connectivityTimer.Start();
        await RefreshConnectivityStateAsync();
    }

    /// <summary>
    /// يحدّث تفعيل زر "مزامنة الآن" حسب اتصال فعلي بالسيرفر (لا بس وجود
    /// كرت شبكة). ما يلمس الزر إطلاقًا لو فيه مزامنة شغّالة أصلًا (يدوية
    /// أو تلقائية) - حالته وقتها محكومة من SyncNowButton_Click/الدورة نفسها.
    /// </summary>
    private async Task RefreshConnectivityStateAsync()
    {
        if (_backgroundSync.IsSyncing)
        {
            return;
        }

        var isConnected = await _apiClient.IsServerReachableAsync(CancellationToken.None);
        SyncNowButton.IsEnabled = isConnected;
        ConnectionStatusText.Text = isConnected ? "متصل" : "بلا اتصال بالسيرفر";
    }

    private async void SyncNowButton_Click(object sender, RoutedEventArgs e)
    {
        SyncNowButton.IsEnabled = false;
        CancelSyncButton.IsEnabled = true;
        CancelSyncButton.Visibility = Visibility.Visible;
        ConnectionStatusText.Text = "جاري المزامنة...";

        var outcome = await _backgroundSync.TriggerManualSyncAsync();

        CancelSyncButton.Visibility = Visibility.Collapsed;

        // إعادة تحميل طرق الدفع محليًا أولًا (لو المزامنة جابت تحديثًا،
        // نادر بس ممكن) - قبل رسالة النتيجة، عشان LoadPaymentMethodsAsync
        // ما تكتب فوق رسالة النتيجة (هي نفسها بتحدّث ConnectionStatusText).
        await LoadPaymentMethodsAsync();

        ConnectionStatusText.Text = outcome switch
        {
            BackgroundSyncService.ManualSyncOutcome.Completed => "تمت المزامنة بنجاح",
            BackgroundSyncService.ManualSyncOutcome.Cancelled => "أُلغيت المزامنة",
            BackgroundSyncService.ManualSyncOutcome.AlreadyRunning => "فيه مزامنة شغّالة أصلًا",
            _ => $"فشلت المزامنة: {_backgroundSync.LastErrorMessage}"
        };

        // إعادة تفعيل الزر فورًا (بدل انتظار دورة الفحص الدوري لحد 10 ثواني) -
        // لو الاتصال فعليًا انقطع بين الوقتين، الدورة الجاية للفحص الدوري
        // (خلال 10 ثواني) بتعطّله من جديد تلقائيًا.
        SyncNowButton.IsEnabled = true;
    }

    private void CancelSyncButton_Click(object sender, RoutedEventArgs e)
    {
        _backgroundSync.CancelManualSync();
        CancelSyncButton.IsEnabled = false;
    }

    /// <summary>
    /// كانت مفقودة كليًا - محمية بنفس باسوورد شاشة الإدارة المحلي
    /// (راجع MainWindow.AdminAccessButton_Click) لأن صلاحية CashClosing.Manage
    /// الفعلية أصلًا مو من صلاحيات دور "كاشير" الافتراضية - هدف الباسوورد
    /// هون تنظيم الوصول لإجراء نهاية وردية، لا حماية أمنية جدّية.
    /// </summary>
    private void CashClosingButton_Click(object sender, RoutedEventArgs e)
    {
        var passwordPrompt = new AdminPasswordWindow { Owner = this };
        if (passwordPrompt.ShowDialog() != true || passwordPrompt.EnteredPassword != _adminScreenPassword)
        {
            return;
        }

        if (_authSession.BranchId is null)
        {
            ShowError("لا يوجد فرع مرتبط بجلستك - راجع الإدارة.");
            return;
        }

        var closingWindow = new CashClosingWindow(_apiClient, _authSession.BranchId.Value, _paymentMethods) { Owner = this };
        closingWindow.ShowDialog();
    }

    /// <summary>
    /// خروج طوعي - تحذير أول لو فيه أصناف بالسلة لسه ما اتباعت (تفادي
    /// خروج بالغلط وضياع سلة نصف جاهزة)، ثم POST /auth/logout أفضل-محاولة
    /// (فشلها ما يمنع الخروج المحلي، راجع ApiClient.LogoutAsync)، ثم مسح
    /// الجلسة وفتح شاشة دخول جديدة نظيفة.
    /// </summary>
    private async void LogoutButton_Click(object sender, RoutedEventArgs e)
    {
        if (_cart.Count > 0)
        {
            var confirmDiscard = MessageBox.Show(
                "فيه أصناف بالسلة الحالية لسه ما اتباعت - هل متأكد من الخروج؟ رح تضيع السلة.",
                "تأكيد الخروج", MessageBoxButton.YesNo, MessageBoxImage.Warning, MessageBoxResult.No);

            if (confirmDiscard != MessageBoxResult.Yes)
            {
                return;
            }
        }

        LogoutButton.IsEnabled = false;
        _backgroundSync.Stop();

        if (_authSession.RefreshToken is { } refreshToken)
        {
            await _apiClient.LogoutAsync(refreshToken, CancellationToken.None);
        }

        _authSession.Clear();

        var loginWindow = new LoginWindow(_apiClient, _authSession, _dbPath, _backgroundSync, _receiptPrinter, _adminScreenPassword);
        loginWindow.Show();
        Close();
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
    /// باركود غير مطابق - أو حقل فاضي أصلًا - ما يمنع البيع، بيفتح شاشة
    /// البحث الكاملة بدلًا من مجرد رسالة خطأ (راجع ProductSearchWindow).
    /// </summary>
    private void AddScannedItem()
    {
        var barcodeValue = BarcodeBox.Text.Trim();
        BarcodeBox.Clear();

        try
        {
            using var db = new LocalDbContext(_dbPath);

            if (!string.IsNullOrWhiteSpace(barcodeValue))
            {
                var barcode = db.ProductBarcodes.FirstOrDefault(b => b.BarcodeValue == barcodeValue);
                if (barcode is not null)
                {
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

                    AddResolvedItem(db, product, unit);
                    return;
                }
            }

            OpenSearchWindow(barcodeValue, priceCheckOnly: false);
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

    /// <summary>نقطة إضافة موحَّدة - يقرر بين صنف عادي أو متتبَّع دفعات، مستخدَمة من مسار الباركود المباشر وشاشة البحث الكاملة معًا، صفر تكرار منطق.</summary>
    private void AddResolvedItem(LocalDbContext db, LocalProduct product, LocalProductUnit unit)
    {
        if (product.IsBatchTracked)
        {
            AddBatchTrackedItem(db, product, unit);
        }
        else
        {
            AddSimpleItem(product, unit);
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
            var newLine = new CartLine
            {
                ProductId = product.ProductId,
                ProductUnitId = unit.UnitId,
                ProductName = product.Name,
                UnitName = unit.UnitName,
                Quantity = 1,
                UnitPrice = product.SellingPrice
            };
            _lastAddedLine = newLine;
            _cart.Add(newLine);
        }

        UpdateTotal();
    }

    /// <summary>
    /// FIFO حسب تاريخ الصلاحية الأقرب — دفعات بلا تاريخ صلاحية (خام/غير
    /// منتهية الصلاحية بطبيعتها) تُستخدم أخيرًا (DateOnly.MaxValue
    /// بالترتيب). تجاوز الرصيد المحلي المعروض ما يمنع البيع — سماح مع
    /// مراجعة (CLAUDE.md §1.6): البضاعة ممكن تكون وصلت فعليًا ولسه ما
    /// انزامنت محليًا؛ الحكم الفعلي عند السيرفر (AllowNegativeStock).
    /// </summary>
    private void AddBatchTrackedItem(LocalDbContext db, LocalProduct product, LocalProductUnit unit)
    {
        var existingLine = _cart.FirstOrDefault(l => l.ProductUnitId == unit.UnitId && l.ProductBatchId is not null);

        if (existingLine is not null)
        {
            var currentBatch = db.ProductBatches.FirstOrDefault(b => b.BatchId == existingLine.ProductBatchId);
            existingLine.Quantity += 1;

            // تجاوز الرصيد المحلي المعروض - سماح مع مراجعة، لا منع (راجع
            // تعليق CartLine.NeedsReview). السيرفر هو الحكم الفعلي.
            if (currentBatch is null || existingLine.Quantity > currentBatch.QuantityAvailable)
            {
                existingLine.NeedsReview = true;
            }

            FlashAddedSuccess();
            CartGrid.Items.Refresh();
            UpdateTotal();
            return;
        }

        // أولوية للدفعات اللي فيها رصيد محلي معروض؛ لو ما لقينا، نرجع لأي
        // دفعة فعلية للمنتج (حتى لو رصيدها المحلي صفر أو قديم) بدل ما نمنع
        // البيع بالكامل - البضاعة ممكن تكون وصلت فعليًا وما زامنت لسه.
        var batch = db.ProductBatches
            .Where(b => b.ProductId == product.ProductId && b.QuantityAvailable >= 1)
            .OrderBy(b => b.ExpiryDate ?? DateOnly.MaxValue)
            .FirstOrDefault();

        var needsReview = batch is null;
        if (batch is null)
        {
            batch = db.ProductBatches
                .Where(b => b.ProductId == product.ProductId)
                .OrderBy(b => b.ExpiryDate ?? DateOnly.MaxValue)
                .FirstOrDefault();
        }

        if (batch is null)
        {
            ShowError($"لا يوجد أي دفعة مسجّلة لهذا الصنف محليًا - '{product.Name}'. راجع الإدارة قبل البيع.");
            return;
        }

        FlashAddedSuccess();

        var newBatchLine = new CartLine
        {
            ProductId = product.ProductId,
            ProductUnitId = unit.UnitId,
            ProductBatchId = batch.BatchId,
            BatchNumber = batch.BatchNumber,
            ProductName = product.Name,
            UnitName = unit.UnitName,
            Quantity = 1,
            UnitPrice = product.SellingPrice,
            NeedsReview = needsReview
        };
        _lastAddedLine = newBatchLine;
        _cart.Add(newBatchLine);

        UpdateTotal();
    }

    private void UpdateTotal()
    {
        var total = _cart.Sum(l => l.LineTotal);
        TotalText.Text = $"الإجمالي: {total:0.00}";
        UpdateChangeDisplay();
    }

    /// <summary>
    /// كبسة فئة نقدية دينار - تستبدل قيمة صندوق المبلغ المستلم بالكامل
    /// (لا تُضاف عليها)، تمامًا متل ما لو الكاشير كتبها يدويًا. لو المبلغ
    /// المستلم مبلغ غير اعتيادي (51 مثلًا)، الكاشير بيكتبه يدويًا بالصندوق
    /// مباشرة بدل الكبسات.
    /// </summary>
    private void JodDenominationButton_Click(object sender, RoutedEventArgs e)
    {
        if (sender is Button { Tag: string tag } && decimal.TryParse(tag, out var jodAmount))
        {
            TenderedAmountBox.Text = jodAmount.ToString("0.00");
        }
    }

    /// <summary>كبسة فئة نقدية دولار - تُحوَّل للدينار بسعر الصرف من الإعدادات قبل ما تنحط بصندوق المبلغ المستلم.</summary>
    private void UsdDenominationButton_Click(object sender, RoutedEventArgs e)
    {
        if (sender is Button { Tag: string tag } && decimal.TryParse(tag, out var usdAmount))
        {
            TenderedAmountBox.Text = (usdAmount * _usdToJodExchangeRate).ToString("0.00");
        }
    }

    private void TenderedAmountBox_TextChanged(object sender, System.Windows.Controls.TextChangedEventArgs e)
    {
        UpdateChangeDisplay();
    }

    /// <summary>
    /// الباقي = المستلم - الإجمالي. أخضر كبير وواضح بالحالة الطبيعية؛
    /// أحمر لو المبلغ المستلم غير كافٍ - تنبيه بصري بس، لا يمنع إتمام
    /// البيع (نفس فلسفة "لا نوقف الكاشير أبدًا").
    /// </summary>
    private void UpdateChangeDisplay()
    {
        var total = _cart.Sum(l => l.LineTotal);
        var tenderedText = TenderedAmountBox.Text.Trim();

        if (tenderedText.Length == 0 || !decimal.TryParse(tenderedText, out var tendered))
        {
            ChangeAmountText.Text = "0.00";
            ChangeAmountText.Foreground = System.Windows.Media.Brushes.Gray;
            return;
        }

        var change = tendered - total;
        ChangeAmountText.Text = change.ToString("0.00");
        ChangeAmountText.Foreground = change >= 0
            ? System.Windows.Media.Brushes.Green
            : System.Windows.Media.Brushes.Red;
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

        // محاولة إرسال فورية (Online-first) - البيع بالكاشير ما يتوقف أبدًا
        // لأي سبب، مهما كان (لا انقطاع نت، ولا حتى رفض فعلي من السيرفر
        // كـSale.ProductNotActive) - الزبون واقف قدام الكاشير، والبضاعة
        // بتطلع معه بكل الأحوال. الفرق الوحيد بين "انقطاع نت" و"رفض فعلي"
        // مش شي الكاشير لازم يتعامل معه لحظيًا - هو بس يحدّد هل الصف
        // بيضل بالطابور (الحالتين، فعليًا) لمراجعة لاحقة من الإدارة
        // (PendingQueueWindow يعرض السبب الحقيقي، وزر الحذف هناك للتعامل
        // مع رفض نهائي ما رح ينحل بإعادة المحاولة). لو الإدارة صلّحت السبب
        // لاحقًا (مثلًا فعّلت المنتج من جديد)، الفاتورة العالقة بتترسل
        // تلقائيًا بنفس دورة المزامنة الجاية - ذاتية الإصلاح بلا أي تدخّل
        // إضافي.
        var sendResult = await _apiClient.SendPendingSaleAsync(pendingSale, CancellationToken.None);

        using (var db = new LocalDbContext(_dbPath))
        {
            var savedRow = await db.PendingSales.FirstOrDefaultAsync(s => s.ClientRequestId == clientRequestId);
            if (savedRow is not null)
            {
                if (sendResult.Success)
                {
                    db.PendingSales.Remove(savedRow);
                }
                else
                {
                    // لو ظلّت بالطابور، نسجّل سبب الفشل من أول محاولة فورًا -
                    // بلا هذا، عمود "آخر خطأ" بـPendingQueueWindow كان يضل
                    // فاضي لحد أول دورة مزامنة خلفية لاحقة (فجوة صغيرة، بس
                    // حقيقية لو الإدارة فتحت الشاشة قبل أول دورة).
                    savedRow.AttemptCount += 1;
                    savedRow.LastAttemptAtLocal = DateTime.UtcNow;
                    savedRow.LastErrorMessage = ExtractErrorDetail(sendResult.ErrorMessage);
                }

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
            PaymentMethodName: selectedMethod.Name,
            StoreName: StoreBrandingCache.ReadStoreName(Path.GetDirectoryName(_dbPath) ?? ""));

        var printResult = await _receiptPrinter.PrintAsync(receiptData, CancellationToken.None);

        _cart.Clear();
        TenderedAmountBox.Clear();
        UpdateTotal();
        CompleteSaleButton.IsEnabled = true;
        BarcodeBox.Focus();

        // رسالتان منفصلتان عمدًا - نجاح/فشل الإرسال للسيرفر شي، ونجاح/فشل
        // الطباعة شي تاني كليًا. فشل الطباعة *أبدًا* ما يعني فشل البيع.
        var saleMessage = sendResult.Success
            ? "تم إتمام البيع وإرساله فورًا."
            : "تم حفظ البيع محليًا - رح يُرسل تلقائيًا بالخلفية (راجع شاشة الفواتير المعلَّقة لو ضلّت متكرّرة).";
        var printMessage = printResult.Success
            ? "تمت طباعة الفاتورة."
            : $"تعذّرت الطباعة: {printResult.ErrorMessage}";

        MessageBox.Show($"{saleMessage}\n{printMessage}", "تم", MessageBoxButton.OK,
            printResult.Success ? MessageBoxImage.Information : MessageBoxImage.Warning);
    }

    /// <summary>
    /// رسالة الخطأ الخام شكلها "422: {json ProblemDetails}" - نطلع حقل
    /// "detail" منها للكاشير، بدل ما نعرضله JSON خام. لو الاستخراج فشل
    /// لأي سبب (شكل غير متوقَّع)، نرجع النص الخام كما هو - أفضل من رسالة فاضية.
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
        FlashBarcodeBox(System.Windows.Media.Brushes.MistyRose);
    }

    // === شاشة البحث الكاملة — بحث محلي بس (SQLite)، بلا أي اتصال API ===

    private void SearchByNameButton_Click(object sender, RoutedEventArgs e)
    {
        OpenSearchWindow(initialTerm: string.Empty, priceCheckOnly: false);
    }

    private void PriceCheckButton_Click(object sender, RoutedEventArgs e)
    {
        OpenSearchWindow(initialTerm: string.Empty, priceCheckOnly: true);
    }

    /// <summary>
    /// نافذة معيارية (ShowDialog) - تحجب هذه الشاشة لحد ما الكاشير يختار
    /// صنف أو يقفل (Escape). بوضع الإضافة (priceCheckOnly=false) وDialogResult
    /// صار true، بنضيف المنتج/الوحدة المختارة مباشرة لنفس السلة الحالية.
    /// بوضع معرفة السعر، ما في شي يُضاف إطلاقًا مهما اختار الكاشير.
    /// </summary>
    private void OpenSearchWindow(string initialTerm, bool priceCheckOnly)
    {
        var window = new ProductSearchWindow(_dbPath, initialTerm, priceCheckOnly) { Owner = this };
        var result = window.ShowDialog();

        if (result == true && window.SelectedProduct is LocalProduct selectedProduct && window.SelectedUnit is LocalProductUnit selectedUnit)
        {
            using var db = new LocalDbContext(_dbPath);
            AddResolvedItem(db, selectedProduct, selectedUnit);
        }

        BarcodeBox.Focus();
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
    /// يُطلَق كل ما DataGrid يولّد صفًّا (سطر جديد أو إعادة استخدام صف
    /// موجود عند التمرير) - بنفحص إذا كان هذا بالضبط آخر سطر انضاف
    /// للسلة (_lastAddedLine)، ولو أه نشغّل أنيميشن وميض خلفية عليه
    /// (Storyboard حقيقي على الصف نفسه، لا بس على مربع الباركود) - تمييز
    /// بصري إضافي وأوضح إنه الصنف انضاف فعليًا، خصوصًا بسلة فيها أصناف كثيرة.
    /// </summary>
    private void CartGrid_LoadingRow(object sender, System.Windows.Controls.DataGridRowEventArgs e)
    {
        if (_lastAddedLine is null || !ReferenceEquals(e.Row.Item, _lastAddedLine))
        {
            return;
        }

        _lastAddedLine = null;

        var row = e.Row;
        var flashBrush = new System.Windows.Media.SolidColorBrush(System.Windows.Media.Colors.PaleGreen);
        row.Background = flashBrush;

        var animation = new System.Windows.Media.Animation.ColorAnimation
        {
            To = System.Windows.Media.Colors.White,
            Duration = TimeSpan.FromMilliseconds(600),
            FillBehavior = System.Windows.Media.Animation.FillBehavior.HoldEnd
        };
        // بعد ما الأنيميشن توصل للأبيض، نصفّر القيمة المحلية بالكامل -
        // يرجّع الصف يتبع تلوين DataGrid العادي (أبيض/رمادي فاتح متبادل)
        // بدل ما يعلق أبيض دائمًا حتى لو صار Row متبادل اللون لاحقًا.
        animation.Completed += (_, _) => row.ClearValue(System.Windows.Controls.Control.BackgroundProperty);

        flashBrush.BeginAnimation(System.Windows.Media.SolidColorBrush.ColorProperty, animation);
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
