using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text.Json;
using SupermarketSystem.IntegrationTests.Common;
using Xunit;

namespace SupermarketSystem.IntegrationTests.CashManagement;

/// <summary>
/// زر "فتح الصندوق" (28/9/2026): الكاشير بيسجّل فتح الدرج بلا بيع لفرعه بس، والإدارة بتشوف كم مرة
/// لكل كاشير بتقرير. تسجيل بس، بلا أي أثر على المبالغ.
/// </summary>
[Collection(DatabaseCollection.Name)]
public sealed class DrawerOpenEventsHttpTests : IntegrationTestBase
{
    public DrawerOpenEventsHttpTests(DatabaseFixture fixture) : base(fixture) { }

    private static string Iso(DateTime value) => Uri.EscapeDataString(value.ToString("o"));

    private async Task<HttpClient> CashierAsync(string prefix)
    {
        var (_, username) = await UsersTestDataHelper.CreateUserWithRoleReturningUsernameAsync(
            Fixture.Factory.Services, Fixture.TestBranchId, UsersTestDataHelper.CashierRoleId, prefix);
        var client = Fixture.Factory.CreateClient();
        var login = await client.PostAsJsonAsync("/api/v1/auth/login", new
        {
            username, password = UsersTestDataHelper.DefaultPassword, appType = 1,
            branchId = (Guid?)null, ipAddress = (string?)null, deviceInfo = "CASHIER-PC"
        });
        var body = await login.Content.ReadAsStringAsync();
        Assert.True(login.IsSuccessStatusCode, body);
        client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue(
            "Bearer", JsonDocument.Parse(body).RootElement.GetProperty("accessToken").GetString());
        return client;
    }

    [Fact]
    public async Task الكاشير_بيسجّل_فتح_الصندوق_لفرعه_والتقرير_بيعدّ_المرات()
    {
        var cashier = await CashierAsync("drawer.open");

        for (var i = 0; i < 2; i++)
        {
            var response = await cashier.PostAsJsonAsync("/api/v1/cash-drawer/open-events",
                new { branchId = Fixture.TestBranchId, reason = i == 0 ? "فكّة لزبون" : null });
            Assert.Equal(HttpStatusCode.Created, response.StatusCode);
        }

        // فرع غير فرعه - ممنوع.
        var otherBranch = await cashier.PostAsJsonAsync("/api/v1/cash-drawer/open-events",
            new { branchId = Guid.NewGuid(), reason = (string?)null });
        Assert.Equal(HttpStatusCode.Forbidden, otherBranch.StatusCode);

        // الكاشير ما بيشوف التقارير.
        var range = $"branchId={Fixture.TestBranchId}&fromUtc={Iso(DateTime.UtcNow.AddHours(-1))}&toUtc={Iso(DateTime.UtcNow.AddHours(1))}";
        var cashierReport = await cashier.GetAsync($"/api/v1/reports/cashiers/drawer-opens?{range}");
        Assert.Equal(HttpStatusCode.Forbidden, cashierReport.StatusCode);

        var admin = await CreateAuthenticatedClientAsync();
        var report = await admin.GetAsync($"/api/v1/reports/cashiers/drawer-opens?{range}");
        var reportBody = await report.Content.ReadAsStringAsync();
        Assert.Equal(HttpStatusCode.OK, report.StatusCode);

        var row = JsonDocument.Parse(reportBody).RootElement.GetProperty("items").EnumerateArray()
            .Single(r => r.GetProperty("username").GetString()!.StartsWith("drawer.open"));
        Assert.Equal(2, row.GetProperty("openCount").GetInt32());
    }
}
