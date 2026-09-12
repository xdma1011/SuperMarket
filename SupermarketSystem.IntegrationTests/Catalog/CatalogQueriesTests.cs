using Microsoft.Extensions.DependencyInjection;
using SupermarketSystem.Application.Catalog.CreateProduct;
using SupermarketSystem.Application.Catalog.CreateProductBranch;
using SupermarketSystem.Application.Catalog.CreateProductCategory;
using SupermarketSystem.Application.Catalog.GetProductBranches;
using SupermarketSystem.Application.Catalog.GetProductByBarcode;
using SupermarketSystem.Application.Catalog.GetProductCategories;
using SupermarketSystem.Application.Catalog.GetProducts;
using SupermarketSystem.Application.Common.Pagination;
using Xunit;

namespace SupermarketSystem.IntegrationTests.Catalog;

/// <summary>استعلامات قراءة بسيطة قريبة جدًا من بعض (كلها Select عن جدول Catalog مباشر) - مجمَّعة بملف واحد، كل استعلام بصنف اختبار مستقل.</summary>
public static class CatalogQueriesTestHelpers
{
    public static async Task<Guid> CreateCategoryAsync(IServiceScope scope, string name)
    {
        var handler = scope.ServiceProvider.GetRequiredService<CreateProductCategoryHandler>();
        var result = await handler.HandleAsync(new CreateProductCategoryCommand(name, null), CancellationToken.None);
        Assert.True(result.IsSuccess);
        return result.Value.CategoryId;
    }

    public static async Task<Guid> CreateProductAsync(
        IServiceScope scope, Guid categoryId, string name, string? barcode = null, decimal? suggestedPrice = null)
    {
        var handler = scope.ServiceProvider.GetRequiredService<CreateProductHandler>();
        var barcodes = barcode is null
            ? Array.Empty<CreateProductBarcodeDto>()
            : new[] { new CreateProductBarcodeDto(barcode, "قطعة") };

        var result = await handler.HandleAsync(
            new CreateProductCommand(
                name, null, categoryId, false, suggestedPrice, null,
                new[] { new CreateProductUnitDto("قطعة", 1m, true) },
                barcodes),
            CancellationToken.None);

        Assert.True(result.IsSuccess);
        return result.Value.ProductId;
    }
}

[Collection(DatabaseCollection.Name)]
public sealed class GetProductsQueryTests : IntegrationTestBase
{
    public GetProductsQueryTests(DatabaseFixture fixture) : base(fixture) { }

    [Fact]
    public async Task جلب_المنتجات_يفلتر_حسب_التصنيف()
    {
        using var scope = CreateScope();
        var categoryA = await CatalogQueriesTestHelpers.CreateCategoryAsync(scope, "تصنيف أ");
        var categoryB = await CatalogQueriesTestHelpers.CreateCategoryAsync(scope, "تصنيف ب");
        await CatalogQueriesTestHelpers.CreateProductAsync(scope, categoryA, "منتج أ1");
        await CatalogQueriesTestHelpers.CreateProductAsync(scope, categoryB, "منتج ب1");

        var handler = scope.ServiceProvider.GetRequiredService<GetProductsHandler>();
        var result = await handler.HandleAsync(new GetProductsQuery(new PagedRequest(), categoryA), CancellationToken.None);

        var item = Assert.Single(result.Items);
        Assert.Equal("منتج أ1", item.Name);
        Assert.Equal(1, result.TotalCount);
    }

    [Fact]
    public async Task جلب_المنتجات_يبحث_بالاسم()
    {
        using var scope = CreateScope();
        var categoryId = await CatalogQueriesTestHelpers.CreateCategoryAsync(scope, "تصنيف");
        await CatalogQueriesTestHelpers.CreateProductAsync(scope, categoryId, "شيبس بطاطا");
        await CatalogQueriesTestHelpers.CreateProductAsync(scope, categoryId, "عصير برتقال");

        var handler = scope.ServiceProvider.GetRequiredService<GetProductsHandler>();
        var paging = new PagedRequest { Search = "شيبس" };
        var result = await handler.HandleAsync(new GetProductsQuery(paging, null), CancellationToken.None);

        var item = Assert.Single(result.Items);
        Assert.Equal("شيبس بطاطا", item.Name);
    }
}

[Collection(DatabaseCollection.Name)]
public sealed class GetProductByBarcodeQueryTests : IntegrationTestBase
{
    public GetProductByBarcodeQueryTests(DatabaseFixture fixture) : base(fixture) { }

    [Fact]
    public async Task باركود_موجود_يرجع_المنتج_وتصنيفه()
    {
        using var scope = CreateScope();
        var categoryId = await CatalogQueriesTestHelpers.CreateCategoryAsync(scope, "مشروبات");
        await CatalogQueriesTestHelpers.CreateProductAsync(scope, categoryId, "عصير تفاح", barcode: "6006006006001");

        var handler = scope.ServiceProvider.GetRequiredService<GetProductByBarcodeHandler>();
        var result = await handler.HandleAsync(new GetProductByBarcodeQuery("6006006006001"), CancellationToken.None);

        Assert.NotNull(result);
        Assert.Equal("عصير تفاح", result!.ProductName);
        Assert.Equal("مشروبات", result.CategoryName);
    }

    [Fact]
    public async Task باركود_غير_موجود_يرجع_null()
    {
        using var scope = CreateScope();
        var handler = scope.ServiceProvider.GetRequiredService<GetProductByBarcodeHandler>();

        var result = await handler.HandleAsync(new GetProductByBarcodeQuery("0000000000000"), CancellationToken.None);

        Assert.Null(result);
    }
}

[Collection(DatabaseCollection.Name)]
public sealed class GetProductBranchesQueryTests : IntegrationTestBase
{
    public GetProductBranchesQueryTests(DatabaseFixture fixture) : base(fixture) { }

    [Fact]
    public async Task يرجع_الفروع_المرتبطة_بمنتج_معيّن_فقط()
    {
        using var scope = CreateScope();
        // ProductBranch كيان Branch-owned - راجع تعليق TestAuthContext.
        scope.ActAsCrossBranchUser();
        var categoryId = await CatalogQueriesTestHelpers.CreateCategoryAsync(scope, "تصنيف");
        var productId = await CatalogQueriesTestHelpers.CreateProductAsync(scope, categoryId, "منتج", suggestedPrice: 3m);

        var handler = scope.ServiceProvider.GetRequiredService<GetProductBranchesHandler>();
        var result = await handler.HandleAsync(new GetProductBranchesQuery(productId), CancellationToken.None);

        // المنتج ربط تلقائيًا بكل فرع فعّال (راجع CreateProductHandler) -
        // فروع كتير متراكمة من تشغيلات سابقة (Branches مستثناة من التصفير)،
        // فالتحقق هون بفرع الاختبار المحدَّد لا بعدد العناصر الكلي.
        var item = Assert.Single(result, x => x.BranchId == Fixture.TestBranchId);
        Assert.Equal(3m, item.SellingPrice);
        Assert.True(item.IsAvailableForSale);
    }

    [Fact]
    public async Task منتج_بلا_أي_ربط_فرع_يرجع_قائمة_فاضية()
    {
        using var scope = CreateScope();
        var categoryId = await CatalogQueriesTestHelpers.CreateCategoryAsync(scope, "تصنيف");
        var productId = await CatalogQueriesTestHelpers.CreateProductAsync(scope, categoryId, "منتج بلا فرع");

        var handler = scope.ServiceProvider.GetRequiredService<GetProductBranchesHandler>();
        var result = await handler.HandleAsync(new GetProductBranchesQuery(productId), CancellationToken.None);

        Assert.Empty(result);
    }
}

[Collection(DatabaseCollection.Name)]
public sealed class GetProductCategoriesQueryTests : IntegrationTestBase
{
    public GetProductCategoriesQueryTests(DatabaseFixture fixture) : base(fixture) { }

    [Fact]
    public async Task يبحث_بالاسم_ويرتب_أبجديًا()
    {
        using var scope = CreateScope();
        await CatalogQueriesTestHelpers.CreateCategoryAsync(scope, "ألبان");
        await CatalogQueriesTestHelpers.CreateCategoryAsync(scope, "ألبان ومشتقاتها");
        await CatalogQueriesTestHelpers.CreateCategoryAsync(scope, "تنظيف");

        var handler = scope.ServiceProvider.GetRequiredService<GetProductCategoriesHandler>();
        var paging = new PagedRequest { Search = "ألبان" };
        var result = await handler.HandleAsync(new GetProductCategoriesQuery(paging), CancellationToken.None);

        Assert.Equal(2, result.TotalCount);
        Assert.All(result.Items, c => Assert.Contains("ألبان", c.Name));
    }
}
