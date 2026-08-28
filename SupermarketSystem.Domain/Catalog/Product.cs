using SupermarketSystem.Domain.Common;

namespace SupermarketSystem.Domain.Catalog;

/// <summary>
/// Fixed, code-driving values — not admin-addable master data, hence an
/// enum rather than an entity (Architecture Review §4, reclassified from
/// the original entity list).
/// </summary>
public enum ProductStatus
{
    Active = 1,
    Inactive = 2,
    Discontinued = 3,
    PendingApproval = 4
}

/// <summary>
/// Aggregate root. Global catalog data (Architecture Review §1 v2):
/// name/description/category/units/barcodes/images/notes are shared across
/// every branch — the same product, not one row per branch. Branch-specific
/// concerns (selling price, availability, stock thresholds) live on
/// <see cref="ProductBranch"/>, a deliberately separate aggregate.
///
/// Deliberately excluded from this aggregate: ProductBatch and
/// StockMovement (Inventory bounded context) — nesting them here would make
/// every product-name edit potentially touch a huge, ever-growing object
/// graph (Phase A §"Oversized aggregate risks").
/// </summary>
public class Product : AuditableEntity, ISoftDeletable, IHasRowVersion
{
    public string Name { get; private set; } = null!;
    public string? Description { get; private set; }
    public Guid CategoryId { get; private set; }
    public ProductStatus Status { get; private set; }
    public bool IsBatchTracked { get; private set; }

    /// <summary>
    /// Catalog-level reference value only (e.g. MSRP for new-branch
    /// onboarding defaults). NEVER read at sale time — SaleInvoiceItem
    /// always snapshots from ProductBranch.SellingPrice. See Architecture
    /// Review §2.
    /// </summary>
    public decimal? SuggestedRetailPrice { get; private set; }

    /// <summary>
    /// مدة الصلاحية المتوقَّعة بالأيام من تاريخ الشراء — قيمة مرجعية
    /// اختيارية لمنتجات إلها عمر افتراضي معروف (حليب، خبز، إلخ)، لا قيد
    /// إلزامي. الغرض العملي: تقدر تُستخدم لاحقًا لاقتراح تاريخ صلاحية
    /// دفعة جديدة تلقائيًا وقت الشراء (تاريخ الشراء + هذا الرقم)، بدل ما
    /// الكاشير/أمين المخزن يحسبها يدويًا كل مرة. الاقتراح فقط — تاريخ
    /// الصلاحية الفعلي لكل دفعة (ProductBatch.ExpiryDate) يضل قابلًا
    /// للتعديل يدويًا دائمًا، لأن دفعات فعلية ممكن تختلف عن المتوقَّع.
    /// null يعني "بلا مدة صلاحية معروفة" (منتجات غير قابلة للتلف مثلًا).
    /// </summary>
    public int? ExpectedShelfLifeDays { get; private set; }

    /// <summary>
    /// هل يُسمح تسجيل هذا المنتج ضمن "ضيافة/استهلاك داخلي"؟ افتراضيًا
    /// false — منتج ما حد يقرر صراحة إنه مسموح للضيافة (زي لحمة مجمّدة
    /// بكرتونة كاملة) ما لازم يظهر أصلًا بخيار كاشير أو موظف بلا قرار
    /// إداري واعٍ. RecordComplimentaryIssueHandler بيحرس هذا الشرط.
    /// </summary>
    public bool IsComplimentaryAllowed { get; private set; }

    public bool IsDeleted { get; private set; }
    public byte[]? RowVersion { get; private set; }

    private readonly List<ProductUnit> _units = new();
    public IReadOnlyCollection<ProductUnit> Units => _units.AsReadOnly();

    private readonly List<ProductBarcode> _barcodes = new();
    public IReadOnlyCollection<ProductBarcode> Barcodes => _barcodes.AsReadOnly();

    private readonly List<ProductImage> _images = new();
    public IReadOnlyCollection<ProductImage> Images => _images.AsReadOnly();

    private readonly List<ProductNote> _notes = new();
    public IReadOnlyCollection<ProductNote> Notes => _notes.AsReadOnly();

    private Product() { } // EF Core

    public Product(string name, Guid categoryId, bool isBatchTracked, decimal? suggestedRetailPrice = null, int? expectedShelfLifeDays = null)
    {
        Name = name;
        CategoryId = categoryId;
        IsBatchTracked = isBatchTracked;
        SuggestedRetailPrice = suggestedRetailPrice;
        ExpectedShelfLifeDays = expectedShelfLifeDays;
        Status = ProductStatus.PendingApproval;
    }

    public void Rename(string name) => Name = name;
    public void SetDescription(string? description) => Description = description;
    public void ChangeCategory(Guid categoryId) => CategoryId = categoryId;
    public void ChangeStatus(ProductStatus status) => Status = status;
    public void SetExpectedShelfLifeDays(int? days) => ExpectedShelfLifeDays = days;
    public void SetSuggestedRetailPrice(decimal? price) => SuggestedRetailPrice = price;
    public void SetComplimentaryAllowed(bool allowed) => IsComplimentaryAllowed = allowed;
    public void MarkDeleted() => IsDeleted = true;
    public void Restore() => IsDeleted = false;

    public ProductUnit AddUnit(string unitName, decimal conversionFactorToBase, bool isBaseUnit)
    {
        var unit = new ProductUnit(Id, unitName, conversionFactorToBase, isBaseUnit);
        _units.Add(unit);
        return unit;
    }

    public ProductBarcode AddBarcode(string barcodeValue, Guid productUnitId)
    {
        var barcode = new ProductBarcode(Id, barcodeValue, productUnitId);
        _barcodes.Add(barcode);
        return barcode;
    }

    public ProductImage AddImage(string url, bool isPrimary, int sortOrder)
    {
        var image = new ProductImage(Id, url, isPrimary, sortOrder);
        _images.Add(image);
        return image;
    }

    public ProductNote AddNote(string text)
    {
        var note = new ProductNote(Id, text);
        _notes.Add(note);
        return note;
    }
}

/// <summary>
/// Child of the Product aggregate but keeps its own stable Id, because
/// StockMovement/SaleInvoiceItem/PurchaseInvoiceItem reference a specific
/// unit by id (Architecture Review §12 "Inventory Strategy" — quantities are
/// normalized to the base unit via ConversionFactorToBase).
/// </summary>
public class ProductUnit : Entity
{
    public Guid ProductId { get; private set; }
    public string UnitName { get; private set; } = null!;
    public decimal ConversionFactorToBase { get; private set; }
    public bool IsBaseUnit { get; private set; }

    private ProductUnit() { } // EF Core

    internal ProductUnit(Guid productId, string unitName, decimal conversionFactorToBase, bool isBaseUnit)
    {
        ProductId = productId;
        UnitName = unitName;
        ConversionFactorToBase = conversionFactorToBase;
        IsBaseUnit = isBaseUnit;
    }
}

/// <summary>
/// Child of the Product aggregate. Each barcode corresponds to a specific
/// sellable unit of the product (e.g. a case barcode vs. a single-piece
/// barcode) — required ProductUnitId, not optional, since scanning it must
/// resolve to an unambiguous unit/quantity at POS.
/// </summary>
public class ProductBarcode : Entity
{
    public Guid ProductId { get; private set; }
    public string BarcodeValue { get; private set; } = null!;
    public Guid ProductUnitId { get; private set; }

    private ProductBarcode() { } // EF Core

    internal ProductBarcode(Guid productId, string barcodeValue, Guid productUnitId)
    {
        ProductId = productId;
        BarcodeValue = barcodeValue;
        ProductUnitId = productUnitId;
    }
}

public class ProductImage : Entity
{
    public Guid ProductId { get; private set; }
    public string Url { get; private set; } = null!;
    public bool IsPrimary { get; private set; }
    public int SortOrder { get; private set; }

    private ProductImage() { } // EF Core

    internal ProductImage(Guid productId, string url, bool isPrimary, int sortOrder)
    {
        ProductId = productId;
        Url = url;
        IsPrimary = isPrimary;
        SortOrder = sortOrder;
    }
}

public class ProductNote : Entity
{
    public Guid ProductId { get; private set; }
    public string Text { get; private set; } = null!;

    private ProductNote() { } // EF Core

    internal ProductNote(Guid productId, string text)
    {
        ProductId = productId;
        Text = text;
    }
}
