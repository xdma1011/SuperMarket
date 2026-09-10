using System.Windows;
using System.Windows.Input;
using System.Windows.Threading;
using Microsoft.EntityFrameworkCore;
using SupermarketSystem.CashierApp.Local;

namespace SupermarketSystem.CashierApp.Views;

/// <summary>
/// شاشة بحث كاملة بديلة عن مجرد "الباركود غير موجود" — تُفتح لما الباركود
/// المدخَل مش مطابق أو فاضي، أو من زر "معرفة السعر". بحث محلي بس (SQLite)،
/// بالاسم أو جزء من الباركود معًا، مع تأخير ثانيتين بعد التوقف عن الكتابة
/// (لا نبحث على كل حرف) وترقيم صفحات، وتنقّل كامل بالكيبورد.
///
/// وضعان:
///   - إضافة (priceCheckOnly=false): Enter على نتيجة يقفل الشاشة ويرجّع
///     المنتج/الوحدة المختارة للمستدعي (SelectedProduct/SelectedUnit)،
///     هو اللي يقرر AddSimpleItem أو AddBatchTrackedItem - صفر تكرار منطق.
///   - معرفة سعر فقط (priceCheckOnly=true): Enter يعرض تفاصيل السعر بلوحة
///     كبيرة واضحة، بلا ما يضيف شي للفاتورة ولا يقفل الشاشة - الكاشير ممكن
///     يفحص أكتر من صنف ورا بعض لزبون واقف يسأل بس.
/// </summary>
public partial class ProductSearchWindow : Window
{
    private const int PageSize = 20;
    private const int DebounceMilliseconds = 2000;

    private readonly string _dbPath;
    private readonly bool _priceCheckOnly;
    private readonly DispatcherTimer _debounceTimer;

    private List<SearchResultRow> _allResults = new();
    private int _pageNumber = 1;

    public LocalProduct? SelectedProduct { get; private set; }
    public LocalProductUnit? SelectedUnit { get; private set; }

    public ProductSearchWindow(string dbPath, string initialSearchTerm, bool priceCheckOnly)
    {
        InitializeComponent();
        _dbPath = dbPath;
        _priceCheckOnly = priceCheckOnly;

        _debounceTimer = new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(DebounceMilliseconds) };
        _debounceTimer.Tick += (_, _) =>
        {
            _debounceTimer.Stop();
            RunSearch(resetPage: true);
        };

        TitleText.Text = priceCheckOnly ? "معرفة السعر" : "بحث عن صنف";
        ModeHintText.Text = priceCheckOnly
            ? "وضع معرفة السعر فقط - لن تُضاف أي نتيجة للفاتورة."
            : "اختر صنفًا لإضافته مباشرة للفاتورة الحالية.";

        SearchBox.Text = initialSearchTerm;
        Loaded += (_, _) =>
        {
            SearchBox.Focus();
            SearchBox.CaretIndex = SearchBox.Text.Length;
            RunSearch(resetPage: true); // بحث فوري بلا تأخير - المستخدم مستني أصلًا (باركود ما طابق أو زر معرفة السعر)
        };
    }

    private void SearchBox_TextChanged(object sender, System.Windows.Controls.TextChangedEventArgs e)
    {
        _debounceTimer.Stop();
        _debounceTimer.Start();
    }

    private void SearchBox_KeyDown(object sender, KeyEventArgs e)
    {
        if (e.Key == Key.Enter)
        {
            _debounceTimer.Stop();
            RunSearch(resetPage: true);

            if (ResultsList.Items.Count > 0)
            {
                ResultsList.Focus();
                ResultsList.SelectedIndex = 0;
            }
        }
        else if (e.Key == Key.Down && ResultsList.Items.Count > 0)
        {
            ResultsList.Focus();
            ResultsList.SelectedIndex = 0;
        }
    }

    private void ResultsList_KeyDown(object sender, KeyEventArgs e)
    {
        if (e.Key == Key.Enter && ResultsList.SelectedItem is SearchResultRow selected)
        {
            SelectResult(selected);
        }
        else if (e.Key == Key.PageDown)
        {
            ChangePage(_pageNumber + 1);
        }
        else if (e.Key == Key.PageUp)
        {
            ChangePage(_pageNumber - 1);
        }
    }

    private void ResultsList_MouseDoubleClick(object sender, System.Windows.Input.MouseButtonEventArgs e)
    {
        if (ResultsList.SelectedItem is SearchResultRow selected)
        {
            SelectResult(selected);
        }
    }

    private void Window_KeyDown(object sender, KeyEventArgs e)
    {
        if (e.Key == Key.Escape)
        {
            // بوضع معرفة السعر، Escape الأولى ترجع للبحث لو اللوحة ظاهرة، بس لو مقفولة أصلًا تقفل الشاشة كلها.
            if (_priceCheckOnly && PriceCheckPanel.Visibility == Visibility.Visible)
            {
                PriceCheckPanel.Visibility = Visibility.Collapsed;
                SearchBox.Focus();
                return;
            }

            DialogResult = false;
            Close();
        }
        else if (e.Key == Key.PageDown)
        {
            ChangePage(_pageNumber + 1);
        }
        else if (e.Key == Key.PageUp)
        {
            ChangePage(_pageNumber - 1);
        }
    }

    private void CloseButton_Click(object sender, RoutedEventArgs e)
    {
        DialogResult = false;
        Close();
    }

    private void PrevPageButton_Click(object sender, RoutedEventArgs e) => ChangePage(_pageNumber - 1);
    private void NextPageButton_Click(object sender, RoutedEventArgs e) => ChangePage(_pageNumber + 1);

    private void SelectResult(SearchResultRow row)
    {
        using var db = new LocalDbContext(_dbPath);
        var product = db.Products.FirstOrDefault(p => p.ProductId == row.ProductId);
        var unit = db.ProductUnits.FirstOrDefault(u => u.UnitId == row.UnitId);

        if (product is null || unit is null)
        {
            return;
        }

        if (_priceCheckOnly)
        {
            PriceCheckProductText.Text = product.Name;
            PriceCheckUnitText.Text = string.IsNullOrEmpty(row.MatchedBarcode)
                ? $"الوحدة: {unit.UnitName}"
                : $"الوحدة: {unit.UnitName} · الباركود: {row.MatchedBarcode}";
            PriceCheckPriceText.Text = $"{row.Price:0.00}";
            PriceCheckPanel.Visibility = Visibility.Visible;
            return;
        }

        SelectedProduct = product;
        SelectedUnit = unit;
        DialogResult = true;
        Close();
    }

    /// <summary>
    /// بحث محلي بس (SQLite) - بالاسم أو جزء من الباركود معًا، ثم دمج
    /// النتيجتين وترتيبهما وترقيمهما بالذاكرة (كتالوج محل واحد، حجم صغير
    /// بطبيعته - لا داعي لتعقيد الاستعلام لمجرد "الأداء").
    /// </summary>
    private void RunSearch(bool resetPage)
    {
        if (resetPage)
        {
            _pageNumber = 1;
        }

        var term = SearchBox.Text.Trim();

        using var db = new LocalDbContext(_dbPath);

        var byName = db.Products
            .Where(p => p.IsAvailableForSale && (term == "" || p.Name.Contains(term)))
            .Join(db.ProductUnits.Where(u => u.IsBaseUnit), p => p.ProductId, u => u.ProductId,
                (p, u) => new SearchResultRow
                {
                    ProductId = p.ProductId,
                    ProductName = p.Name,
                    UnitId = u.UnitId,
                    UnitName = u.UnitName,
                    Price = p.SellingPrice,
                    MatchedBarcode = null
                })
            .ToList();

        var byBarcode = term == ""
            ? new List<SearchResultRow>()
            : db.ProductBarcodes
                .Where(b => b.BarcodeValue.Contains(term))
                .Join(db.ProductUnits, b => b.ProductUnitId, u => u.UnitId, (b, u) => new { b.BarcodeValue, Unit = u })
                .Join(db.Products.Where(p => p.IsAvailableForSale), x => x.Unit.ProductId, p => p.ProductId,
                    (x, p) => new SearchResultRow
                    {
                        ProductId = p.ProductId,
                        ProductName = p.Name,
                        UnitId = x.Unit.UnitId,
                        UnitName = x.Unit.UnitName,
                        Price = p.SellingPrice,
                        MatchedBarcode = x.BarcodeValue
                    })
                .ToList();

        // دمج - لو نفس (منتج، وحدة) ظهر بالاثنين، نفضّل نسخة الباركود (فيها
        // معلومة إضافية مفيدة للكاشير: أي باركود بالضبط طابق).
        var merged = byBarcode
            .Concat(byName.Where(n => !byBarcode.Any(b => b.ProductId == n.ProductId && b.UnitId == n.UnitId)))
            .OrderBy(r => r.ProductName)
            .ThenBy(r => r.UnitName)
            .ToList();

        _allResults = merged;
        RenderCurrentPage();
    }

    private void ChangePage(int newPageNumber)
    {
        var totalPages = Math.Max(1, (int)Math.Ceiling(_allResults.Count / (double)PageSize));
        if (newPageNumber < 1 || newPageNumber > totalPages)
        {
            return;
        }

        _pageNumber = newPageNumber;
        RenderCurrentPage();
    }

    private void RenderCurrentPage()
    {
        var totalPages = Math.Max(1, (int)Math.Ceiling(_allResults.Count / (double)PageSize));
        var pageItems = _allResults.Skip((_pageNumber - 1) * PageSize).Take(PageSize).ToList();

        ResultsList.ItemsSource = pageItems;
        NoResultsText.Visibility = _allResults.Count == 0 ? Visibility.Visible : Visibility.Collapsed;
        PageInfoText.Text = $"صفحة {_pageNumber} من {totalPages} ({_allResults.Count} نتيجة)";
        PrevPageButton.IsEnabled = _pageNumber > 1;
        NextPageButton.IsEnabled = _pageNumber < totalPages;
    }

    private sealed class SearchResultRow
    {
        public Guid ProductId { get; set; }
        public string ProductName { get; set; } = string.Empty;
        public Guid UnitId { get; set; }
        public string UnitName { get; set; } = string.Empty;
        public decimal Price { get; set; }
        public string? MatchedBarcode { get; set; }
    }
}
