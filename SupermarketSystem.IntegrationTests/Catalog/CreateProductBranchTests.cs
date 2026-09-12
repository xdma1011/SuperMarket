using Microsoft.Extensions.DependencyInjection;
using SupermarketSystem.Application.Catalog.CreateProduct;
using SupermarketSystem.Application.Catalog.CreateProductBranch;
using SupermarketSystem.Application.Catalog.CreateProductCategory;
using SupermarketSystem.Application.Common.Results;
using Xunit;

namespace SupermarketSystem.IntegrationTests.Catalog;

/// <summary>
/// "ربط منتج بفرع صراحة" - خطوة "الفروع والأسعار" اليدوية اللي CLAUDE.md
/// §1.8 بيحذّر إن نسيانها بيخلي المنتج غير مرئي كليًا للكاشير.
/// </summary>
[Collection(DatabaseCollection.Name)]
public sealed class CreateProductBranchTests : IntegrationTestBase
{
    public CreateProductBranchTests(DatabaseFixture fixture) : base(fixture) { }

    private async Task<Guid> SeedProductWithoutBranchAsync(IServiceScope scope)
    {
        var categoryHandler = scope.ServiceProvider.GetRequiredService<CreateProductCategoryHandler>();
        var category = await categoryHandler.HandleAsync(new CreateProductCategoryCommand("تصنيف", null), CancellationToken.None);

        var productHandler = scope.ServiceProvider.GetRequiredService<CreateProductHandler>();
        var product = await productHandler.HandleAsync(
            new CreateProductCommand(
                "منتج بلا فرع", null, category.Value.CategoryId, false, null, null,
                new[] { new CreateProductUnitDto("قطعة", 1m, true) },
                Array.Empty<CreateProductBarcodeDto>()),
            CancellationToken.None);

        return product.Value.ProductId;
    }

    [Fact]
    public async Task ربط_منتج_بفرع_بسعر_صريح_ينجح()
    {
        using var scope = CreateScope();
        var productId = await SeedProductWithoutBranchAsync(scope);
        var handler = scope.ServiceProvider.GetRequiredService<CreateProductBranchHandler>();

        var result = await handler.HandleAsync(
            new CreateProductBranchCommand(productId, Fixture.TestBranchId, 5.75m, MinimumStock: 2, MaximumStock: 50),
            CancellationToken.None);

        Assert.True(result.IsSuccess);
        Assert.Equal(5.75m, result.Value.SellingPrice);
    }

    [Fact]
    public async Task ربط_نفس_المنتج_بنفس_الفرع_مرتين_يفشل_بـConflict()
    {
        using var scope = CreateScope();
        // فحص "موجود أصلًا" جوّا الـHandler كيان Branch-owned - راجع تعليق TestAuthContext.
        scope.ActAsCrossBranchUser();
        var productId = await SeedProductWithoutBranchAsync(scope);
        var handler = scope.ServiceProvider.GetRequiredService<CreateProductBranchHandler>();

        var first = await handler.HandleAsync(
            new CreateProductBranchCommand(productId, Fixture.TestBranchId, 5m, null, null), CancellationToken.None);
        Assert.True(first.IsSuccess);

        var second = await handler.HandleAsync(
            new CreateProductBranchCommand(productId, Fixture.TestBranchId, 6m, null, null), CancellationToken.None);

        Assert.True(second.IsFailure);
        Assert.Equal(ErrorType.Conflict, second.Error!.Type);
        Assert.Equal("ProductBranch.AlreadyExists", second.Error.Code);
    }

    [Fact]
    public async Task ربط_بمنتج_غير_موجود_يفشل_بـNotFound()
    {
        using var scope = CreateScope();
        var handler = scope.ServiceProvider.GetRequiredService<CreateProductBranchHandler>();

        var result = await handler.HandleAsync(
            new CreateProductBranchCommand(Guid.NewGuid(), Fixture.TestBranchId, 5m, null, null), CancellationToken.None);

        Assert.True(result.IsFailure);
        Assert.Equal("ProductBranch.ProductNotFound", result.Error!.Code);
    }

    [Fact]
    public async Task ربط_بسعر_سالب_يفشل_بخطأ_تحقق()
    {
        using var scope = CreateScope();
        var productId = await SeedProductWithoutBranchAsync(scope);
        var handler = scope.ServiceProvider.GetRequiredService<CreateProductBranchHandler>();

        var result = await handler.HandleAsync(
            new CreateProductBranchCommand(productId, Fixture.TestBranchId, -1m, null, null), CancellationToken.None);

        Assert.True(result.IsFailure);
        Assert.Equal(ErrorType.Validation, result.Error!.Type);
        Assert.Equal("ProductBranch.SellingPriceNegative", result.Error.Code);
    }

    [Fact]
    public async Task ربط_بحد_أدنى_أكبر_من_الحد_الأقصى_يفشل_بخطأ_تحقق()
    {
        using var scope = CreateScope();
        var productId = await SeedProductWithoutBranchAsync(scope);
        var handler = scope.ServiceProvider.GetRequiredService<CreateProductBranchHandler>();

        var result = await handler.HandleAsync(
            new CreateProductBranchCommand(productId, Fixture.TestBranchId, 5m, MinimumStock: 100, MaximumStock: 10),
            CancellationToken.None);

        Assert.True(result.IsFailure);
        Assert.Equal("ProductBranch.MinExceedsMax", result.Error!.Code);
    }
}
