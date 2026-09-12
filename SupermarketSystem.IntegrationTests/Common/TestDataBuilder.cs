using System.Globalization;
using System.Security.Claims;
using Microsoft.AspNetCore.Http;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using SupermarketSystem.Application.Common.Interfaces;
using SupermarketSystem.Domain.Branches;
using SupermarketSystem.Domain.Catalog;
using SupermarketSystem.Domain.Common;
using SupermarketSystem.Domain.Customers;
using SupermarketSystem.Domain.Inventory;
using SupermarketSystem.Domain.Purchasing;
using SupermarketSystem.Domain.Settings;
using SupermarketSystem.Infrastructure.Persistence;
using SupermarketSystem.Infrastructure.Persistence.Configurations;
using SupermarketSystem.Infrastructure.Services;
using SupermarketSystem.IntegrationTests;

namespace SupermarketSystem.IntegrationTests.Common;

/// <summary>
/// دوال مساعدة مشتركة لبذر بيانات اختبار (منتج + وحدة أساسية + سعر بفرع،
/// إلخ) — تُستخدم مباشرة عبر AppDbContext (لا Handler)، عشان تحضير حالة
/// البيانات *قبل* استدعاء الـHandler قيد الاختبار يضل بسيطًا وواضحًا،
/// بلا تكرار نفس منطق الإنشاء بكل ملف اختبار. هذا ملف مساعد إضافي جديد،
/// لا تعديل على أي من ملفات الـfixture المشتركة الممنوع لمسها.
/// </summary>
public static class TestDataBuilder
{
    /// <summary>معرّفات طرق الدفع الثابتة المبذورة عبر Migration HasData (راجع PaymentMethodConfiguration) — لا حاجة نستعلم عنها، ولا ننشئها.</summary>
    public static Guid CashPaymentMethodId => PaymentMethodConfiguration.CashId;
    public static Guid VisaPaymentMethodId => PaymentMethodConfiguration.VisaId;
    public static Guid CliqPaymentMethodId => PaymentMethodConfiguration.CliqId;

    public static async Task<ProductCategory> CreateCategoryAsync(AppDbContext db, string name = "تصنيف اختبار")
    {
        var category = new ProductCategory(name);
        db.ProductCategories.Add(category);
        await db.SaveChangesAsync();
        return category;
    }

    /// <summary>
    /// منتج فعّال (Active) بوحدة أساسية واحدة (معامل تحويل = 1) — الحالة
    /// الافتراضية عند الإنشاء صارت Active مباشرة (كانت PendingApproval
    /// بلا أي ترقية، فجوة حقيقية انصلحت - راجع تعليق PublicCatalogTests.cs).
    /// ChangeStatus هون no-op الآن، بقي توثيقًا للنية.
    /// </summary>
    public static async Task<(Product Product, ProductUnit BaseUnit)> CreateActiveProductAsync(
        AppDbContext db,
        string name = "منتج اختبار",
        bool isBatchTracked = false,
        Guid? categoryId = null,
        bool isComplimentaryAllowed = false)
    {
        var resolvedCategoryId = categoryId ?? (await CreateCategoryAsync(db)).Id;

        var product = new Product(name, resolvedCategoryId, isBatchTracked);
        product.ChangeStatus(ProductStatus.Active);
        if (isComplimentaryAllowed)
        {
            product.SetComplimentaryAllowed(true);
        }

        var baseUnit = product.AddUnit("حبة", conversionFactorToBase: 1m, isBaseUnit: true);

        db.Products.Add(product);
        await db.SaveChangesAsync();

        return (product, baseUnit);
    }

    public static async Task<ProductBranch> CreateProductBranchAsync(
        AppDbContext db, Guid productId, Guid branchId, decimal sellingPrice, bool isAvailableForSale = true)
    {
        var productBranch = new ProductBranch(productId, branchId, sellingPrice);
        if (!isAvailableForSale)
        {
            productBranch.MakeUnavailable();
        }

        db.ProductBranches.Add(productBranch);
        await db.SaveChangesAsync();
        return productBranch;
    }

    /// <summary>يضبط رصيد المخزون الحالي لمنتج بفرع معيّن (إنشاء صف Stock مباشرة — لا عبر حركة بيع/شراء، هذا تحضير حالة أولية فقط).</summary>
    public static async Task<Stock> SetStockAsync(
        AppDbContext db, Guid productId, Guid branchId, decimal quantityOnHand, Guid? productBatchId = null)
    {
        var stock = new Stock(productId, branchId, productBatchId);
        if (quantityOnHand > 0)
        {
            stock.Increase(quantityOnHand);
        }

        db.Stocks.Add(stock);
        await db.SaveChangesAsync();
        return stock;
    }

    public static async Task<ProductBatch> CreateBatchAsync(
        AppDbContext db, Guid productId, Guid branchId, string batchNumber, decimal unitCost = 1m, DateOnly? expiryDate = null)
    {
        var batch = new ProductBatch(productId, branchId, batchNumber, expiryDate, unitCost);
        db.ProductBatches.Add(batch);
        await db.SaveChangesAsync();
        return batch;
    }

    /// <summary>
    /// Branches مستثناة من تصفير Respawn بين الاختبارات (TablesToIgnore
    /// بـDatabaseFixture) - قصدًا، بس هذا معناه فروع الاختبار الإضافية
    /// اللي هالملف بينشئها (فروع وجهة لنقل المخزون مثلًا) بتضل موجودة
    /// عبر تشغيلات dotnet test منفصلة متعددة على نفس القاعدة، فكود فرع
    /// مكرَّر بين تشغيلتين بيصطدم بـunique index. الحل: لو الكود موجود
    /// أصلًا (تشغيلة سابقة)، نعيد استخدام نفس الفرع بدل إنشاء واحد جديد -
    /// آمن لأن كل البيانات التجارية المرتبطة فيه (Stock/StockTransfer/إلخ)
    /// اتصفّرت أصلًا بـResetDatabaseAsync قبل هذا الاختبار بالذات.
    /// </summary>
    public static async Task<Branch> CreateBranchAsync(AppDbContext db, string name, string code)
    {
        var existing = await db.Branches.IgnoreQueryFilters().FirstOrDefaultAsync(b => b.Code == code);
        if (existing is not null)
        {
            return existing;
        }

        var branch = new Branch(name, code, address: null, phoneNumber: null);
        db.Branches.Add(branch);
        await db.SaveChangesAsync();
        return branch;
    }

    public static async Task<Customer> CreateCustomerAsync(AppDbContext db, string fullName = "زبون اختبار", string? phone = "0790000000")
    {
        var customer = new Customer(fullName, phone, email: null);
        db.Customers.Add(customer);
        await db.SaveChangesAsync();
        return customer;
    }

    public static async Task<Supplier> CreateSupplierAsync(AppDbContext db, string name = "مورد اختبار")
    {
        var supplier = new Supplier(name, contactName: null, phone: null, email: null, address: null);
        db.Suppliers.Add(supplier);
        await db.SaveChangesAsync();
        return supplier;
    }

    /// <summary>
    /// يكتب/يحدّث إعداد نظام مباشرة بقاعدة البيانات، ويُبطل الكاش فورًا
    /// (CachedSettingsProvider بيخزّن IMemoryCache Singleton يعيش طول
    /// تشغيلة الاختبارات كاملة عبر DatabaseFixture المشتركة - بدون هذا
    /// الإبطال، اختبار لاحق ممكن يقرأ قيمة قديمة مخزَّنة من اختبار سابق
    /// لنفس المفتاح، حتى لو انصفّرت قاعدة البيانات بين الاختبارين).
    /// </summary>
    public static async Task SetSettingAsync(IServiceScope scope, string key, string value)
    {
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();

        var existing = await db.Set<SystemSetting>().FirstOrDefaultAsync(s => s.Key == key);
        if (existing is null)
        {
            db.Set<SystemSetting>().Add(new SystemSetting(key, value, description: null));
        }
        else
        {
            existing.UpdateValue(value);
        }

        await db.SaveChangesAsync();

        scope.ServiceProvider.GetRequiredService<ISettingsProvider>().Invalidate(key);
    }

    public static Task SetSettingAsync(IServiceScope scope, string key, decimal value) =>
        SetSettingAsync(scope, key, value.ToString(CultureInfo.InvariantCulture));

    public static Task SetSettingAsync(IServiceScope scope, string key, bool value) =>
        SetSettingAsync(scope, key, value ? "true" : "false");

    /// <summary>
    /// اكتشاف مهم أثناء بناء هذي الاختبارات، اذكره بالتقرير النهائي:
    /// AppDbContext بيطبّق فلتر استعلام عالمي (Global Query Filter) على كل
    /// كيان IBranchOwned (ProductBranch/Stock/SaleInvoice/PurchaseInvoice/
    /// StockMovement/CashDrawerLog/إلخ - راجع AppDbContext.cs السطر ~177):
    /// `_isCrossBranchAccessAllowed || e.BranchId == _currentBranchId`، والقيمتان
    /// دايمًا تُقرآن من ICurrentUserContext وقت إنشاء AppDbContext (مرة
    /// وحدة لكل Scope). RealCurrentUserContext (التنفيذ الفعلي) بيعتمد
    /// حصريًا على IHttpContextAccessor.HttpContext - يعني استدعاء Handler
    /// مباشرة من Scope عادي (بلا HTTP request حقيقي) بيخلي BranchId=null
    /// وCrossBranchAccess=false دائمًا، فكل استعلام لأي كيان IBranchOwned
    /// (حتى لو انبذر بنفس القاعدة لحظات قبل) بيرجع فاضي تمامًا - فشل صامت
    /// مضلِّل (مثلًا Sale.ProductNotPricedAtBranch رغم وجود الصف فعليًا).
    ///
    /// الحل هون: نبني HttpContext مصطنع بنفس الـclaims اللي JwtTokenService
    /// الحقيقي بيحطها لمستخدم Master Admin فعليًا (NameIdentifier + branch_id
    /// + cross_branch=true)، ونعيّنه على IHttpContextAccessor.HttpContext
    /// **قبل** أي resolve لـAppDbContext أو أي Handler بنفس الـScope - عشان
    /// RealCurrentUserContext (يلتقط _httpContext بالـconstructor مرة وحدة،
    /// لقطة لا قراءة حية) يشوفه صحيح. لا لمس لأي ملف fixture مشترك ولا
    /// لـCustomWebApplicationFactory - هذا استخدام DI عادي بالكامل داخل
    /// نطاق الاختبار نفسه.
    ///
    /// اكتشاف ثانٍ مرتبط: DatabaseFixture.SeedFixedDataAsync بينشئ فرع
    /// الاختبار بمنشئ Branch العام مباشرة (`new Branch(...)`)، لا عبر
    /// CreateBranchHandler الحقيقي - وهذا الأخير هو اللي بيبذر صفوف
    /// BranchDocumentSequence (سطر واحد لكل DocumentType) بنفس المعاملة
    /// (راجع تعليقه: "A branch created without sequences would look
    /// completely fine until the first sale... failed"). يعني فرع
    /// الاختبار المشترك بلا أي صف BranchDocumentSequence إطلاقًا، وأي
    /// Handler بيستخدم IDocumentNumberGenerator (بيع، إرجاع، شراء، جرد،
    /// نقل مخزون) بيفشل باستثناء غير متوقَّع لو استُدعي مباشرة من Scope.
    /// جدول BranchDocumentSequences نفسه مش مستثنى من تصفير Respawn (لا
    /// يظهر بـTablesToIgnore)، فلازم يُعاد بذره بكل اختبار من جديد - هون
    /// بالضبط المكان الصحيح (يُستدعى أول شي بكل اختبار مباشر Handler-level).
    /// </summary>
    public static async Task ActAsAdminAsync(IServiceScope scope, DatabaseFixture fixture)
    {
        var accessor = scope.ServiceProvider.GetRequiredService<IHttpContextAccessor>();

        var identity = new ClaimsIdentity(new[]
        {
            new Claim(ClaimTypes.NameIdentifier, fixture.AdminUserId.ToString()),
            new Claim(JwtTokenService.BranchIdClaim, fixture.TestBranchId.ToString()),
            new Claim(JwtTokenService.CrossBranchClaim, "true")
        }, authenticationType: "TestFakeAuth");

        accessor.HttpContext = new DefaultHttpContext { User = new ClaimsPrincipal(identity) };

        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        await EnsureDocumentSequencesProvisionedAsync(db, fixture.TestBranchId);
    }

    /// <summary>
    /// نفس تزويد BranchDocumentSequence أعلاه، بس لوحده - مطلوب حتى
    /// باختبارات HTTP الحقيقية (Endpoint tests) اللي ما بتحتاج ActAsAdminAsync
    /// (التوكن الحقيقي عبر CreateAuthenticatedClientAsync كافٍ لفلتر الفرع)،
    /// لأنه فرع الاختبار المشترك نفسه بلا صفوف تسلسل مستندات (راجع تعليق
    /// ActAsAdminAsync) - أي endpoint بيولّد رقم مستند (بيع/شراء/جرد/نقل
    /// مخزون) هيفشل بـ500 بلا هذا التزويد، حتى لو الطلب عبر HTTP حقيقي
    /// بتوكن صحيح.
    /// </summary>
    public static async Task EnsureDocumentSequencesProvisionedAsync(AppDbContext db, Guid branchId)
    {
        var alreadyProvisioned = await db.Set<BranchDocumentSequence>()
            .IgnoreQueryFilters()
            .AnyAsync(s => s.BranchId == branchId);

        if (alreadyProvisioned)
        {
            return;
        }

        foreach (var documentType in Enum.GetValues<DocumentType>())
        {
            db.Set<BranchDocumentSequence>().Add(new BranchDocumentSequence(branchId, documentType));
        }

        await db.SaveChangesAsync();
    }
}
