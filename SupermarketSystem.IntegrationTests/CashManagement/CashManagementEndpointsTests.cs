using System.Net;
using System.Net.Http.Json;
using Xunit;

namespace SupermarketSystem.IntegrationTests.CashManagement;

/// <summary>اختبار HTTP لـ CashManagementEndpoints.cs - 401 بلا توكن، 201/200 بتوكن Master Admin.</summary>
[Collection(DatabaseCollection.Name)]
public sealed class CashManagementEndpointsTests : IntegrationTestBase
{
    public CashManagementEndpointsTests(DatabaseFixture fixture) : base(fixture) { }

    [Fact]
    public async Task تقفيل_الصندوق_بلا_توكن_يرجّع_401()
    {
        var client = CreateAnonymousClient();

        var response = await client.PostAsJsonAsync("/api/v1/cash-closings", new
        {
            BranchId = Fixture.TestBranchId,
            BusinessDate = DateOnly.FromDateTime(DateTime.UtcNow),
            CountedCash = 0m,
            CountedDetails = Array.Empty<object>()
        });

        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
    }

    [Fact]
    public async Task تقفيل_الصندوق_بتوكن_Master_Admin_يرجّع_201()
    {
        var client = await CreateAuthenticatedClientAsync();

        var response = await client.PostAsJsonAsync("/api/v1/cash-closings", new
        {
            BranchId = Fixture.TestBranchId,
            BusinessDate = DateOnly.FromDateTime(DateTime.UtcNow),
            CountedCash = 0m,
            CountedDetails = Array.Empty<object>()
        });

        Assert.Equal(HttpStatusCode.Created, response.StatusCode);
    }

    [Fact]
    public async Task قائمة_تقفيلات_الصندوق_بتوكن_صحيح_ترجّع_200()
    {
        var client = await CreateAuthenticatedClientAsync();
        var response = await client.GetAsync("/api/v1/cash-closings");
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
    }
}
