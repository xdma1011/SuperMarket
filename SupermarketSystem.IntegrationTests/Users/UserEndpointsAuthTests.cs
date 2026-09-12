using System.Net;
using Xunit;

namespace SupermarketSystem.IntegrationTests.Users;

[Collection(DatabaseCollection.Name)]
public sealed class UserEndpointsAuthTests : IntegrationTestBase
{
    public UserEndpointsAuthTests(DatabaseFixture fixture) : base(fixture) { }

    [Fact]
    public async Task مجموعة_المستخدمين_محمية_401_بلا_توكن_ومسموحة_بتوكن_صحيح()
    {
        var anonymous = CreateAnonymousClient();
        var anonymousResponse = await anonymous.GetAsync("/api/v1/users");
        Assert.Equal(HttpStatusCode.Unauthorized, anonymousResponse.StatusCode);

        var authenticated = await CreateAuthenticatedClientAsync();
        var authenticatedResponse = await authenticated.GetAsync("/api/v1/users");
        Assert.Equal(HttpStatusCode.OK, authenticatedResponse.StatusCode);
    }

    [Fact]
    public async Task كاشير_بلا_صلاحية_Users_Manage_يُرفض_بـ403()
    {
        var (_, username) = await UsersTestDataHelper.CreateUserWithRoleReturningUsernameAsync(
            Fixture.Factory.Services, Fixture.TestBranchId, UsersTestDataHelper.CashierRoleId, "cashier.usersendpoint.test");

        var cashierClient = await LoginHelper.LoginAsAsync(Fixture, username, UsersTestDataHelper.DefaultPassword);

        var response = await cashierClient.GetAsync("/api/v1/users");
        Assert.Equal(HttpStatusCode.Forbidden, response.StatusCode);
    }
}
