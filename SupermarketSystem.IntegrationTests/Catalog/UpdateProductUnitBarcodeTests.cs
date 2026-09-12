using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using SupermarketSystem.Application.Catalog.CreateProduct;
using SupermarketSystem.Application.Catalog.CreateProductCategory;
using SupermarketSystem.Application.Catalog.UpdateProductUnitBarcode;
using SupermarketSystem.Application.Common.Results;
using Xunit;

namespace SupermarketSystem.IntegrationTests.Catalog;

[Collection(DatabaseCollection.Name)]
public sealed class UpdateProductUnitBarcodeTests : IntegrationTestBase
{
    public UpdateProductUnitBarcodeTests(DatabaseFixture fixture) : base(fixture) { }

    private async Task<(Guid ProductId, Guid UnitId)> SeedProductAsync(IServiceScope scope, string? barcode = null)
    {
        var categoryHandler = scope.ServiceProvider.GetRequiredService<CreateProductCategoryHandler>();
        var category = await categoryHandler.HandleAsync(new CreateProductCategoryCommand("تصنيف", null), CancellationToken.None);

        var productHandler = scope.ServiceProvider.GetRequiredService<CreateProductHandler>();
        var barcodes = barcode is null
            ? Array.Empty<CreateProductBarcodeDto>()
            : new[] { new CreateProductBarcodeDto(barcode, "قطعة") };

        var product = await productHandler.HandleAsync(
            new CreateProductCommand(
                "منتج", null, category.Value.CategoryId, false, null, null,
                new[] { new CreateProductUnitDto("قطعة", 1m, true) },
                barcodes),
            CancellationToken.None);

        var db = CreateDbContext(scope);
        var unit = await db.ProductUnits.AsNoTracking().SingleAsync(u => u.ProductId == product.Value.ProductId);
        return (product.Value.ProductId, unit.Id);
    }

    [Fact]
    public async Task تحديث_باركود_وحدة_بلا_باركود_سابق_ينجح()
    {
        using var scope = CreateScope();
        var (productId, unitId) = await SeedProductAsync(scope);
        var handler = scope.ServiceProvider.GetRequiredService<UpdateProductUnitBarcodeHandler>();

        var result = await handler.HandleAsync(
            new UpdateProductUnitBarcodeCommand(productId, unitId, "5551112223334"), CancellationToken.None);

        Assert.True(result.IsSuccess);

        var db = CreateDbContext(scope);
        var barcode = await db.ProductBarcodes.AsNoTracking().SingleAsync(b => b.ProductUnitId == unitId);
        Assert.Equal("5551112223334", barcode.BarcodeValue);
    }

    [Fact]
    public async Task إرسال_قيمة_فاضية_يحذف_الباركود_الحالي_للوحدة()
    {
        using var scope = CreateScope();
        var (productId, unitId) = await SeedProductAsync(scope, barcode: "1231231231230");
        var handler = scope.ServiceProvider.GetRequiredService<UpdateProductUnitBarcodeHandler>();

        var result = await handler.HandleAsync(
            new UpdateProductUnitBarcodeCommand(productId, unitId, BarcodeValue: null), CancellationToken.None);

        Assert.True(result.IsSuccess);

        var db = CreateDbContext(scope);
        var stillExists = await db.ProductBarcodes.AsNoTracking().AnyAsync(b => b.ProductUnitId == unitId);
        Assert.False(stillExists);
    }

    [Fact]
    public async Task تحديث_باركود_لقيمة_مستخدمة_بوحدة_أخرى_يفشل_بـConflict()
    {
        using var scope = CreateScope();
        var (_, otherUnitId) = await SeedProductAsync(scope, barcode: "7778889990001");
        var (productId, unitId) = await SeedProductAsync(scope);
        var handler = scope.ServiceProvider.GetRequiredService<UpdateProductUnitBarcodeHandler>();

        var result = await handler.HandleAsync(
            new UpdateProductUnitBarcodeCommand(productId, unitId, "7778889990001"), CancellationToken.None);

        Assert.True(result.IsFailure);
        Assert.Equal(ErrorType.Conflict, result.Error!.Type);
        Assert.Equal("ProductUnit.BarcodeTaken", result.Error.Code);
        Assert.NotEqual(Guid.Empty, otherUnitId);
    }

    [Fact]
    public async Task تحديث_باركود_لمنتج_غير_موجود_يفشل_بـNotFound()
    {
        using var scope = CreateScope();
        var handler = scope.ServiceProvider.GetRequiredService<UpdateProductUnitBarcodeHandler>();

        var result = await handler.HandleAsync(
            new UpdateProductUnitBarcodeCommand(Guid.NewGuid(), Guid.NewGuid(), "123"), CancellationToken.None);

        Assert.True(result.IsFailure);
        Assert.Equal(ErrorType.NotFound, result.Error!.Type);
        Assert.Equal("ProductUnit.ProductNotFound", result.Error.Code);
    }
}
