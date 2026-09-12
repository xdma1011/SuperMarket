using System.Net;
using System.Net.Http.Json;
using Xunit;

namespace SupermarketSystem.IntegrationTests.Purchasing;

/// <summary>اختبار HTTP لـ SupplierEndpoints.cs.</summary>
[Collection(DatabaseCollection.Name)]
public sealed class SupplierEndpointsTests : IntegrationTestBase
{
    public SupplierEndpointsTests(DatabaseFixture fixture) : base(fixture) { }

    [Fact]
    public async Task إنشاء_مورد_بلا_توكن_يرجّع_401()
    {
        var client = CreateAnonymousClient();
        var response = await client.PostAsJsonAsync("/api/v1/suppliers", new { Name = "مورد HTTP" });
        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
    }

    [Fact]
    public async Task إنشاء_مورد_بتوكن_Master_Admin_يرجّع_201()
    {
        var client = await CreateAuthenticatedClientAsync();

        var response = await client.PostAsJsonAsync("/api/v1/suppliers", new
        {
            Name = "مورد HTTP",
            ContactName = (string?)null,
            Phone = (string?)null,
            Email = (string?)null,
            Street = (string?)null,
            City = (string?)null,
            PostalCode = (string?)null,
            Country = (string?)null
        });

        Assert.Equal(HttpStatusCode.Created, response.StatusCode);
    }

    [Fact]
    public async Task قائمة_الموردين_بتوكن_صحيح_ترجّع_200()
    {
        var client = await CreateAuthenticatedClientAsync();
        var response = await client.GetAsync("/api/v1/suppliers");
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
    }
}
