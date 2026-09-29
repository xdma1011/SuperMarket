namespace SupermarketSystem.CashierApp.Local;

public sealed class LocalProduct
{
    public Guid ProductId { get; set; }
    public string Name { get; set; } = string.Empty;
    public Guid CategoryId { get; set; }
    public string CategoryName { get; set; } = string.Empty;
    public decimal SellingPrice { get; set; }
    public bool IsAvailableForSale { get; set; }
    public bool IsBatchTracked { get; set; }
}

public sealed class LocalProductUnit
{
    public Guid UnitId { get; set; }
    public Guid ProductId { get; set; }
    public string UnitName { get; set; } = string.Empty;
    public decimal ConversionFactorToBase { get; set; }
    public bool IsBaseUnit { get; set; }
}

public sealed class LocalProductBarcode
{
    public int Id { get; set; }
    public string BarcodeValue { get; set; } = string.Empty;
    public Guid ProductUnitId { get; set; }
}

/// <summary>
/// بيع محلي بانتظار الإرسال للسيرفر — يُنشأ فورًا عند إتمام البيع
/// بالكاشير، ويُحذف بس بعد نجاح الإرسال المؤكَّد. ClientRequestId هو
/// نفسه اللي بينبعت بجسم الطلب الفعلي.
/// </summary>
public sealed class PendingSale
{
    public int Id { get; set; }
    public Guid ClientRequestId { get; set; }
    public Guid BranchId { get; set; }
    public string RequestPayloadJson { get; set; } = string.Empty;
    public DateTime CreatedAtLocal { get; set; }
    public int AttemptCount { get; set; }
    public DateTime? LastAttemptAtLocal { get; set; }
    public string? LastErrorMessage { get; set; }
}

/// <summary>دفعة متوفرة فعليًا (كميتها > 0) لصنف "يتتبّع دفعات" بهذا الفرع.</summary>
public sealed class LocalProductBatch
{
    public Guid BatchId { get; set; }
    public Guid ProductId { get; set; }
    public string BatchNumber { get; set; } = string.Empty;
    public DateOnly? ExpiryDate { get; set; }
    public decimal QuantityAvailable { get; set; }
}

/// <summary>نادرًا ما تتغيّر - تُخزَّن محليًا لتشتغل شاشة البيع بلا اتصال حتى بأول تشغيل بعد مزامنة واحدة ناجحة.</summary>
public sealed class LocalPaymentMethod
{
    public Guid Id { get; set; }
    public string Name { get; set; } = string.Empty;
    public bool RequiresExternalReference { get; set; }
}

/// <summary>
/// عرض كمية ("N بسعر كذا") لصنف بهالفرع (29/9/2026) - الشغّال والمجدول، بتواريخه. شاشة البيع بتختار الشغّال
/// لحظة إضافة الصنف حسب ساعة الجهاز (بدء/انتهاء العرض ما بيغيّر رقم نسخة الكتالوج فما في مزامنة وقتها).
/// </summary>
public sealed class LocalPromotion
{
    public Guid PromotionId { get; set; }
    public Guid ProductId { get; set; }
    public string Title { get; set; } = string.Empty;
    public int BundleQuantity { get; set; }
    public decimal BundlePrice { get; set; }
    public decimal? MaxQuantityPerInvoice { get; set; }
    public DateTime StartAtUtc { get; set; }
    public DateTime EndAtUtc { get; set; }
    public DateTime CreatedAtUtc { get; set; }
}

/// <summary>صف واحد بس - آخر رقم نسخة كتالوج مسحوب فعليًا محليًا.</summary>
public sealed class SyncState
{
    public int Id { get; set; }
    public long LastSyncedCatalogVersion { get; set; }
    public DateTime? LastSuccessfulSyncAtLocal { get; set; }

    /// <summary>آخر تحديث لطرق الدفع من السيرفر (29/9/2026) - التحديث التلقائي مرة كل 24 ساعة (PaymentMethodStore).</summary>
    public DateTime? LastPaymentMethodsRefreshAtUtc { get; set; }
}
