using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using SupermarketSystem.Application.Catalog.CreateProduct;
using SupermarketSystem.Application.Catalog.CreateProductBranch;
using SupermarketSystem.Application.Catalog.CreateProductCategory;

namespace SupermarketSystem.IntegrationTests.Ordering;

/// <summary>
/// يبني منتجًا جاهزًا للبيع بفرع الاختبار (تصنيف + منتج بوحدة أساسية +
/// سطر ProductBranches فعلي - راجع CLAUDE.md §1.8: بلا هذا السطر الأخير
/// المنتج غير مرئي كليًا لأي عملية طلب/بيع) - يخدم كل اختبارات Ordering
/// اللي محتاجة صنف قابل للطلب فعليًا.
/// </summary>
internal static class OrderingTestDataHelper
{
    public static async Task<(Guid ProductId, Guid ProductUnitId, decimal SellingPrice)> CreateSellableProductAsync(
        IServiceProvider services, Guid branchId, decimal sellingPrice = 2.5m)
    {
        var categoryHandler = services.GetRequiredService<CreateProductCategoryHandler>();
        var categoryResult = await categoryHandler.HandleAsync(
            new CreateProductCategoryCommand($"تصنيف-{Guid.NewGuid():N}", ParentCategoryId: null), CancellationToken.None);

        var productHandler = services.GetRequiredService<CreateProductHandler>();
        var productResult = await productHandler.HandleAsync(new CreateProductCommand(
            $"منتج-{Guid.NewGuid():N}", Description: null, categoryResult.Value.CategoryId,
            IsBatchTracked: false, SuggestedRetailPrice: null, ExpectedShelfLifeDays: null,
            Units: new[] { new CreateProductUnitDto("قطعة", 1m, true) },
            Barcodes: Array.Empty<CreateProductBarcodeDto>()), CancellationToken.None);

        var branchHandler = services.GetRequiredService<CreateProductBranchHandler>();
        var branchResult = await branchHandler.HandleAsync(new CreateProductBranchCommand(
            productResult.Value.ProductId, branchId, sellingPrice, MinimumStock: null, MaximumStock: null),
            CancellationToken.None);

        Xunit.Assert.True(branchResult.IsSuccess, branchResult.Error?.Message);

        using var scope = services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<SupermarketSystem.Infrastructure.Persistence.AppDbContext>();

        // ⚠️ فجوة إنتاج حقيقية مكتشَفة (خارج نطاق دومينات هذه المهمة -
        // Catalog/Sales - بس اكتشفناها هون لأننا احتجنا صنف قابل للبيع):
        // CreateProductHandler بينشئ كل منتج جديد بحالة ProductStatus.
        // PendingApproval (راجع Product.cs constructor)، وما في ولا Handler
        // واحد بكامل Application يستدعي Product.ChangeStatus لترقيته لـ
        // Active (تحقّقنا بـgrep شامل - صفر استدعاء). CompleteSaleHandler
        // (Sales/CompleteSale) يرفض بيع أي منتج Status != Active صراحة
        // ("Sale.ProductNotActive")، وGetPublicCatalogHandler بيخفي أي
        // منتج Status != Active كذلك. يعني عمليًا: أي منتج يُنشأ اليوم
        // عبر شاشة الكتالوج الحقيقية يضل PendingApproval للأبد وما ينباع
        // أبدًا عبر أي واجهة، إلا بتعديل يدوي مباشر بقاعدة البيانات
        // (SSMS) - فجوة من نفس عيار المذكورة بـCLAUDE.md §1.8 (منتج غير
        // مربوط بفرع)، بس أخطر لأنها تمنع البيع كليًا لا الظهور بالكاشير
        // فقط. لازم يُبلَّغ صاحب المشروع فورًا (تفاصيل كاملة بتقرير الجلسة).
        // هون نفعّله يدويًا مباشرة بقاعدة بيانات الاختبار (بلا المرور بأي
        // Handler إنتاجي - لأنه ما يوجد Handler كهذا أصلًا) فقط لغرض بناء
        // بيانات اختبار Ordering صالحة، لا كإصلاح للفجوة الحقيقية.
        var product = await db.Products.FirstAsync(p => p.Id == productResult.Value.ProductId);
        product.ChangeStatus(SupermarketSystem.Domain.Catalog.ProductStatus.Active);
        await db.SaveChangesAsync();

        // جِب معرّف الوحدة الأساسية فعليًا (Product.AddUnit ما بيرجّع معرّفها بالـresponse).
        var unit = await db.ProductUnits
            .Where(u => u.ProductId == productResult.Value.ProductId && u.IsBaseUnit)
            .Select(u => u.Id)
            .FirstAsync();

        return (productResult.Value.ProductId, unit, sellingPrice);
    }
}
