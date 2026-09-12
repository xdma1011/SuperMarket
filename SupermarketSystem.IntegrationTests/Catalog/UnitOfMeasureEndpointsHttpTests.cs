using System.Net;
using System.Net.Http.Json;
using Xunit;

namespace SupermarketSystem.IntegrationTests.Catalog;

[Collection(DatabaseCollection.Name)]
public sealed class UnitOfMeasureEndpointsHttpTests : IntegrationTestBase
{
    public UnitOfMeasureEndpointsHttpTests(DatabaseFixture fixture) : base(fixture) { }

    [Fact]
    public async Task جلب_وحدات_القياس_بلا_توكن_يرجع_401()
    {
        var client = CreateAnonymousClient();

        var response = await client.GetAsync("/api/v1/units-of-measure");

        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
    }

    [Fact]
    public async Task إنشاء_وحدة_قياس_عبر_HTTP_بتوكن_صحيح_يرجع_201()
    {
        var client = await CreateAuthenticatedClientAsync();
        var name = $"وحدة-{Guid.NewGuid():N}"[..15];

        var response = await client.PostAsJsonAsync("/api/v1/units-of-measure", new { Name = name });

        Assert.Equal(HttpStatusCode.Created, response.StatusCode);

        var listResponse = await client.GetAsync("/api/v1/units-of-measure");
        Assert.Equal(HttpStatusCode.OK, listResponse.StatusCode);
    }
}
