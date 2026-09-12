using System.Net;
using System.Net.Http.Json;
using SupermarketSystem.IntegrationTests.Common;
using Xunit;

namespace SupermarketSystem.IntegrationTests.Inventory;

/// <summary>اختبار HTTP لـ StocktakeEndpoints.cs.</summary>
[Collection(DatabaseCollection.Name)]
public sealed class StocktakeEndpointsTests : IntegrationTestBase
{
    public StocktakeEndpointsTests(DatabaseFixture fixture) : base(fixture) { }

    [Fact]
    public async Task إنشاء_جرد_بلا_توكن_يرجّع_401()
    {
        var client = CreateAnonymousClient();

        var response = await client.PostAsJsonAsync("/api/v1/stocktakes", new
        {
            BranchId = Fixture.TestBranchId,
            IncludeAllProductsAtBranch = true,
            ProductIds = (object?)null
        });

        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
    }

    [Fact]
    public async Task إنشاء_جرد_بتوكن_Master_Admin_يرجّع_201()
    {
        using var scope = CreateScope();
        var db = CreateDbContext(scope);
        await TestDataBuilder.EnsureDocumentSequencesProvisionedAsync(db, Fixture.TestBranchId);
        var (product, _) = await TestDataBuilder.CreateActiveProductAsync(db, "منتج جرد HTTP");
        await TestDataBuilder.CreateProductBranchAsync(db, product.Id, Fixture.TestBranchId, 10m);
        await TestDataBuilder.SetStockAsync(db, product.Id, Fixture.TestBranchId, 5m);

        var client = await CreateAuthenticatedClientAsync();
        var response = await client.PostAsJsonAsync("/api/v1/stocktakes", new
        {
            BranchId = Fixture.TestBranchId,
            IncludeAllProductsAtBranch = true,
            ProductIds = (object?)null
        });

        Assert.Equal(HttpStatusCode.Created, response.StatusCode);
    }

    [Fact]
    public async Task قائمة_الجرد_بتوكن_صحيح_ترجّع_200()
    {
        var client = await CreateAuthenticatedClientAsync();
        var response = await client.GetAsync("/api/v1/stocktakes");
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
    }
}
