using System.Net;
using System.Net.Http.Json;
using Xunit;

namespace SupermarketSystem.IntegrationTests.Branches;

[Collection(DatabaseCollection.Name)]
public sealed class BranchEndpointsHttpTests : IntegrationTestBase
{
    public BranchEndpointsHttpTests(DatabaseFixture fixture) : base(fixture) { }

    [Fact]
    public async Task جلب_الفروع_بلا_توكن_يرجع_401()
    {
        var client = CreateAnonymousClient();

        var response = await client.GetAsync("/api/v1/branches");

        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
    }

    [Fact]
    public async Task إنشاء_فرع_عبر_HTTP_بتوكن_صحيح_يرجع_201()
    {
        var client = await CreateAuthenticatedClientAsync();
        var code = $"HTTP{Guid.NewGuid():N}"[..12];

        var response = await client.PostAsJsonAsync("/api/v1/branches", new
        {
            Name = "فرع عبر HTTP",
            Code = code,
            PhoneNumber = (string?)null,
            Street = (string?)null,
            City = (string?)null,
            PostalCode = (string?)null,
            Country = (string?)null
        });

        Assert.Equal(HttpStatusCode.Created, response.StatusCode);
        Assert.NotNull(response.Headers.Location);
    }
}
