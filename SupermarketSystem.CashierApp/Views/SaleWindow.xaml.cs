using System.Collections.ObjectModel;
using System.Globalization;
using System.IO;
using System.Text.RegularExpressions;
using System.Text.Json;
using System.Windows;
using System.Windows.Controls;
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

    /// <summary>
    /// السلة الحالية نازلة من طلب جاهز (مساعد الكاشير) - بينبعت مع البيع عشان الطلب يتسكّر بالسيرفر.
    /// بيتصفّر بعد كل بيعة، وبينتقل مع السلة لو انعلّقت.
    /// </summary>
    private Guid? _preparedOrderId;
    private int? _preparedTicketNumber;

    /// <summary>مجلد local.db - فيه ملف الفواتير المعلّقة (HeldCartStore).</summary>
    private string LocalDirectory => Path.GetDirectoryName(_dbPath) ?? "";

    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase
    };

    // فحص اتصال دوري خفيف - يحدّد بس تفعيل/تعطيل زر "مزامنة الآن"، لا علاقة
    // له بتحميل بيانات الشاشة نفسها (تلك محلية بالكامل، راجع LoadPaymentMethodsAsync).
    // 10 ثواني: كافية تلتقط انقطاع/عودة نت بسرعة معقولة، بلا ضغط زائد على السيرفر.
    private readonly DispatcherTimer _connectivityTimer;

    /// <summary>بيخفي سطر حالة آخر عملية بعد ثواني (بدل الرسالة المنبثقة بعد كل بيعة).</summary>
    private readonly DispatcherTimer _statusTimer = new() { Interval = TimeSpan.FromSeconds(7) };

    /// <summary>تنبيه "بلا طابعة مضبوطة" بيطلع مرة وحدة بكل تشغيل للتطبيق، مش مع كل بيعة.</summary>
    private static bool s_printerNotConfiguredNoticeShown;

    /// <summary>كمية*باركود - مثلًا "5*6250000000011" أو "1.5*..." (وزن). الكمية لازم تكون أكبر من صفر.</summary>
    private static readonly Regex QuantityPrefix = new(@"^\s*(\d+(?:[.,]\d+)?)\s*\*\s*(.*)$", RegexOptions.Compiled);

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

        _statusTimer.Tick += (_, _) =>
        {
            _statusTimer.Stop();
            SaleStatusBanner.Visibility = Visibility.Collapsed;
        };
        Closed += (_, _) => _statusTimer.Stop();
    }

    /// <summary>سطر حالة داخل الشاشة: أخضر للنجاح، برتقالي لتنبيه - بلا ما يوقف الكاشير بنافذة.</summary>
    private void ShowSaleStatus(string message, bool isWarning)
    {
        SaleStatusText.Text = message;
        SaleStatusText.Foreground = isWarning
            ? (System.Windows.Media.Brush)FindResource("AccentHoverBrush")
            : (System.Windows.Media.Brush)FindResource("GreenBrush");
        SaleStatusBanner.Background = isWarning
            ? (System.Windows.Media.Brush)FindResource("AccentSoftBrush")
            : new System.Windows.Media.SolidColorBrush(System.Windows.Media.Color.FromRgb(0xEC, 0xFD, 0xF3));
        SaleStatusBanner.Visibility = Visibility.Visible;
        _statusTimer.Stop();
        _statusTimer.Start();
    }

    private async void SaleWindow_Loaded(object sender, RoutedEventArgs e)
    {
        RefreshHeldCount();
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

    /// <summary>
    /// اختصارات الكيبورد (مراجعة UI/UX 24/9، انبنت 28/9/2026) - الكاشير ما بيترك الكيبورد/الماسح:
    /// F2 بحث عن صنف · F4 معرفة السعر · F8 تعليق · F9 المعلّقة · F12 إتمام البيع ·
    /// Esc يمسح خانة الباركود، ولو فاضية بيسأل يفرّغ السلة · Delete (والباركود فاضي) يحذف آخر سطر ·
    /// + و − بلوحة الأرقام بيعدّلوا كمية آخر سطر (برّا خانة الكمية نفسها - هناك بتنكتب عادي).
    /// PreviewKeyDown عالنافذة: بيشتغل وين ما كان التركيز، قبل ما خانة نص تاكل المفتاح.
    /// </summary>
    private void SaleWindow_PreviewKeyDown(object sender, KeyEventArgs e)
    {
        var inQuantityBox = e.OriginalSource is TextBox box && !ReferenceEquals(box, BarcodeBox)
                            && !ReferenceEquals(box, TenderedAmountBox) && !ReferenceEquals(box, CustomerPhoneBox);

        switch (e.Key)
        {
            case Key.F2:
                e.Handled = true;
                OpenSearchWindow(initialTerm: BarcodeBox.Text.Trim(), priceCheckOnly: false);
                BarcodeBox.Clear();
                return;
            case Key.F4:
                e.Handled = true;
                OpenSearchWindow(initialTerm: string.Empty, priceCheckOnly: true);
                return;
            case Key.F8:
                e.Handled = true;
                HoldButton_Click(HoldButton, new RoutedEventArgs());
                return;
            case Key.F9:
                e.Handled = true;
                HeldSalesButton_Click(HeldSalesButton, new RoutedEventArgs());
                return;
            case Key.F12:
                e.Handled = true;
                if (CompleteSaleButton.IsEnabled)
                {
                    CompleteSaleButton_Click(CompleteSaleButton, new RoutedEventArgs());
                }
                return;
            case Key.Escape:
                e.Handled = true;
                if (BarcodeBox.Text.Length > 0)
                {
                    BarcodeBox.Clear();
                }
                else if (_cart.Count > 0
                         && MessageBox.Show(this, "تفريغ السلة الحالية؟ (الأصناف بتنشال، ما في إشي بينسجّل)", "إلغاء الفاتورة الحالية",
                             MessageBoxButton.YesNo, MessageBoxImage.Question, MessageBoxResult.No) == MessageBoxResult.Yes)
                {
                    ClearCurrentCart();
                    ShowSaleStatus("انفرّغت السلة.", isWarning: false);
                }
                BarcodeBox.Focus();
                return;
            case Key.Delete when !inQuantityBox && BarcodeBox.Text.Length == 0 && _cart.Count > 0:
                e.Handled = true;
                var removed = _cart[^1];
                _cart.Remove(removed);
                if (_cart.Count == 0)
                {
                    _preparedOrderId = null;
                    _preparedTicketNumber = null;
                }
                UpdateTotal();
                ShowSaleStatus($"انشال: {removed.ProductName}", isWarning: false);
                BarcodeBox.Focus();
                return;
            case Key.Add or Key.Subtract when !inQuantityBox && !ReferenceEquals(e.OriginalSource, BarcodeBox)
                                                && !ReferenceEquals(e.OriginalSource, TenderedAmountBox) && _cart.Count > 0:
                // جوّا خانة الباركود بيتعالج بـBarcodeBox_KeyDown (والحقل فاضي)؛ هون لما التركيز على زر أو الجدول.
                e.Handled = true;
                var lastLine = _cart[^1];
                var delta = e.Key == Key.Add ? 1m : -1m;
                if (lastLine.Quantity + delta > 0)
                {
                    SetLineQuantity(lastLine, lastLine.Quantity + delta);
                }
                BarcodeBox.Focus();
                return;
        }
    }

    /// <summary>تفريغ السلة بلا تسجيل (Esc) - نفس تنظيف ما بعد البيع.</summary>
    private void ClearCurrentCart()
    {
        _cart.Clear();
        _preparedOrderId = null;
        _preparedTicketNumber = null;
        TenderedAmountBox.Clear();
        CustomerPhoneBox.Clear();
        UpdateTotal();
    }

    /// <summary>Enter وTab معًا - قرّائات باركود مختلفة بتستخدم أحدهما كفاصل نهاية المسح، بلا إعداد موحَّد.</summary>
    private void BarcodeBox_KeyDown(object sender, KeyEventArgs e)
    {
        if (e.Key is Key.Enter or Key.Tab)
        {
            e.Handled = true; // يمنع Tab من نقل التركيز لعنصر تاني قبل ما نعالج المسح
            AddScannedItem();
            return;
        }

        // + و − والحقل فاضي = كمية آخر صنف انضاف (أسرع من الماوس)؛ الحذف من زر ✕ بالسطر.
        if (BarcodeBox.Text.Length == 0 && _cart.Count > 0 && e.Key is Key.Add or Key.OemPlus or Key.Subtract or Key.OemMinus)
        {
            e.Handled = true;
            var lastLine = _cart[^1];
            var delta = e.Key is Key.Add or Key.OemPlus ? 1m : -1m;
            if (lastLine.Quantity + delta > 0)
            {
                SetLineQuantity(lastLine, lastLine.Quantity + delta);
            }
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
        var (quantity, barcodeValue) = ParseQuantityPrefix(BarcodeBox.Text);
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

                    AddResolvedItem(db, product, unit, quantity);
                    return;
                }
            }

            OpenSearchWindow(barcodeValue, priceCheckOnly: false, quantity);
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

    private static (decimal Quantity, string Barcode) ParseQuantityPrefix(string rawText)
    {
        var match = QuantityPrefix.Match(rawText);
        if (match.Success
            && decimal.TryParse(match.Groups[1].Value.Replace(',', '.'), NumberStyles.Number, CultureInfo.InvariantCulture, out var quantity)
            && quantity > 0)
        {
            return (quantity, match.Groups[2].Value.Trim());
        }

        return (1m, rawText.Trim());
    }

    /// <summary>نقطة إضافة موحَّدة - يقرر بين صنف عادي أو متتبَّع دفعات، مستخدَمة من مسار الباركود المباشر وشاشة البحث الكاملة معًا، صفر تكرار منطق.</summary>
    private void AddResolvedItem(LocalDbContext db, LocalProduct product, LocalProductUnit unit, decimal quantity = 1m)
    {
        if (product.IsBatchTracked)
        {
            AddBatchTrackedItem(db, product, unit, quantity);
        }
        else
        {
            AddSimpleItem(product, unit, quantity);
        }
    }

    /// <summary>
    /// السعر ورقم نسخة الكتالوج بنفس القراءة (معاملة قراءة وحدة) - المزامنة بتكتب الاثنين
    /// سوا بمعاملة وحدة (CatalogSyncService)، فهيك ما بنقرأ سعر جديد برقم نسخة قديم أو العكس.
    /// </summary>
    private (decimal BaseUnitPrice, long? CatalogVersion) ReadPriceSnapshot(Guid productId)
    {
        using var db = new LocalDbContext(_dbPath);
        using var transaction = db.Database.BeginTransaction();
        var baseUnitPrice = db.Products.Where(p => p.ProductId == productId).Select(p => p.SellingPrice).First();
        var catalogVersion = db.SyncStates.Select(s => (long?)s.LastSyncedCatalogVersion).FirstOrDefault();
        return (baseUnitPrice, catalogVersion);
    }

    private void AddSimpleItem(LocalProduct product, LocalProductUnit unit, decimal quantity)
    {
        FlashAddedSuccess();

        var existingLine = _cart.FirstOrDefault(l => l.ProductUnitId == unit.UnitId && l.ProductBatchId is null);
        if (existingLine is not null)
        {
            existingLine.Quantity += quantity;
            CartGrid.Items.Refresh();
        }
        else
        {
            // سعر الوحدة = سعر الوحدة الأساسية × معامل التحويل (كرتونة = 12 حبة مثلًا) - نفس
            // حساب السيرفر بالضبط (PRICING ASSUMPTION بـCompleteSaleHandler). كان بياخد سعر
            // الحبة لأي وحدة، فأي بيع بوحدة غير أساسية كان بينرفض بالسيرفر ويعلق بالطابور.
            var (baseUnitPrice, catalogVersion) = ReadPriceSnapshot(product.ProductId);
            var newLine = new CartLine
            {
                ProductId = product.ProductId,
                ProductUnitId = unit.UnitId,
                ProductName = product.Name,
                UnitName = unit.UnitName,
                Quantity = quantity,
                UnitPrice = baseUnitPrice * unit.ConversionFactorToBase,
                CatalogVersion = catalogVersion
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
    private void AddBatchTrackedItem(LocalDbContext db, LocalProduct product, LocalProductUnit unit, decimal quantity)
    {
        var existingLine = _cart.FirstOrDefault(l => l.ProductUnitId == unit.UnitId && l.ProductBatchId is not null);

        if (existingLine is not null)
        {
            var currentBatch = db.ProductBatches.FirstOrDefault(b => b.BatchId == existingLine.ProductBatchId);
            existingLine.Quantity += quantity;

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
            .Where(b => b.ProductId == product.ProductId && b.QuantityAvailable >= quantity)
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

        var (batchBaseUnitPrice, batchCatalogVersion) = ReadPriceSnapshot(product.ProductId);
        var newBatchLine = new CartLine
        {
            ProductId = product.ProductId,
            ProductUnitId = unit.UnitId,
            ProductBatchId = batch.BatchId,
            BatchNumber = batch.BatchNumber,
            ProductName = product.Name,
            UnitName = unit.UnitName,
            Quantity = quantity,
            UnitPrice = batchBaseUnitPrice * unit.ConversionFactorToBase,
            CatalogVersion = batchCatalogVersion,
            NeedsReview = needsReview
        };
        _lastAddedLine = newBatchLine;
        _cart.Add(newBatchLine);

        UpdateTotal();
    }

    private void UpdateTotal()
    {
        var total = _cart.Sum(l => l.LineTotal);
        TotalText.Text = $"الإجمالي: {total:0.000} د.أ";
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
            TenderedAmountBox.Text = jodAmount.ToString("0.000", CultureInfo.InvariantCulture);
        }
    }

    /// <summary>كبسة فئة نقدية دولار - تُحوَّل للدينار بسعر الصرف من الإعدادات قبل ما تنحط بصندوق المبلغ المستلم.</summary>
    private void UsdDenominationButton_Click(object sender, RoutedEventArgs e)
    {
        if (sender is Button { Tag: string tag } && decimal.TryParse(tag, out var usdAmount))
        {
            TenderedAmountBox.Text = (usdAmount * _usdToJodExchangeRate).ToString("0.000", CultureInfo.InvariantCulture);
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
            ChangeAmountText.Text = "0.000";
            ChangeAmountText.Foreground = System.Windows.Media.Brushes.Gray;
            return;
        }

        var change = tendered - total;
        ChangeAmountText.Text = change.ToString("0.000", CultureInfo.InvariantCulture);
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

        // رقم الزبون اختياري - بس لو انكتب لازم يكون رقم منطقي (7-15 رقم)، عشان ما ينحفظ غلط على الفاتورة.
        var customerPhone = CustomerPhoneBox.Text.Trim();
        var phoneDigits = customerPhone.Count(char.IsAsciiDigit);
        if (customerPhone.Length > 0
            && (phoneDigits is < 7 or > 15 || customerPhone.Any(ch => !char.IsAsciiDigit(ch) && ch is not (' ' or '-' or '+'))))
        {
            ShowError("رقم الزبون مش صحيح - اكتبه أرقام (مثلًا 0791234567) أو فضّي الخانة.");
            CustomerPhoneBox.Focus();
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
                productBatchId = l.ProductBatchId,
                catalogVersion = l.CatalogVersion
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
            },
            // طلب جاهز من مساعد الكاشير (لو السلة نزلت منه) - بيتسكّر بالسيرفر بنفس معاملة البيع.
            preparedOrderId = _preparedOrderId,
            // السيرفر بيربط الزبون المسجّل بنفس الرقم (حتى لو البيعة وصلت متأخرة من الطابور الأوفلاين).
            customerPhone = customerPhone.Length > 0 ? customerPhone : null
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
        _preparedOrderId = null;
        _preparedTicketNumber = null;
        TenderedAmountBox.Clear();
        CustomerPhoneBox.Clear();
        UpdateTotal();
        CompleteSaleButton.IsEnabled = true;
        BarcodeBox.Focus();

        // سطر حالة بالشاشة بدل رسالة منبثقة بعد كل بيعة (طلب صاحب المشروع 28/9/2026: "مستفزة").
        // الإرسال والطباعة منفصلين: فشل الطباعة أبدًا ما يعني فشل البيع.
        var saleMessage = sendResult.Success
            ? $"✓ تم البيع ({total:0.000} د.أ) وانبعت للسيرفر"
            : $"✓ تم البيع ({total:0.000} د.أ) - انحفظ على الجهاز وبينبعت لحاله";
        var isWarning = !sendResult.Success;

        if (printResult.NotConfigured)
        {
            if (!s_printerNotConfiguredNoticeShown)
            {
                s_printerNotConfiguredNoticeShown = true;
                saleMessage += " · ما في طابعة مضبوطة، الفاتورة ما انطبعت";
                isWarning = true;
            }
        }
        else if (!printResult.Success)
        {
            saleMessage += $" · ⚠ تعذّرت الطباعة: {printResult.ErrorMessage}";
            isWarning = true;
        }

        ShowSaleStatus(saleMessage, isWarning);
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
    private void OpenSearchWindow(string initialTerm, bool priceCheckOnly, decimal quantity = 1m)
    {
        var window = new ProductSearchWindow(_dbPath, initialTerm, priceCheckOnly) { Owner = this };
        var result = window.ShowDialog();

        if (result == true && window.SelectedProduct is LocalProduct selectedProduct && window.SelectedUnit is LocalProductUnit selectedUnit)
        {
            using var db = new LocalDbContext(_dbPath);
            AddResolvedItem(db, selectedProduct, selectedUnit, quantity);
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

    // === تعديل الكمية بالسلة (بدل مسح نفس الصنف كذا مرة) ===

    private static CartLine? LineOf(object sender) => (sender as FrameworkElement)?.DataContext as CartLine;

    /// <summary>
    /// كمية جديدة لسطر: لسطر دفعات بيعيد فحص الرصيد المحلي (تجاوزه = ⚠ مراجعة، مش منع - §1.6، السيرفر هو
    /// الحكم). الكمية لازم تكون أكبر من صفر؛ الحذف من زر ✕.
    /// </summary>
    private void SetLineQuantity(CartLine line, decimal quantity)
    {
        if (quantity <= 0)
        {
            return;
        }

        line.Quantity = quantity;
        if (line.ProductBatchId is { } batchId)
        {
            using var db = new LocalDbContext(_dbPath);
            var batch = db.ProductBatches.FirstOrDefault(b => b.BatchId == batchId);
            line.NeedsReview = batch is null || quantity > batch.QuantityAvailable;
        }

        CartGrid.Items.Refresh();
        UpdateTotal();
    }

    private void IncreaseQuantity_Click(object sender, RoutedEventArgs e)
    {
        if (LineOf(sender) is { } line)
        {
            SetLineQuantity(line, line.Quantity + 1);
        }

        BarcodeBox.Focus();
    }

    private void DecreaseQuantity_Click(object sender, RoutedEventArgs e)
    {
        if (LineOf(sender) is { } line && line.Quantity - 1 > 0)
        {
            SetLineQuantity(line, line.Quantity - 1);
        }

        BarcodeBox.Focus();
    }

    private void RemoveLine_Click(object sender, RoutedEventArgs e)
    {
        if (LineOf(sender) is { } line)
        {
            _cart.Remove(line);
            if (_cart.Count == 0)
            {
                // السلة فضيت - الطلب الجاهز ما عاد إله علاقة بالبيعة الجاية.
                _preparedOrderId = null;
                _preparedTicketNumber = null;
            }

            UpdateTotal();
        }

        BarcodeBox.Focus();
    }

    private void QuantityBox_KeyDown(object sender, KeyEventArgs e)
    {
        if (e.Key == Key.Enter)
        {
            e.Handled = true;
            BarcodeBox.Focus(); // بيطلق LostKeyboardFocus تحت، اللي بيطبّق الكمية
        }
    }

    private void QuantityBox_LostKeyboardFocus(object sender, KeyboardFocusChangedEventArgs e)
    {
        if (sender is not TextBox box || box.DataContext is not CartLine line)
        {
            return;
        }

        if (decimal.TryParse(box.Text.Trim().Replace(',', '.'), NumberStyles.Number, CultureInfo.InvariantCulture, out var quantity)
            && quantity > 0)
        {
            if (quantity != line.Quantity)
            {
                SetLineQuantity(line, quantity);
            }
        }
        else
        {
            // رقم غلط أو صفر - رجّع الكمية القديمة بدل ما نسجّل إشي غلط.
            box.Text = line.Quantity.ToString("0.###", CultureInfo.InvariantCulture);
        }
    }

    // === عمليات جانبية بلا ما تطلع من شاشة البيع ===

    /// <summary>رجوع للشاشة الرئيسية (رفع فاتورة AI وغيرها) بلا تسجيل خروج - المزامنة الخلفية بتضل شغّالة.</summary>
    private void HomeButton_Click(object sender, RoutedEventArgs e)
    {
        if (_cart.Count > 0)
        {
            var confirm = MessageBox.Show(
                "فيه أصناف بالسلة لسه ما اتباعت - هل متأكد من الرجوع للقائمة؟ رح تضيع السلة.",
                "تأكيد", MessageBoxButton.YesNo, MessageBoxImage.Warning, MessageBoxResult.No);
            if (confirm != MessageBoxResult.Yes)
            {
                return;
            }
        }

        var mainWindow = new MainWindow(_apiClient, _authSession, _dbPath, _receiptPrinter, _backgroundSync, _adminScreenPassword);
        mainWindow.Show();
        Close();
    }

    private void ReturnButton_Click(object sender, RoutedEventArgs e)
    {
        new ReturnWindow(_apiClient, _authSession) { Owner = this }.ShowDialog();
        BarcodeBox.Focus();
    }

    private void VoidSaleButton_Click(object sender, RoutedEventArgs e)
    {
        new VoidSaleWindow(_apiClient, _authSession) { Owner = this }.ShowDialog();
        BarcodeBox.Focus();
    }

    // === تعليق فاتورة (28/9/2026): زبون بدو يبدّل صنف والطابور واقف ===

    private void RefreshHeldCount()
    {
        var count = HeldCartStore.Count(LocalDirectory);
        HeldSalesButton.Content = $"📋 المعلّقة ({count})";
        HeldSalesButton.FontWeight = count > 0 ? FontWeights.Bold : FontWeights.Normal;
    }

    /// <summary>بيحط السلة الحالية بملف المعلّقة وبيفضّي الشاشة للزبون اللي بعده - false لو السلة فاضية.</summary>
    private bool HoldCurrentCart()
    {
        if (_cart.Count == 0)
        {
            return false;
        }

        var held = new HeldCart
        {
            HeldAtLocal = DateTime.Now,
            CashierName = _authSession.FullName,
            Lines = _cart.ToList(),
            PreparedOrderId = _preparedOrderId,
            PreparedTicketNumber = _preparedTicketNumber,
            CustomerPhone = string.IsNullOrWhiteSpace(CustomerPhoneBox.Text) ? null : CustomerPhoneBox.Text.Trim()
        };

        try
        {
            HeldCartStore.Add(LocalDirectory, held);
        }
        catch (Exception ex)
        {
            ShowError($"ما قدرت أعلّق الفاتورة: {ex.Message}");
            return false;
        }

        _cart.Clear();
        _preparedOrderId = null;
        _preparedTicketNumber = null;
        TenderedAmountBox.Clear();
        CustomerPhoneBox.Clear();
        UpdateTotal();
        RefreshHeldCount();
        ShowSaleStatus($"⏸ انعلّقت الفاتورة ({held.ItemCount} صنف، {held.Total:0.000} د.أ) - بترجعها من \"المعلّقة\"", isWarning: false);
        return true;
    }

    /// <summary>
    /// قبل ما تنزل سلة تانية (معلّقة أو طلب جاهز): لو في أصناف بالشاشة، بنسأل نعلّقها - ما في طريقة تضيع سلة
    /// بالغلط. false = الكاشير لغى.
    /// </summary>
    private bool MakeRoomForAnotherCart()
    {
        if (_cart.Count == 0)
        {
            return true;
        }

        var answer = MessageBox.Show(
            "في أصناف بالشاشة هلق - بدك تعلّقها وتفتح التانية؟",
            "السلة الحالية", MessageBoxButton.YesNo, MessageBoxImage.Question, MessageBoxResult.Yes);
        return answer == MessageBoxResult.Yes && HoldCurrentCart();
    }

    private void HoldButton_Click(object sender, RoutedEventArgs e)
    {
        if (!HoldCurrentCart() && _cart.Count == 0)
        {
            ShowError("السلة فاضية - ما في إشي يتعلّق.");
        }

        BarcodeBox.Focus();
    }

    private void HeldSalesButton_Click(object sender, RoutedEventArgs e)
    {
        var window = new HeldSalesWindow(LocalDirectory) { Owner = this };
        if (window.ShowDialog() == true && window.ResumedCart is { } resumed)
        {
            if (!MakeRoomForAnotherCart())
            {
                // رجّعها للملف - الكاشير لغى، ما لازم تضيع.
                HeldCartStore.Add(LocalDirectory, resumed);
            }
            else
            {
                foreach (var line in resumed.Lines)
                {
                    _cart.Add(line);
                }

                _preparedOrderId = resumed.PreparedOrderId;
                _preparedTicketNumber = resumed.PreparedTicketNumber;
                CustomerPhoneBox.Text = resumed.CustomerPhone ?? "";
                HideError();
                UpdateTotal();
                ShowSaleStatus($"↩ رجعت الفاتورة المعلّقة ({resumed.ItemCount} صنف، {resumed.Total:0.000} د.أ)", isWarning: false);
            }
        }

        RefreshHeldCount();
        BarcodeBox.Focus();
    }

    // === طلبات مساعد الكاشير (28/9/2026): المساعد ضرب الأغراض من تلفونه، الكاشير بيحاسب ===

    private void PreparedOrdersButton_Click(object sender, RoutedEventArgs e)
    {
        var window = new PreparedOrdersWindow(_apiClient, _authSession.BranchId) { Owner = this };
        if (window.ShowDialog() != true || window.SelectedOrder is not { } order || !MakeRoomForAnotherCart())
        {
            BarcodeBox.Focus();
            return;
        }

        // الأصناف من الكتالوج المحلي (سعر الفرع الحالي ورقم نسخته - نفس أي ضربة باركود)، مش سعر لحظة التجهيز.
        var missing = new List<string>();
        using (var db = new LocalDbContext(_dbPath))
        {
            foreach (var item in order.Items)
            {
                var product = db.Products.FirstOrDefault(p => p.ProductId == item.ProductId);
                var unit = db.ProductUnits.FirstOrDefault(u => u.UnitId == item.ProductUnitId);
                if (product is null || unit is null || !product.IsAvailableForSale)
                {
                    missing.Add(item.ProductName);
                    continue;
                }

                AddResolvedItem(db, product, unit, item.Quantity);
            }
        }

        if (_cart.Count > 0)
        {
            _preparedOrderId = order.Id;
            _preparedTicketNumber = order.TicketNumber;
        }

        if (missing.Count > 0)
        {
            ShowError($"أصناف مش موجودة على الجهاز (اعمل مزامنة واضربها يدويًا): {string.Join("، ", missing)}");
        }

        ShowSaleStatus(
            $"🛍 طلب {order.TicketNumber} نزل بالسلة (جهّزه {order.PreparedByName})" +
            (string.IsNullOrWhiteSpace(order.Note) ? "" : $" · {order.Note}"),
            isWarning: missing.Count > 0);
        BarcodeBox.Focus();
    }

    /// <summary>سحب شريك من الصندوق - الشريك بيثبت هويته بيوزره وكلمة سره (راجع PartnerWithdrawalWindow).</summary>
    private void PartnerWithdrawalButton_Click(object sender, RoutedEventArgs e)
    {
        if (_authSession.BranchId is null)
        {
            ShowError("لا يوجد فرع مرتبط بجلستك - راجع الإدارة.");
            return;
        }

        var window = new PartnerWithdrawalWindow(_apiClient, _authSession.BranchId.Value) { Owner = this };
        if (window.ShowDialog() == true && window.ResultMessage is { } message)
        {
            ShowSaleStatus(message, isWarning: false);
        }

        BarcodeBox.Focus();
    }

    private void InvoiceSearchButton_Click(object sender, RoutedEventArgs e)
    {
        new InvoiceSearchWindow(_apiClient, _authSession) { Owner = this }.ShowDialog();
        BarcodeBox.Focus();
    }

    /// <summary>
    /// "فتح الصندوق" بلا بيع: تسجيل بس بالسيرفر (مين وإمتى) - ما في ربط بدرج حقيقي لسه. الإدارة بتشوف كم مرة
    /// لكل كاشير بتقرير "فتح الصندوق بلا بيع".
    /// </summary>
    private async void DrawerOpenButton_Click(object sender, RoutedEventArgs e)
    {
        if (_authSession.BranchId is null)
        {
            ShowError("لا يوجد فرع مرتبط بجلستك - راجع الإدارة.");
            return;
        }

        DrawerOpenButton.IsEnabled = false;
        var (success, errorMessage) = await _apiClient.RecordDrawerOpenAsync(_authSession.BranchId.Value, reason: null, CancellationToken.None);
        DrawerOpenButton.IsEnabled = true;

        ShowSaleStatus(success ? "🗄 انسجّل فتح الصندوق" : $"ما انسجّل فتح الصندوق: {errorMessage}", isWarning: !success);
        BarcodeBox.Focus();
    }
}
