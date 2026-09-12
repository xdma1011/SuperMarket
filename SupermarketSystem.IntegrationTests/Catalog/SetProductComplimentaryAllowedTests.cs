using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using SupermarketSystem.Application.Catalog.CreateProduct;
using SupermarketSystem.Application.Catalog.CreateProductCategory;
using SupermarketSystem.Application.Catalog.SetProductComplimentaryAllowed;
using SupermarketSystem.Application.Common.Results;
using Xunit;

namespace SupermarketSystem.IntegrationTests.Catalog;

[Collection(DatabaseCollection.Name)]
public sealed class SetProductComplimentaryAllowedTests : IntegrationTestBase
{
    public SetProductComplimentaryAllowedTests(DatabaseFixture fixture) : base(fixture) { }

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
    public async Task تفعيل_السماح_بالضيافة_لمنتج_ينجح()
    {
        using var scope = CreateScope();
        var productId = await SeedProductAsync(scope);
        var handler = scope.ServiceProvider.GetRequiredService<SetProductComplimentaryAllowedHandler>();

        var result = await handler.HandleAsync(new SetProductComplimentaryAllowedCommand(productId, true), CancellationToken.None);

        Assert.True(result.IsSuccess);

        var db = CreateDbContext(scope);
        var product = await db.Products.AsNoTracking().SingleAsync(p => p.Id == productId);
        Assert.True(product.IsComplimentaryAllowed);
    }

    [Fact]
    public async Task إيقاف_السماح_بالضيافة_بعد_تفعيله_ينجح()
    {
        using var scope = CreateScope();
        var productId = await SeedProductAsync(scope);
        var handler = scope.ServiceProvider.GetRequiredService<SetProductComplimentaryAllowedHandler>();

        await handler.HandleAsync(new SetProductComplimentaryAllowedCommand(productId, true), CancellationToken.None);
        var result = await handler.HandleAsync(new SetProductComplimentaryAllowedCommand(productId, false), CancellationToken.None);

        Assert.True(result.IsSuccess);

        var db = CreateDbContext(scope);
        var product = await db.Products.AsNoTracking().SingleAsync(p => p.Id == productId);
        Assert.False(product.IsComplimentaryAllowed);
    }

    [Fact]
    public async Task منتج_غير_موجود_يفشل_بـNotFound()
    {
        using var scope = CreateScope();
        var handler = scope.ServiceProvider.GetRequiredService<SetProductComplimentaryAllowedHandler>();

        var result = await handler.HandleAsync(new SetProductComplimentaryAllowedCommand(Guid.NewGuid(), true), CancellationToken.None);

        Assert.True(result.IsFailure);
        Assert.Equal(ErrorType.NotFound, result.Error!.Type);
        Assert.Equal("Product.NotFound", result.Error.Code);
    }
}
