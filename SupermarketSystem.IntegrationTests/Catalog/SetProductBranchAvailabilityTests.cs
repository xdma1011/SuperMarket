using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using SupermarketSystem.Application.Catalog.CreateProduct;
using SupermarketSystem.Application.Catalog.CreateProductBranch;
using SupermarketSystem.Application.Catalog.CreateProductCategory;
using SupermarketSystem.Application.Catalog.SetProductBranchAvailability;
using SupermarketSystem.Application.Common.Results;
using Xunit;

namespace SupermarketSystem.IntegrationTests.Catalog;

/// <summary>
/// كانت مفقودة بالكامل بالإنتاج (راجع تعليق SetProductBranchAvailabilityCommand)
/// - هذا كان سبب Sale.ProductNotActive الغامض بلا أي طريقة تعديله من الواجهة.
/// </summary>
[Collection(DatabaseCollection.Name)]
public sealed class SetProductBranchAvailabilityTests : IntegrationTestBase
{
    public SetProductBranchAvailabilityTests(DatabaseFixture fixture) : base(fixture) { }

    private async Task<Guid> SeedProductBranchAsync(IServiceScope scope)
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

        var branchHandler = scope.ServiceProvider.GetRequiredService<CreateProductBranchHandler>();
        var productBranch = await branchHandler.HandleAsync(
            new CreateProductBranchCommand(product.Value.ProductId, Fixture.TestBranchId, 4m, null, null),
            CancellationToken.None);

        return productBranch.Value.ProductBranchId;
    }

    [Fact]
    public async Task إيقاف_بيع_منتج_بفرع_ينجح()
    {
        using var scope = CreateScope();
        // ProductBranch كيان Branch-owned - راجع تعليق TestAuthContext.
        scope.ActAsCrossBranchUser();
        var productBranchId = await SeedProductBranchAsync(scope);
        var handler = scope.ServiceProvider.GetRequiredService<SetProductBranchAvailabilityHandler>();

        var result = await handler.HandleAsync(
            new SetProductBranchAvailabilityCommand(productBranchId, IsAvailableForSale: false), CancellationToken.None);

        Assert.True(result.IsSuccess);

        var db = CreateDbContext(scope);
        var productBranch = await db.ProductBranches.AsNoTracking().SingleAsync(pb => pb.Id == productBranchId);
        Assert.False(productBranch.IsAvailableForSale);
    }

    [Fact]
    public async Task تفعيل_بيع_منتج_موقوف_يعيده_متاحًا()
    {
        using var scope = CreateScope();
        scope.ActAsCrossBranchUser();
        var productBranchId = await SeedProductBranchAsync(scope);
        var handler = scope.ServiceProvider.GetRequiredService<SetProductBranchAvailabilityHandler>();

        await handler.HandleAsync(new SetProductBranchAvailabilityCommand(productBranchId, false), CancellationToken.None);
        var result = await handler.HandleAsync(new SetProductBranchAvailabilityCommand(productBranchId, true), CancellationToken.None);

        Assert.True(result.IsSuccess);

        var db = CreateDbContext(scope);
        var productBranch = await db.ProductBranches.AsNoTracking().SingleAsync(pb => pb.Id == productBranchId);
        Assert.True(productBranch.IsAvailableForSale);
    }

    [Fact]
    public async Task ربط_غير_موجود_يفشل_بـNotFound()
    {
        using var scope = CreateScope();
        var handler = scope.ServiceProvider.GetRequiredService<SetProductBranchAvailabilityHandler>();

        var result = await handler.HandleAsync(
            new SetProductBranchAvailabilityCommand(Guid.NewGuid(), false), CancellationToken.None);

        Assert.True(result.IsFailure);
        Assert.Equal(ErrorType.NotFound, result.Error!.Type);
        Assert.Equal("ProductBranch.NotFound", result.Error.Code);
    }
}
