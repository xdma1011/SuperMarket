using System.Net;
using System.Net.Http.Json;
using SupermarketSystem.IntegrationTests.Common;
using Xunit;

namespace SupermarketSystem.IntegrationTests.Inventory;

/// <summary>اختبار HTTP لـ InventoryAdjustmentEndpoints.cs.</summary>
[Collection(DatabaseCollection.Name)]
public sealed class InventoryAdjustmentEndpointsTests : IntegrationTestBase
{
    public InventoryAdjustmentEndpointsTests(DatabaseFixture fixture) : base(fixture) { }

    [Fact]
    public async Task تسجيل_ضيافة_بلا_توكن_يرجّع_401()
    {
        var client = CreateAnonymousClient();

        var response = await client.PostAsJsonAsync("/api/v1/inventory/complimentary-issues", new
        {
            ProductId = Guid.NewGuid(),
            ProductUnitId = Guid.NewGuid(),
            BranchId = Fixture.TestBranchId,
            Quantity = 1m,
            Reason = (string?)null
        });

        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
    }

    [Fact]
    public async Task تسجيل_ضيافة_بتوكن_Master_Admin_ينجح()
    {
        using var scope = CreateScope();
        var db = CreateDbContext(scope);
        var (product, unit) = await TestDataBuilder.CreateActiveProductAsync(db, "منتج ضيافة HTTP", isComplimentaryAllowed: true);
        await TestDataBuilder.SetStockAsync(db, product.Id, Fixture.TestBranchId, 20m);

        var client = await CreateAuthenticatedClientAsync();
        var response = await client.PostAsJsonAsync("/api/v1/inventory/complimentary-issues", new
        {
            ProductId = product.Id,
            ProductUnitId = unit.Id,
            BranchId = Fixture.TestBranchId,
            Quantity = 1m,
            Reason = "اختبار HTTP"
        });

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
    }

    [Fact]
    public async Task المخزون_الحالي_بلا_توكن_يرجّع_401()
    {
        var client = CreateAnonymousClient();
        var response = await client.GetAsync("/api/v1/inventory/current-stock");
        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
    }

    [Fact]
    public async Task المخزون_الحالي_بتوكن_صحيح_يرجّع_200()
    {
        var client = await CreateAuthenticatedClientAsync();
        var response = await client.GetAsync("/api/v1/inventory/current-stock");
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
    }
}
