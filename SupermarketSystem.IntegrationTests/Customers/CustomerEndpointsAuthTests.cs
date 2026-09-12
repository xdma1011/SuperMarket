using System.Net;
using System.Net.Http.Json;
using Xunit;

namespace SupermarketSystem.IntegrationTests.Customers;

/// <summary>
/// يفحص التمييز الموثَّق بتعليقات CustomerEndpoints.cs بدقة: أي endpoint
/// AllowAnonymous فعلي (شكوى، qr-token، توكن جهاز، رصيد ولاء - كلها
/// "مؤقتة بلا تحقق هوية حقيقي" بتصميم صريح) لازم يرجع 200/404 بلا توكن
/// (لا 401)، بينما مجموعة إدارة الزبائن (CustomersManage) واقعيًا محمية.
/// </summary>
[Collection(DatabaseCollection.Name)]
public sealed class CustomerEndpointsAuthTests : IntegrationTestBase
{
    public CustomerEndpointsAuthTests(DatabaseFixture fixture) : base(fixture) { }

    [Fact]
    public async Task قائمة_الزبائن_محمية_401_بلا_توكن_ومسموحة_بتوكن_صحيح()
    {
        var anonymous = CreateAnonymousClient();
        var anonymousResponse = await anonymous.GetAsync("/api/v1/customers");
        Assert.Equal(HttpStatusCode.Unauthorized, anonymousResponse.StatusCode);

        var authenticated = await CreateAuthenticatedClientAsync();
        var authenticatedResponse = await authenticated.GetAsync("/api/v1/customers");
        Assert.Equal(HttpStatusCode.OK, authenticatedResponse.StatusCode);
    }

    [Fact]
    public async Task رمز_QR_لزبون_غير_موجود_يرجع_404_بلا_توكن_لا_401()
    {
        var client = CreateAnonymousClient();
        var response = await client.GetAsync($"/api/v1/customers/{Guid.NewGuid()}/qr-token");
        Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
    }

    [Fact]
    public async Task تسجيل_شكوى_بلا_توكن_يرجع_400_لا_401_لأنه_AllowAnonymous()
    {
        var client = CreateAnonymousClient();
        var response = await client.PostAsJsonAsync("/api/v1/complaints", new
        {
            CustomerId = Guid.NewGuid(),
            OrderId = (Guid?)null,
            Text = ""
        });

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
    }

    [Fact]
    public async Task مسح_باركود_الزبون_محمي_بصلاحية_Sales_Create_401_بلا_توكن()
    {
        var client = CreateAnonymousClient();
        var response = await client.PostAsJsonAsync("/api/v1/customers/resolve-qr", new { QrToken = "x" });
        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
    }
}
