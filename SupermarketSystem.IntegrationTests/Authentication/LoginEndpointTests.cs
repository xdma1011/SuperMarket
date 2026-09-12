using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using Xunit;

namespace SupermarketSystem.IntegrationTests.Authentication;

/// <summary>
/// اختبار endpoint حقيقي كامل عبر HttpClient (لا استدعاء Handler مباشر) —
/// يمشي فعليًا خلال كل الـmiddleware pipeline (CORS، Auth، Exception
/// handling) بالضبط متل طلب حقيقي من الفرونت إند.
/// </summary>
[Collection(DatabaseCollection.Name)]
public sealed class LoginEndpointTests : IntegrationTestBase
{
    public LoginEndpointTests(DatabaseFixture fixture) : base(fixture) { }

    [Fact]
    public async Task دخول_ببيانات_صحيحة_يرجع_200_وتوكن()
    {
        var client = CreateAnonymousClient();

        var response = await client.PostAsJsonAsync("/api/v1/auth/login", new
        {
            Username = DatabaseFixture.AdminUsername,
            Password = DatabaseFixture.AdminPassword,
            AppType = "Admin",
            BranchId = Fixture.TestBranchId
        });

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);

        var body = await response.Content.ReadFromJsonAsync<JsonElement>();
        Assert.False(string.IsNullOrWhiteSpace(body.GetProperty("accessToken").GetString()));
    }

    [Fact]
    public async Task دخول_بكلمة_سر_غلط_يرجع_403_برسالة_عامة()
    {
        var client = CreateAnonymousClient();

        var response = await client.PostAsJsonAsync("/api/v1/auth/login", new
        {
            Username = DatabaseFixture.AdminUsername,
            Password = "كلمة سر غلط",
            AppType = "Admin",
            BranchId = Fixture.TestBranchId
        });

        Assert.Equal(HttpStatusCode.Forbidden, response.StatusCode);
    }

    [Fact]
    public async Task دخول_باسم_مستخدم_غير_موجود_يرجع_نفس_رسالة_كلمة_السر_الغلط()
    {
        // مبدأ حاكم بالمشروع (راجع تعليق LoginHandler): رسالة فشل واحدة
        // لكل الأسباب، حتى لا يقدر مهاجم يعرف اسم مستخدم صحيح من غلط.
        var client = CreateAnonymousClient();

        var wrongPasswordResponse = await client.PostAsJsonAsync("/api/v1/auth/login", new
        {
            Username = DatabaseFixture.AdminUsername,
            Password = "غلط",
            AppType = "Admin",
            BranchId = Fixture.TestBranchId
        });

        var unknownUserResponse = await client.PostAsJsonAsync("/api/v1/auth/login", new
        {
            Username = "user_not_exists_at_all",
            Password = "غلط",
            AppType = "Admin",
            BranchId = (Guid?)null
        });

        Assert.Equal(wrongPasswordResponse.StatusCode, unknownUserResponse.StatusCode);

        var wrongPasswordBody = await wrongPasswordResponse.Content.ReadFromJsonAsync<JsonElement>();
        var unknownUserBody = await unknownUserResponse.Content.ReadFromJsonAsync<JsonElement>();

        Assert.Equal(
            wrongPasswordBody.GetProperty("detail").GetString(),
            unknownUserBody.GetProperty("detail").GetString());
    }

    [Fact]
    public async Task endpoint_محمي_بلا_توكن_يرجع_401()
    {
        var client = CreateAnonymousClient();

        var response = await client.GetAsync("/api/v1/products");

        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
    }

    [Fact]
    public async Task endpoint_محمي_بتوكن_صحيح_يرجع_200()
    {
        var client = await CreateAuthenticatedClientAsync();

        var response = await client.GetAsync("/api/v1/products");

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
    }
}
