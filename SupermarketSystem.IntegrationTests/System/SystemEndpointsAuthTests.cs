using System.Net;
using Xunit;

namespace SupermarketSystem.IntegrationTests.SystemDomain;

[Collection(DatabaseCollection.Name)]
public sealed class SystemEndpointsAuthTests : IntegrationTestBase
{
    public SystemEndpointsAuthTests(DatabaseFixture fixture) : base(fixture) { }

    [Fact]
    public async Task إعدادات_الإدارة_محمية_401_بلا_توكن_ومسموحة_بتوكن_صحيح()
    {
        var anonymous = CreateAnonymousClient();
        var anonymousResponse = await anonymous.GetAsync("/api/v1/system/admin-settings");
        Assert.Equal(HttpStatusCode.Unauthorized, anonymousResponse.StatusCode);

        var authenticated = await CreateAuthenticatedClientAsync();
        var authenticatedResponse = await authenticated.GetAsync("/api/v1/system/admin-settings");
        Assert.Equal(HttpStatusCode.OK, authenticatedResponse.StatusCode);
    }

    [Fact]
    public async Task كاشير_بلا_صلاحية_System_SettingsManage_يُرفض_بـ403()
    {
        var (_, username) = await UsersTestDataHelper.CreateUserWithRoleReturningUsernameAsync(
            Fixture.Factory.Services, Fixture.TestBranchId, UsersTestDataHelper.CashierRoleId, "cashier.systemendpoint.test");

        var cashierClient = await LoginHelper.LoginAsAsync(Fixture, username, UsersTestDataHelper.DefaultPassword);

        var response = await cashierClient.GetAsync("/api/v1/system/admin-settings");
        Assert.Equal(HttpStatusCode.Forbidden, response.StatusCode);
    }

    [Fact]
    public async Task الأسرار_محمية_401_بلا_توكن()
    {
        var client = CreateAnonymousClient();
        var response = await client.GetAsync("/api/v1/system/secrets");
        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
    }
}
