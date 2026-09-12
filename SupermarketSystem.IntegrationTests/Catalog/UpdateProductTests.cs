using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using SupermarketSystem.Application.Catalog.CreateProduct;
using SupermarketSystem.Application.Catalog.CreateProductCategory;
using SupermarketSystem.Application.Catalog.UpdateProduct;
using SupermarketSystem.Application.Common.Results;
using Xunit;

namespace SupermarketSystem.IntegrationTests.Catalog;

[Collection(DatabaseCollection.Name)]
public sealed class UpdateProductTests : IntegrationTestBase
{
    public UpdateProductTests(DatabaseFixture fixture) : base(fixture) { }

    private async Task<(Guid CategoryId, Guid ProductId)> SeedProductAsync(IServiceScope scope)
    {
        var categoryHandler = scope.ServiceProvider.GetRequiredService<CreateProductCategoryHandler>();
        var category = await categoryHandler.HandleAsync(new CreateProductCategoryCommand("تصنيف أصلي", null), CancellationToken.None);
        Assert.True(category.IsSuccess);

        var productHandler = scope.ServiceProvider.GetRequiredService<CreateProductHandler>();
        var product = await productHandler.HandleAsync(
            new CreateProductCommand(
                "منتج قديم", null, category.Value.CategoryId, false, null, null,
                new[] { new CreateProductUnitDto("قطعة", 1m, true) },
                Array.Empty<CreateProductBarcodeDto>()),
            CancellationToken.None);
        Assert.True(product.IsSuccess);

        return (category.Value.CategoryId, product.Value.ProductId);
    }

    [Fact]
    public async Task تعديل_منتج_موجود_يحدث_القيم_فعليًا()
    {
        using var scope = CreateScope();
        var (categoryId, productId) = await SeedProductAsync(scope);
        var handler = scope.ServiceProvider.GetRequiredService<UpdateProductHandler>();

        var result = await handler.HandleAsync(
            new UpdateProductCommand(productId, "منتج جديد", categoryId, 9.99m, 30),
            CancellationToken.None);

        Assert.True(result.IsSuccess);

        var db = CreateDbContext(scope);
        var product = await db.Products.AsNoTracking().SingleAsync(p => p.Id == productId);
        Assert.Equal("منتج جديد", product.Name);
        Assert.Equal(9.99m, product.SuggestedRetailPrice);
        Assert.Equal(30, product.ExpectedShelfLifeDays);
    }

    [Fact]
    public async Task تعديل_منتج_باسم_فاضي_يفشل_بخطأ_تحقق()
    {
        using var scope = CreateScope();
        var (categoryId, productId) = await SeedProductAsync(scope);
        var handler = scope.ServiceProvider.GetRequiredService<UpdateProductHandler>();

        var result = await handler.HandleAsync(
            new UpdateProductCommand(productId, "   ", categoryId, null, null),
            CancellationToken.None);

        Assert.True(result.IsFailure);
        Assert.Equal(ErrorType.Validation, result.Error!.Type);
        Assert.Equal("Product.NameRequired", result.Error.Code);
    }

    [Fact]
    public async Task تعديل_منتج_غير_موجود_يفشل_بـNotFound()
    {
        using var scope = CreateScope();
        var (categoryId, _) = await SeedProductAsync(scope);
        var handler = scope.ServiceProvider.GetRequiredService<UpdateProductHandler>();

        var result = await handler.HandleAsync(
            new UpdateProductCommand(Guid.NewGuid(), "اسم", categoryId, null, null),
            CancellationToken.None);

        Assert.True(result.IsFailure);
        Assert.Equal(ErrorType.NotFound, result.Error!.Type);
        Assert.Equal("Product.NotFound", result.Error.Code);
    }

    [Fact]
    public async Task تعديل_منتج_لتصنيف_غير_موجود_يفشل_بـNotFound()
    {
        using var scope = CreateScope();
        var (_, productId) = await SeedProductAsync(scope);
        var handler = scope.ServiceProvider.GetRequiredService<UpdateProductHandler>();

        var result = await handler.HandleAsync(
            new UpdateProductCommand(productId, "اسم", Guid.NewGuid(), null, null),
            CancellationToken.None);

        Assert.True(result.IsFailure);
        Assert.Equal("Product.CategoryNotFound", result.Error!.Code);
    }
}
