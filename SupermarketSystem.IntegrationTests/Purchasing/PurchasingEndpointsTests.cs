using System.Net;
using System.Net.Http.Json;
using SupermarketSystem.IntegrationTests.Common;
using Xunit;

namespace SupermarketSystem.IntegrationTests.Purchasing;

/// <summary>اختبار HTTP لـ PurchasingEndpoints.cs.</summary>
[Collection(DatabaseCollection.Name)]
public sealed class PurchasingEndpointsTests : IntegrationTestBase
{
    public PurchasingEndpointsTests(DatabaseFixture fixture) : base(fixture) { }

    [Fact]
    public async Task إتمام_فاتورة_شراء_بلا_توكن_يرجّع_401()
    {
        var client = CreateAnonymousClient();

        var response = await client.PostAsJsonAsync("/api/v1/purchase-invoices", new
        {
            BranchId = Fixture.TestBranchId,
            SupplierId = Guid.NewGuid(),
            SupplierInvoiceReference = (string?)null,
            Items = Array.Empty<object>()
        });

        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
    }

    [Fact]
    public async Task إتمام_فاتورة_شراء_بتوكن_Master_Admin_يرجّع_201()
    {
        using var scope = CreateScope();
        var db = CreateDbContext(scope);
        await TestDataBuilder.EnsureDocumentSequencesProvisionedAsync(db, Fixture.TestBranchId);
        var (product, unit) = await TestDataBuilder.CreateActiveProductAsync(db, "منتج شراء HTTP");
        var supplier = await TestDataBuilder.CreateSupplierAsync(db);

        var client = await CreateAuthenticatedClientAsync();
        var response = await client.PostAsJsonAsync("/api/v1/purchase-invoices", new
        {
            BranchId = Fixture.TestBranchId,
            SupplierId = supplier.Id,
            SupplierInvoiceReference = (string?)null,
            Items = new[]
            {
                new
                {
                    ProductId = product.Id, ProductUnitId = unit.Id, Quantity = 2m, UnitCost = 3m,
                    ExistingProductBatchId = (Guid?)null, NewBatchNumber = (string?)null, NewBatchExpiryDate = (DateOnly?)null
                }
            }
        });

        Assert.Equal(HttpStatusCode.Created, response.StatusCode);
    }

    [Fact]
    public async Task قائمة_فواتير_الشراء_بتوكن_صحيح_ترجّع_200()
    {
        var client = await CreateAuthenticatedClientAsync();
        var response = await client.GetAsync("/api/v1/purchase-invoices");
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
    }

    [Fact]
    public async Task ديون_الموردين_بلا_توكن_ترجّع_401()
    {
        var client = CreateAnonymousClient();
        var response = await client.GetAsync("/api/v1/purchase-invoices/supplier-debts");
        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
    }

    [Fact]
    public async Task ديون_الموردين_بتوكن_صحيح_ترجّع_200()
    {
        var client = await CreateAuthenticatedClientAsync();
        var response = await client.GetAsync("/api/v1/purchase-invoices/supplier-debts");
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
    }
}
