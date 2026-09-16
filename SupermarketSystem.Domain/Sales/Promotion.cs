using SupermarketSystem.Domain.Common;

namespace SupermarketSystem.Domain.Sales;

/// <summary>
/// عرض كمية (Bundle) لمنتج معيّن - "N بسعر كذا" (مثلًا 3 قطع بدينار).
/// Product-scoped، مش Branch-owned بحد ذاته - التفعيل الفعلي بفرع معيّن
/// يصير عبر <see cref="PromotionBranch"/> (صف منفصل لكل فرع، بعلامة
/// تفعيل خاصة فيه) - نفس فلسفة Product/ProductBranch بالضبط: تعريف
/// العرض عالمي (اسم، كمية، سعر، فترة صلاحية)، ربطه بفرع خطوة صريحة
/// منفصلة، عشان "ما حدَّدت فرع = بينطبق عالفروع كلها" يصير فعل واضح
/// (إنشاء صف لكل فرع) لا افتراض ضمني، وعشان توقيف العرض بفرع واحد بلا
/// التاني يصير ممكن (تعطيل صف PromotionBranch الواحد بلا حذفه).
///
/// السعر الفعلي المطبَّق وقت البيع يُحفظ Snapshot كامل
/// (SaleInvoiceItem.PromotionAmount + PromotionTitleSnapshot) - حذف أو
/// تعديل أو انتهاء هذا العرض لاحقًا **ما يغيّر أي فاتورة قديمة إطلاقًا**
/// (نفس مبدأ Discount الموثَّق أصلًا بـSalesConfigurations - traceability
/// FK فقط، SetNull on delete).
/// </summary>
public class Promotion : AuditableEntity, IHasRowVersion
{
    public Guid ProductId { get; private set; }
    public string Title { get; private set; } = null!;

    /// <summary>N بـ"N بسعر كذا" - يُطبَّق حصرًا على الوحدة الأساسية للمنتج (راجع تعليق CompleteSaleCommand PROMOTION ASSUMPTION).</summary>
    public int BundleQuantity { get; private set; }

    /// <summary>السعر الإجمالي لشراء BundleQuantity حبة دفعة وحدة.</summary>
    public decimal BundlePrice { get; private set; }

    /// <summary>أقصى كمية بسعر العرض بنفس الفاتورة - null يعني بلا حد. أي كمية زيادة تُسعَّر عاديًا تلقائيًا.</summary>
    public decimal? MaxQuantityPerInvoice { get; private set; }

    public DateTime StartAtUtc { get; private set; }
    public DateTime EndAtUtc { get; private set; }
    public byte[]? RowVersion { get; private set; }

    private readonly List<PromotionBranch> _branches = new();
    public IReadOnlyCollection<PromotionBranch> Branches => _branches.AsReadOnly();

    private Promotion() { } // EF Core

    public Promotion(
        Guid productId, string title, int bundleQuantity, decimal bundlePrice,
        decimal? maxQuantityPerInvoice, DateTime startAtUtc, DateTime endAtUtc)
    {
        ValidateDetails(title, bundleQuantity, bundlePrice, maxQuantityPerInvoice, startAtUtc, endAtUtc);

        ProductId = productId;
        Title = title.Trim();
        BundleQuantity = bundleQuantity;
        BundlePrice = bundlePrice;
        MaxQuantityPerInvoice = maxQuantityPerInvoice;
        StartAtUtc = startAtUtc;
        EndAtUtc = endAtUtc;
    }

    public void UpdateDetails(
        string title, int bundleQuantity, decimal bundlePrice,
        decimal? maxQuantityPerInvoice, DateTime startAtUtc, DateTime endAtUtc)
    {
        ValidateDetails(title, bundleQuantity, bundlePrice, maxQuantityPerInvoice, startAtUtc, endAtUtc);

        Title = title.Trim();
        BundleQuantity = bundleQuantity;
        BundlePrice = bundlePrice;
        MaxQuantityPerInvoice = maxQuantityPerInvoice;
        StartAtUtc = startAtUtc;
        EndAtUtc = endAtUtc;
    }

    /// <summary>لو الفرع عنده صف أصلًا (حتى لو مُعطَّل)، ما يُكرَّر - استخدم PromotionBranch.Activate بدل هيك.</summary>
    public PromotionBranch AddBranch(Guid branchId)
    {
        if (_branches.Any(b => b.BranchId == branchId))
        {
            throw new DomainException("This promotion is already linked to this branch.");
        }

        var branch = new PromotionBranch(Id, branchId);
        _branches.Add(branch);
        return branch;
    }

    private static void ValidateDetails(
        string title, int bundleQuantity, decimal bundlePrice,
        decimal? maxQuantityPerInvoice, DateTime startAtUtc, DateTime endAtUtc)
    {
        if (string.IsNullOrWhiteSpace(title))
        {
            throw new DomainException("Promotion title is required.");
        }

        if (bundleQuantity <= 0)
        {
            throw new DomainException("Bundle quantity must be positive.");
        }

        if (bundlePrice < 0)
        {
            throw new DomainException("Bundle price cannot be negative.");
        }

        if (maxQuantityPerInvoice is <= 0)
        {
            throw new DomainException("Max quantity per invoice, if set, must be positive.");
        }

        if (endAtUtc <= startAtUtc)
        {
            throw new DomainException("Promotion end date must be after its start date.");
        }
    }
}

/// <summary>
/// Child of the Promotion aggregate. وجود هذا الصف هو ما "يشغّل" العرض
/// فعليًا بهذا الفرع بالذات - نفس فلسفة ProductBranch تمامًا: عرض بلا
/// صف PromotionBranch لأي فرع، ما بينطبق على أي بيع أبدًا. IsActive
/// مستقل لكل فرع - توقيف العرض بفرع واحد بلا حذف الصف، جاهز يترجّع
/// بضغطة وحدة بلا إعادة إدخال بيانات.
/// </summary>
public class PromotionBranch : Entity, IBranchOwned
{
    public Guid PromotionId { get; private set; }
    public Guid BranchId { get; private set; }
    public bool IsActive { get; private set; }

    private PromotionBranch() { } // EF Core

    internal PromotionBranch(Guid promotionId, Guid branchId)
    {
        PromotionId = promotionId;
        BranchId = branchId;
        IsActive = true;
    }

    public void Activate() => IsActive = true;
    public void Deactivate() => IsActive = false;
}
