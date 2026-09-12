using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using SupermarketSystem.Application.Catalog.CreateProduct;
using SupermarketSystem.Application.Catalog.CreateProductCategory;
using SupermarketSystem.Application.Catalog.GetPublicCatalog;
using SupermarketSystem.Application.Catalog.GetPublicCatalogCategories;
using SupermarketSystem.Application.Catalog.SetProductBranchAvailability;
using SupermarketSystem.Application.Common.Pagination;
using SupermarketSystem.Domain.Catalog;
using SupermarketSystem.Domain.Inventory;
using Xunit;

namespace SupermarketSystem.IntegrationTests.Catalog;

/// <summary>
/// كتالوج تصفّح عام لتطبيق الزبائن - بلا مصادقة. أهم قاعدة عمل هون: إخفاء
/// المخزون القليل تلقائيًا حسب فئة السعر (الحدود الافتراضية بلا إعدادات
/// مخصصة: أعلى من 1.0 يحتاج مخزون >=7، بين 0.5 و1.0 يحتاج >=5، أقل من
/// 0.5 يحتاج >=30 - راجع GetPublicCatalogHandler).
///
/// ⚠️ فجوة حقيقية اكتُشفت أثناء بناء هالاختبارات (راجع تفاصيلها بالتقرير
/// النهائي - لم تُصلَح هون عمدًا، تحتاج قرار صاحب المشروع): Product الجديد
/// يبلش دائمًا بحالة ProductStatus.PendingApproval (راجع Product.cs
/// constructor)، وGetPublicCatalog/GetPublicCatalogCategories/CompleteSale
/// كلها بتشترط صراحة Status == Active - بس ولا مكان بكل الـApplication
/// layer بيستدعي product.ChangeStatus(Active) أبدًا. يعني عمليًا: أي منتج
/// جديد يتسجّل بالنظام الحقيقي يضل PendingApproval للأبد - ما بينباع
/// (CompleteSaleCommand بيرفضه)، وما بيظهر بكتالوج الزبائن العام. الاختبارات
/// هون بتفعّل المنتج يدويًا مباشرة (ActivateProductForTestAsync) لمحاكاة
/// أي خطوة "اعتماد" مفقودة حاليًا من الإنتاج، بلا لمس أي كود إنتاج -
/// القرار (هل نضيف خطوة اعتماد صريحة، أو نخلي الحالة الافتراضية Active
/// من الأساس) يرجع لصاحب المشروع.
/// </summary>
[Collection(DatabaseCollection.Name)]
public sealed class PublicCatalogTests : IntegrationTestBase
{
    public PublicCatalogTests(DatabaseFixture fixture) : base(fixture) { }

    private async Task<Guid> CreateProductWithBranchAsync(
        IServiceScope scope, Guid categoryId, string name, decimal price, decimal stockQuantity)
    {
        var productHandler = scope.ServiceProvider.GetRequiredService<CreateProductHandler>();
        var product = await productHandler.HandleAsync(
            new CreateProductCommand(
                name, null, categoryId, false, price, null,
                new[] { new CreateProductUnitDto("قطعة", 1m, true) },
                Array.Empty<CreateProductBarcodeDto>()),
            CancellationToken.None);
        Assert.True(product.IsSuccess);

        var db = CreateDbContext(scope);
        await ActivateProductForTestAsync(db, product.Value.ProductId);

        if (stockQuantity > 0)
        {
            var stock = new Stock(product.Value.ProductId, Fixture.TestBranchId, null);
            stock.Increase(stockQuantity);
            db.Stocks.Add(stock);
            await db.SaveChangesAsync(CancellationToken.None);
        }

        return product.Value.ProductId;
    }

    /// <summary>
    /// يفعّل منتجًا اختباريًا يدويًا - راجع تعليق الفجوة أعلى الملف. هذا
    /// تحايل اختباري بحت، لا حل للفجوة نفسها.
    /// </summary>
    private static async Task ActivateProductForTestAsync(SupermarketSystem.Infrastructure.Persistence.AppDbContext db, Guid productId)
    {
        var product = await db.Products.FirstAsync(p => p.Id == productId);
        product.ChangeStatus(ProductStatus.Active);
        await db.SaveChangesAsync(CancellationToken.None);
    }

    [Fact]
    public async Task منتج_بمخزون_كافٍ_لفئته_السعرية_يظهر_بالكتالوج()
    {
        using var scope = CreateScope();
        var categoryHandler = scope.ServiceProvider.GetRequiredService<CreateProductCategoryHandler>();
        var category = await categoryHandler.HandleAsync(new CreateProductCategoryCommand("تصنيف", null), CancellationToken.None);

        // سعر > 1.0 (الحد الافتراضي الأعلى) يحتاج مخزون >= 7 ليظهر.
        await CreateProductWithBranchAsync(scope, category.Value.CategoryId, "منتج ظاهر", price: 2m, stockQuantity: 10);

        var handler = scope.ServiceProvider.GetRequiredService<GetPublicCatalogHandler>();
        var result = await handler.HandleAsync(
            new GetPublicCatalogQuery(Fixture.TestBranchId, null, new PagedRequest()), CancellationToken.None);

        var item = Assert.Single(result.Items);
        Assert.Equal("منتج ظاهر", item.Name);
        Assert.Equal(2m, item.Price);
    }

    [Fact]
    public async Task منتج_بمخزون_أقل_من_الحد_الأدنى_لفئته_السعرية_يُخفى()
    {
        using var scope = CreateScope();
        var categoryHandler = scope.ServiceProvider.GetRequiredService<CreateProductCategoryHandler>();
        var category = await categoryHandler.HandleAsync(new CreateProductCategoryCommand("تصنيف", null), CancellationToken.None);

        // سعر > 1.0 يحتاج مخزون >= 7 - هون بس 3.
        await CreateProductWithBranchAsync(scope, category.Value.CategoryId, "منتج مخفي", price: 2m, stockQuantity: 3);

        var handler = scope.ServiceProvider.GetRequiredService<GetPublicCatalogHandler>();
        var result = await handler.HandleAsync(
            new GetPublicCatalogQuery(Fixture.TestBranchId, null, new PagedRequest()), CancellationToken.None);

        Assert.Empty(result.Items);
    }

    [Fact]
    public async Task منتج_موقوف_البيع_بالفرع_لا_يظهر_رغم_توفر_المخزون()
    {
        using var scope = CreateScope();
        // ProductBranch كيان Branch-owned - راجع تعليق TestAuthContext.
        scope.ActAsCrossBranchUser();
        var categoryHandler = scope.ServiceProvider.GetRequiredService<CreateProductCategoryHandler>();
        var category = await categoryHandler.HandleAsync(new CreateProductCategoryCommand("تصنيف", null), CancellationToken.None);

        var productId = await CreateProductWithBranchAsync(scope, category.Value.CategoryId, "منتج موقوف", price: 2m, stockQuantity: 20);

        var db = CreateDbContext(scope);
        // فروع كتير متراكمة من تشغيلات سابقة (Branches مستثناة من التصفير) -
        // فلترة بالفرع الصريح لا Single() عام.
        var productBranch = db.ProductBranches.Single(pb => pb.ProductId == productId && pb.BranchId == Fixture.TestBranchId);
        var availabilityHandler = scope.ServiceProvider.GetRequiredService<SetProductBranchAvailabilityHandler>();
        await availabilityHandler.HandleAsync(new SetProductBranchAvailabilityCommand(productBranch.Id, false), CancellationToken.None);

        var handler = scope.ServiceProvider.GetRequiredService<GetPublicCatalogHandler>();
        var result = await handler.HandleAsync(
            new GetPublicCatalogQuery(Fixture.TestBranchId, null, new PagedRequest()), CancellationToken.None);

        Assert.Empty(result.Items);
    }

    [Fact]
    public async Task التصنيفات_العامة_تُرجع_فقط_ما_فيه_منتج_متاح_للبيع()
    {
        using var scope = CreateScope();
        var categoryHandler = scope.ServiceProvider.GetRequiredService<CreateProductCategoryHandler>();
        var categoryWithProduct = await categoryHandler.HandleAsync(new CreateProductCategoryCommand("فيها منتج", null), CancellationToken.None);
        var emptyCategory = await categoryHandler.HandleAsync(new CreateProductCategoryCommand("فاضية", null), CancellationToken.None);

        await CreateProductWithBranchAsync(scope, categoryWithProduct.Value.CategoryId, "منتج", price: 2m, stockQuantity: 10);

        var handler = scope.ServiceProvider.GetRequiredService<GetPublicCatalogCategoriesHandler>();
        var result = await handler.HandleAsync(new GetPublicCatalogCategoriesQuery(Fixture.TestBranchId), CancellationToken.None);

        var item = Assert.Single(result);
        Assert.Equal("فيها منتج", item.Name);
        Assert.NotEqual(emptyCategory.Value.CategoryId, item.Id);
    }
}
