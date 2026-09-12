using System.Net;
using System.Net.Http.Json;
using SupermarketSystem.IntegrationTests.Common;
using Xunit;

namespace SupermarketSystem.IntegrationTests.Inventory;

/// <summary>اختبار HTTP لـ StockTransferEndpoints.cs.</summary>
[Collection(DatabaseCollection.Name)]
public sealed class StockTransferEndpointsTests : IntegrationTestBase
{
    public StockTransferEndpointsTests(DatabaseFixture fixture) : base(fixture) { }

    [Fact]
    public async Task إنشاء_نقل_مخزون_بلا_توكن_يرجّع_401()
    {
        var client = CreateAnonymousClient();

        var response = await client.PostAsJsonAsync("/api/v1/stock-transfers", new
        {
            SourceBranchId = Fixture.TestBranchId,
            DestinationBranchId = Guid.NewGuid(),
            Items = Array.Empty<object>()
        });

        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
    }

    [Fact]
    public async Task إنشاء_نقل_مخزون_بتوكن_Master_Admin_يرجّع_201()
    {
        using var scope = CreateScope();
        var db = CreateDbContext(scope);
        await TestDataBuilder.EnsureDocumentSequencesProvisionedAsync(db, Fixture.TestBranchId);
        var destinationBranch = await TestDataBuilder.CreateBranchAsync(db, "فرع نقل HTTP", "HTTPD1");
        var (product, unit) = await TestDataBuilder.CreateActiveProductAsync(db, "منتج نقل HTTP");
        await TestDataBuilder.SetStockAsync(db, product.Id, Fixture.TestBranchId, 10m);

        var client = await CreateAuthenticatedClientAsync();
        var response = await client.PostAsJsonAsync("/api/v1/stock-transfers", new
        {
            SourceBranchId = Fixture.TestBranchId,
            DestinationBranchId = destinationBranch.Id,
            Items = new[] { new { ProductId = product.Id, ProductUnitId = unit.Id, Quantity = 2m, SourceProductBatchId = (Guid?)null } }
        });

        Assert.Equal(HttpStatusCode.Created, response.StatusCode);
    }

    [Fact]
    public async Task قائمة_عمليات_النقل_بتوكن_صحيح_ترجّع_200()
    {
        var client = await CreateAuthenticatedClientAsync();
        var response = await client.GetAsync("/api/v1/stock-transfers");
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
    }
}
