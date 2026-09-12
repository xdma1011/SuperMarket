using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using SupermarketSystem.Application.Catalog.AddProductUnit;
using SupermarketSystem.Application.Catalog.CreateProduct;
using SupermarketSystem.Application.Catalog.CreateProductCategory;
using SupermarketSystem.Application.Common.Results;
using Xunit;

namespace SupermarketSystem.IntegrationTests.Catalog;

[Collection(DatabaseCollection.Name)]
public sealed class AddProductUnitTests : IntegrationTestBase
{
    public AddProductUnitTests(DatabaseFixture fixture) : base(fixture) { }

    private async Task<Guid> SeedProductAsync(IServiceScope scope)
    {
        var categoryHandler = scope.ServiceProvider.GetRequiredService<CreateProductCategoryHandler>();
        var category = await categoryHandler.HandleAsync(new CreateProductCategoryCommand("تصنيف", null), CancellationToken.None);

        var productHandler = scope.ServiceProvider.GetRequiredService<CreateProductHandler>();
        var product = await productHandler.HandleAsync(
            new CreateProductCommand(
                "منتج", null, category.Value.CategoryId, false, null, null,
                new[] { new CreateProductUnitDto("قطعة", 1m, true) },
                Array.Empty<CreateProductBarcodeDto>()),
            CancellationToken.None);

        return product.Value.ProductId;
    }

    [Fact]
    public async Task إضافة_وحدة_كرتونة_بمعامل_تحويل_ينجح()
    {
        using var scope = CreateScope();
        var productId = await SeedProductAsync(scope);
        var handler = scope.ServiceProvider.GetRequiredService<AddProductUnitHandler>();

        var result = await handler.HandleAsync(
            new AddProductUnitCommand(productId, "كرتونة", 12m, "9998887776665"), CancellationToken.None);

        Assert.True(result.IsSuccess);
        Assert.NotNull(result.Value.BarcodeId);

        var db = CreateDbContext(scope);
        var unit = await db.ProductUnits.AsNoTracking().SingleAsync(u => u.Id == result.Value.ProductUnitId);
        Assert.Equal("كرتونة", unit.UnitName);
        Assert.False(unit.IsBaseUnit);
    }

    [Fact]
    public async Task إضافة_وحدة_بباركود_مستخدم_أصلًا_لمنتج_آخر_يفشل_بـConflict()
    {
        using var scope = CreateScope();
        var productId = await SeedProductAsync(scope);
        var handler = scope.ServiceProvider.GetRequiredService<AddProductUnitHandler>();

        var first = await handler.HandleAsync(
            new AddProductUnitCommand(productId, "كرتونة", 12m, "1112223334445"), CancellationToken.None);
        Assert.True(first.IsSuccess);

        var otherProductId = await SeedProductAsync(scope);
        var second = await handler.HandleAsync(
            new AddProductUnitCommand(otherProductId, "كرتونة", 12m, "1112223334445"), CancellationToken.None);

        Assert.True(second.IsFailure);
        Assert.Equal(ErrorType.Conflict, second.Error!.Type);
        Assert.Equal("ProductUnit.BarcodeTaken", second.Error.Code);
    }

    [Fact]
    public async Task إضافة_وحدة_بمعامل_تحويل_غير_موجب_يفشل_بخطأ_تحقق()
    {
        using var scope = CreateScope();
        var productId = await SeedProductAsync(scope);
        var handler = scope.ServiceProvider.GetRequiredService<AddProductUnitHandler>();

        var result = await handler.HandleAsync(
            new AddProductUnitCommand(productId, "كرتونة", 0m, null), CancellationToken.None);

        Assert.True(result.IsFailure);
        Assert.Equal(ErrorType.Validation, result.Error!.Type);
        Assert.Equal("ProductUnit.ConversionFactorMustBePositive", result.Error.Code);
    }

    [Fact]
    public async Task إضافة_وحدة_لمنتج_غير_موجود_يفشل_بـNotFound()
    {
        using var scope = CreateScope();
        var handler = scope.ServiceProvider.GetRequiredService<AddProductUnitHandler>();

        var result = await handler.HandleAsync(
            new AddProductUnitCommand(Guid.NewGuid(), "كرتونة", 12m, null), CancellationToken.None);

        Assert.True(result.IsFailure);
        Assert.Equal(ErrorType.NotFound, result.Error!.Type);
        Assert.Equal("ProductUnit.ProductNotFound", result.Error.Code);
    }
}
