using System.Net;
using Xunit;

namespace SupermarketSystem.IntegrationTests.Reviews;

/// <summary>اختبار HTTP لـ ReviewsEndpoints.cs - 401 بلا توكن، 200/404 بتوكن Master Admin.</summary>
[Collection(DatabaseCollection.Name)]
public sealed class ReviewsEndpointsTests : IntegrationTestBase
{
    public ReviewsEndpointsTests(DatabaseFixture fixture) : base(fixture) { }

    [Fact]
    public async Task قائمة_المراجعات_المعلَّقة_بلا_توكن_ترجّع_401()
    {
        var client = CreateAnonymousClient();
        var response = await client.GetAsync("/api/v1/reviews");
        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
    }

    [Fact]
    public async Task قائمة_المراجعات_المعلَّقة_بتوكن_Master_Admin_ترجّع_200()
    {
        var client = await CreateAuthenticatedClientAsync();
        var response = await client.GetAsync("/api/v1/reviews");
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
    }

    [Fact]
    public async Task تعليم_حركة_مخزون_غير_موجودة_كمُراجَعة_يرجّع_404()
    {
        var client = await CreateAuthenticatedClientAsync();
        var response = await client.PostAsync($"/api/v1/reviews/stock-movements/{Guid.NewGuid()}/mark-reviewed", content: null);
        Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
    }
}
