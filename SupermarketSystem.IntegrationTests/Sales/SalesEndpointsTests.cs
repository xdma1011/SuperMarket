using System.Net;
using System.Net.Http.Json;
using Microsoft.Extensions.DependencyInjection;
using SupermarketSystem.Application.Sales.CompleteSale;
using SupermarketSystem.Infrastructure.Persistence;
using SupermarketSystem.IntegrationTests.Common;
using Xunit;

namespace SupermarketSystem.IntegrationTests.Sales;

/// <summary>اختبار HTTP كامل لـ SalesEndpoints.cs - 401 بلا توكن، 200/201 بتوكن Master Admin.</summary>
[Collection(DatabaseCollection.Name)]
public sealed class SalesEndpointsTests : IntegrationTestBase
{
    public SalesEndpointsTests(DatabaseFixture fixture) : base(fixture) { }

    [Fact]
    public async Task إتمام_بيع_بلا_توكن_يرجّع_401()
    {
        var client = CreateAnonymousClient();

        var response = await client.PostAsJsonAsync("/api/v1/sales", new
        {
            BranchId = Fixture.TestBranchId,
            ClientRequestId = Guid.NewGuid(),
            CustomerId = (Guid?)null,
            InvoiceLevelDiscountAmount = 0m,
            Items = Array.Empty<object>(),
            Payments = Array.Empty<object>()
        });

        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
    }

    [Fact]
    public async Task إتمام_بيع_بتوكن_Master_Admin_يرجّع_201()
    {
        using var scope = CreateScope();
        var db = CreateDbContext(scope);
        await TestDataBuilder.EnsureDocumentSequencesProvisionedAsync(db, Fixture.TestBranchId);
        var (product, unit) = await TestDataBuilder.CreateActiveProductAsync(db, "منتج HTTP للبيع");
        await TestDataBuilder.CreateProductBranchAsync(db, product.Id, Fixture.TestBranchId, 12m);
        await TestDataBuilder.SetStockAsync(db, product.Id, Fixture.TestBranchId, 10m);

        var client = await CreateAuthenticatedClientAsync();

        var response = await client.PostAsJsonAsync("/api/v1/sales", new
        {
            BranchId = Fixture.TestBranchId,
            ClientRequestId = Guid.NewGuid(),
            CustomerId = (Guid?)null,
            InvoiceLevelDiscountAmount = 0m,
            Items = new[]
            {
                new { ProductId = product.Id, ProductUnitId = unit.Id, Quantity = 1m, ManualDiscountAmount = 0m, ProductBatchId = (Guid?)null }
            },
            Payments = new[]
            {
                new { PaymentMethodId = TestDataBuilder.CashPaymentMethodId, Amount = 12m, ExternalReference = (string?)null, ClientRequestId = Guid.NewGuid() }
            }
        });

        Assert.Equal(HttpStatusCode.Created, response.StatusCode);
        var body = await response.Content.ReadFromJsonAsync<CompleteSaleResponse>();
        Assert.NotNull(body);
        Assert.Equal(12m, body!.TotalAmount);
    }

    [Fact]
    public async Task قائمة_فواتير_البيع_بلا_توكن_ترجّع_401()
    {
        var client = CreateAnonymousClient();
        var response = await client.GetAsync("/api/v1/sales");
        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
    }

    [Fact]
    public async Task قائمة_فواتير_البيع_بتوكن_صحيح_ترجّع_200()
    {
        var client = await CreateAuthenticatedClientAsync();
        var response = await client.GetAsync("/api/v1/sales");
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
    }

    [Fact]
    public async Task إلغاء_فاتورة_غير_موجودة_عبر_HTTP_يرجّع_404()
    {
        var client = await CreateAuthenticatedClientAsync();

        var response = await client.PostAsJsonAsync(
            $"/api/v1/sales/{Guid.NewGuid()}/void",
            new { Reason = 1, Notes = (string?)null });

        Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
    }
}
