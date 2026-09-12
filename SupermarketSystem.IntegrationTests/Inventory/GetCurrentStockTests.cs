using Microsoft.Extensions.DependencyInjection;
using SupermarketSystem.Application.Common.Pagination;
using SupermarketSystem.Application.Inventory.GetCurrentStock;
using SupermarketSystem.IntegrationTests.Common;
using Xunit;

namespace SupermarketSystem.IntegrationTests.Inventory;

/// <summary>GetCurrentStockHandler — يجمّع Stock على مستوى (منتج، فرع) بغض النظر عن عدد الدفعات.</summary>
[Collection(DatabaseCollection.Name)]
public sealed class GetCurrentStockTests : IntegrationTestBase
{
    public GetCurrentStockTests(DatabaseFixture fixture) : base(fixture) { }

    [Fact]
    public async Task المخزون_الحالي_يجمع_كميات_دفعات_متعددة_لنفس_المنتج_بنفس_الفرع()
    {
        using var scope = CreateScope();
        await TestDataBuilder.ActAsAdminAsync(scope, Fixture);
        var db = CreateDbContext(scope);
        var (product, _) = await TestDataBuilder.CreateActiveProductAsync(db, "منتج متعدد الدفعات", isBatchTracked: true);
        var batch1 = await TestDataBuilder.CreateBatchAsync(db, product.Id, Fixture.TestBranchId, "B1");
        var batch2 = await TestDataBuilder.CreateBatchAsync(db, product.Id, Fixture.TestBranchId, "B2");
        await TestDataBuilder.SetStockAsync(db, product.Id, Fixture.TestBranchId, 10m, batch1.Id);
        await TestDataBuilder.SetStockAsync(db, product.Id, Fixture.TestBranchId, 15m, batch2.Id);

        var handler = scope.ServiceProvider.GetRequiredService<GetCurrentStockHandler>();
        var result = await handler.HandleAsync(new GetCurrentStockQuery(new PagedRequest(), Fixture.TestBranchId), CancellationToken.None);

        var row = Assert.Single(result.Items, i => i.ProductId == product.Id);
        Assert.Equal(25m, row.QuantityOnHand);
        Assert.Equal("حبة", row.BaseUnitName);
    }

    [Fact]
    public async Task البحث_باسم_المنتج_يفلتر_النتائج()
    {
        using var scope = CreateScope();
        await TestDataBuilder.ActAsAdminAsync(scope, Fixture);
        var db = CreateDbContext(scope);
        var (productA, _) = await TestDataBuilder.CreateActiveProductAsync(db, "أرز بسمتي");
        await TestDataBuilder.SetStockAsync(db, productA.Id, Fixture.TestBranchId, 5m);
        var (productB, _) = await TestDataBuilder.CreateActiveProductAsync(db, "سكر أبيض");
        await TestDataBuilder.SetStockAsync(db, productB.Id, Fixture.TestBranchId, 5m);

        var handler = scope.ServiceProvider.GetRequiredService<GetCurrentStockHandler>();
        var result = await handler.HandleAsync(
            new GetCurrentStockQuery(new PagedRequest { Search = "بسمتي" }, Fixture.TestBranchId), CancellationToken.None);

        var item = Assert.Single(result.Items);
        Assert.Equal("أرز بسمتي", item.ProductName);
    }
}
