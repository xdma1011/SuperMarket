using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using Xunit;

namespace SupermarketSystem.IntegrationTests.Authentication;

/// <summary>
/// UserSession ليس كيانًا IBranchOwned - بس هذه الاختبارات عبر HTTP فعليًا
/// (لا Handler مباشر) لأنها بطبيعتها تدفّق تسجيل دخول/تجديد/إبطال حقيقي
/// (RefreshToken/Logout يحدّدان الجلسة من بصمة التوكن المُرسَل فعليًا -
/// محتاج توكن حقيقي صادر من /auth/login، لا قيمة مصطنعة). يغطّي:
/// RefreshToken (Application/Authentication/RefreshToken)، RevokeSession،
/// GetActiveSessions، Logout، GetMyPermissions - كل واحد Login نفسه
/// مغطّى بالكامل بمثال LoginEndpointTests الموجود مسبقًا.
/// </summary>
[Collection(DatabaseCollection.Name)]
public sealed class SessionManagementTests : IntegrationTestBase
{
    public SessionManagementTests(DatabaseFixture fixture) : base(fixture) { }

    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web);

    private async Task<(string AccessToken, string RefreshToken)> LoginRawTokensAsync(string username, string password)
    {
        var client = Fixture.Factory.CreateClient();
        var response = await client.PostAsJsonAsync("/api/v1/auth/login", new
        {
            Username = username,
            Password = password,
            AppType = "Admin",
            BranchId = Fixture.TestBranchId
        });
        response.EnsureSuccessStatusCode();
        var json = JsonDocument.Parse(await response.Content.ReadAsStringAsync()).RootElement;
        return (json.GetProperty("accessToken").GetString()!, json.GetProperty("refreshToken").GetString()!);
    }

    [Fact]
    public async Task تجديد_التوكن_ينجح_ويصدر_زوجًا_جديدًا_والتوكن_القديم_يصير_غير_صالح()
    {
        var (_, refreshToken) = await LoginRawTokensAsync(DatabaseFixture.AdminUsername, DatabaseFixture.AdminPassword);
        var client = Fixture.Factory.CreateClient();

        var firstRefresh = await client.PostAsJsonAsync("/api/v1/auth/refresh", new { RefreshToken = refreshToken });
        Assert.Equal(HttpStatusCode.OK, firstRefresh.StatusCode);

        var firstJson = JsonDocument.Parse(await firstRefresh.Content.ReadAsStringAsync()).RootElement;
        var newRefreshToken = firstJson.GetProperty("refreshToken").GetString();
        Assert.False(string.IsNullOrWhiteSpace(newRefreshToken));
        Assert.NotEqual(refreshToken, newRefreshToken);

        // نفس التوكن القديم بالضبط - لازم يُرفض هلأ (rotation - راجع UserSession.Rotate).
        var secondRefreshWithOldToken = await client.PostAsJsonAsync("/api/v1/auth/refresh", new { RefreshToken = refreshToken });
        Assert.Equal(HttpStatusCode.Forbidden, secondRefreshWithOldToken.StatusCode);

        // التوكن الجديد لسه صالح.
        var refreshWithNewToken = await client.PostAsJsonAsync("/api/v1/auth/refresh", new { RefreshToken = newRefreshToken });
        Assert.Equal(HttpStatusCode.OK, refreshWithNewToken.StatusCode);
    }

    [Fact]
    public async Task تجديد_بتوكن_غير_موجود_أو_فاضي_يفشل_بـ403()
    {
        var client = Fixture.Factory.CreateClient();

        var bogus = await client.PostAsJsonAsync("/api/v1/auth/refresh", new { RefreshToken = "not-a-real-token" });
        Assert.Equal(HttpStatusCode.Forbidden, bogus.StatusCode);

        var empty = await client.PostAsJsonAsync("/api/v1/auth/refresh", new { RefreshToken = "" });
        Assert.Equal(HttpStatusCode.Forbidden, empty.StatusCode);
    }

    [Fact]
    public async Task تعطيل_المستخدم_يبطل_تجديد_جلسته_بأقصى_عمر_توكن_الوصول()
    {
        var (userId, username) = await UsersTestDataHelper.CreateUserWithRoleReturningUsernameAsync(
            Fixture.Factory.Services, Fixture.TestBranchId, UsersTestDataHelper.CashierRoleId, "session.deactivate.test");

        var (_, refreshToken) = await LoginRawTokensAsync(username, UsersTestDataHelper.DefaultPassword);

        using (var scope = CreateScope())
        {
            var db = CreateDbContext(scope);
            var user = await db.Users.IgnoreQueryFilters().FirstAsync(u => u.Id == userId);
            user.Deactivate();
            await db.SaveChangesAsync();
        }

        var client = Fixture.Factory.CreateClient();
        var response = await client.PostAsJsonAsync("/api/v1/auth/refresh", new { RefreshToken = refreshToken });

        // راجع تعليق RefreshTokenHandler: فحص User.IsActive وقت التجديد
        // نفسه هو خط الدفاع - لازم يُرفض فورًا، لا يضل صالحًا لعمر الجلسة كله.
        Assert.Equal(HttpStatusCode.Forbidden, response.StatusCode);
    }

    [Fact]
    public async Task إبطال_جلسة_إداريًا_يمنع_تجديدها_لاحقًا_وإعادة_إبطالها_يفشل_بـ409()
    {
        var (userId, username) = await UsersTestDataHelper.CreateUserWithRoleReturningUsernameAsync(
            Fixture.Factory.Services, Fixture.TestBranchId, UsersTestDataHelper.CashierRoleId, "session.revoke.test");

        var (_, refreshToken) = await LoginRawTokensAsync(username, UsersTestDataHelper.DefaultPassword);

        Guid sessionId;
        using (var scope = CreateScope())
        {
            var db = CreateDbContext(scope);
            sessionId = (await db.UserSessions.FirstAsync(s => s.UserId == userId)).Id;
        }

        var adminClient = await CreateAuthenticatedClientAsync();

        // ملاحظة: الـendpoint موثَّق بـ.Produces(200) بـAuthenticationEndpoints.cs
        // بس ResultExtensions.ToHttpResult(Result) الفعلي بيرجّع 204 دومًا
        // لنجاح Result بلا قيمة - توثيق Swagger غير مطابق للسلوك الفعلي
        // (فرق تافه بالتوثيق فقط، لا يؤثر على وظيفة الـendpoint).
        var revokeResponse = await adminClient.PostAsync($"/api/v1/auth/sessions/{sessionId}/revoke", content: null);
        Assert.Equal(HttpStatusCode.NoContent, revokeResponse.StatusCode);

        var revokeAgainResponse = await adminClient.PostAsync($"/api/v1/auth/sessions/{sessionId}/revoke", content: null);
        Assert.Equal(HttpStatusCode.Conflict, revokeAgainResponse.StatusCode);

        var refreshAfterRevokeClient = Fixture.Factory.CreateClient();
        var refreshResponse = await refreshAfterRevokeClient.PostAsJsonAsync("/api/v1/auth/refresh", new { RefreshToken = refreshToken });
        Assert.Equal(HttpStatusCode.Forbidden, refreshResponse.StatusCode);
    }

    [Fact]
    public async Task إبطال_جلسة_غير_موجودة_يفشل_بـ404()
    {
        var adminClient = await CreateAuthenticatedClientAsync();
        var response = await adminClient.PostAsync($"/api/v1/auth/sessions/{Guid.NewGuid()}/revoke", content: null);
        Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
    }

    [Fact]
    public async Task قائمة_الجلسات_الفعّالة_تعرض_جلسة_جديدة_وتستثنيها_بعد_إبطالها()
    {
        var (userId, username) = await UsersTestDataHelper.CreateUserWithRoleReturningUsernameAsync(
            Fixture.Factory.Services, Fixture.TestBranchId, UsersTestDataHelper.CashierRoleId, "session.list.test");

        await LoginRawTokensAsync(username, UsersTestDataHelper.DefaultPassword);

        var adminClient = await CreateAuthenticatedClientAsync();
        var listResponse = await adminClient.GetAsync($"/api/v1/auth/sessions?userId={userId}");
        listResponse.EnsureSuccessStatusCode();

        var json = JsonDocument.Parse(await listResponse.Content.ReadAsStringAsync()).RootElement;
        var items = json.GetProperty("items").EnumerateArray().ToList();
        Assert.Single(items);

        var sessionId = items[0].GetProperty("sessionId").GetGuid();
        await adminClient.PostAsync($"/api/v1/auth/sessions/{sessionId}/revoke", content: null);

        var afterRevokeResponse = await adminClient.GetAsync($"/api/v1/auth/sessions?userId={userId}");
        var afterJson = JsonDocument.Parse(await afterRevokeResponse.Content.ReadAsStringAsync()).RootElement;
        Assert.Empty(afterJson.GetProperty("items").EnumerateArray());
    }

    [Fact]
    public async Task مجموعة_إدارة_الجلسات_محمية_401_بلا_توكن()
    {
        var client = CreateAnonymousClient();
        var response = await client.GetAsync("/api/v1/auth/sessions");
        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
    }

    [Fact]
    public async Task خروج_يبطل_الجلسة_والخروج_مرتين_عملية_هادئة_دومًا()
    {
        var (_, refreshToken) = await LoginRawTokensAsync(DatabaseFixture.AdminUsername, DatabaseFixture.AdminPassword);
        var client = Fixture.Factory.CreateClient();

        var firstLogout = await client.PostAsJsonAsync("/api/v1/auth/logout", new { RefreshToken = refreshToken });
        Assert.Equal(HttpStatusCode.NoContent, firstLogout.StatusCode);

        // خروج ثانٍ بنفس التوكن (مُبطَل أصلًا) - يضل 204 بهدوء (idempotent - راجع LogoutHandler).
        var secondLogout = await client.PostAsJsonAsync("/api/v1/auth/logout", new { RefreshToken = refreshToken });
        Assert.Equal(HttpStatusCode.NoContent, secondLogout.StatusCode);

        // خروج بتوكن غير موجود إطلاقًا/فاضي - نفس الهدوء.
        var bogusLogout = await client.PostAsJsonAsync("/api/v1/auth/logout", new { RefreshToken = "no-such-token" });
        Assert.Equal(HttpStatusCode.NoContent, bogusLogout.StatusCode);

        // الجلسة صارت مُبطَلة فعليًا - تجديدها لازم يفشل.
        var refreshResponse = await client.PostAsJsonAsync("/api/v1/auth/refresh", new { RefreshToken = refreshToken });
        Assert.Equal(HttpStatusCode.Forbidden, refreshResponse.StatusCode);
    }

    [Fact]
    public async Task صلاحياتي_تُرجع_رموز_دور_المستخدم_الحالي_ومحمية_401_بلا_توكن()
    {
        var anonymous = CreateAnonymousClient();
        var anonymousResponse = await anonymous.GetAsync("/api/v1/auth/my-permissions");
        Assert.Equal(HttpStatusCode.Unauthorized, anonymousResponse.StatusCode);

        var (_, username) = await UsersTestDataHelper.CreateUserWithRoleReturningUsernameAsync(
            Fixture.Factory.Services, Fixture.TestBranchId, UsersTestDataHelper.CashierRoleId, "mypermissions.test");
        var cashierClient = await LoginHelper.LoginAsAsync(Fixture, username, UsersTestDataHelper.DefaultPassword);

        var response = await cashierClient.GetAsync("/api/v1/auth/my-permissions");
        response.EnsureSuccessStatusCode();

        var json = JsonDocument.Parse(await response.Content.ReadAsStringAsync()).RootElement;
        var codes = json.GetProperty("permissionCodes").EnumerateArray().Select(c => c.GetString()).ToList();

        Assert.Contains("Sales.Create", codes);
        Assert.DoesNotContain("Users.Manage", codes);
    }
}
