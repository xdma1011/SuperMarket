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
        Assert.Equal(1, row.GetProperty("withoutReasonCount").GetInt32());
        Assert.Equal("فكّة لزبون", row.GetProperty("recentReasons").GetString());
    }

    /// <summary>
    /// بلا نت (28/9/2026): الكاشير بيحفظ الفتحة محليًا وبيبعتها لاحقًا بوقتها الفعلي ومفتاح فريد - إعادة الإرسال
    /// بترجّع نفس السجل (200) بلا تكرار، والوقت غير المعقول (بالمستقبل) بيتسجّل بوقت الوصول بدل ما ينرفض.
    /// </summary>
    [Fact]
    public async Task فتحة_محفوظة_بلا_نت_بتنسجّل_بوقتها_الفعلي_ومرة_وحدة_بس()
    {
        var cashier = await CashierAsync("drawer.offline");
        var requestId = Guid.NewGuid();
        var openedAt = DateTime.UtcNow.AddHours(-3);

        var first = await cashier.PostAsJsonAsync("/api/v1/cash-drawer/open-events",
            new { branchId = Fixture.TestBranchId, reason = "تبديل عملة", clientRequestId = requestId, occurredAtUtc = openedAt });
        Assert.Equal(HttpStatusCode.Created, first.StatusCode);
        var firstBody = JsonDocument.Parse(await first.Content.ReadAsStringAsync()).RootElement;
        Assert.True(Math.Abs((firstBody.GetProperty("occurredAtUtc").GetDateTime().ToUniversalTime() - openedAt).TotalSeconds) < 1,
            "لازم ينسجّل وقت الفتح الفعلي عند الكاشير، مش وقت الوصول");

        // إعادة إرسال (المزامنة الخلفية بعد ما رجع النت) - نفس السجل، بلا تكرار.
        var replay = await cashier.PostAsJsonAsync("/api/v1/cash-drawer/open-events",
            new { branchId = Fixture.TestBranchId, reason = "تبديل عملة", clientRequestId = requestId, occurredAtUtc = openedAt });
        Assert.Equal(HttpStatusCode.OK, replay.StatusCode);
        var replayBody = JsonDocument.Parse(await replay.Content.ReadAsStringAsync()).RootElement;
        Assert.Equal(firstBody.GetProperty("drawerOpenEventId").GetGuid(), replayBody.GetProperty("drawerOpenEventId").GetGuid());
        Assert.True(replayBody.GetProperty("wasReplay").GetBoolean());

        // ساعة الجهاز غلط (بكرا) - بينسجّل بوقت الوصول، ما بينرفض (والا بتضل معلّقة بالكاشير للأبد).
        var future = await cashier.PostAsJsonAsync("/api/v1/cash-drawer/open-events",
            new { branchId = Fixture.TestBranchId, reason = (string?)null, clientRequestId = Guid.NewGuid(), occurredAtUtc = DateTime.UtcNow.AddDays(1) });
        Assert.Equal(HttpStatusCode.Created, future.StatusCode);
        var futureAt = JsonDocument.Parse(await future.Content.ReadAsStringAsync()).RootElement.GetProperty("occurredAtUtc").GetDateTime().ToUniversalTime();
        Assert.True(futureAt <= DateTime.UtcNow.AddMinutes(1));

        // التقرير: الفتحة المتأخرة بتنعدّ بيومها الفعلي، مرة وحدة.
        var admin = await CreateAuthenticatedClientAsync();
        var range = $"branchId={Fixture.TestBranchId}&fromUtc={Iso(DateTime.UtcNow.AddHours(-4))}&toUtc={Iso(DateTime.UtcNow.AddHours(1))}";
        var reportBody = await (await admin.GetAsync($"/api/v1/reports/cashiers/drawer-opens?{range}")).Content.ReadAsStringAsync();
        var row = JsonDocument.Parse(reportBody).RootElement.GetProperty("items").EnumerateArray()
            .Single(r => r.GetProperty("username").GetString()!.StartsWith("drawer.offline"));
        Assert.Equal(2, row.GetProperty("openCount").GetInt32());
        Assert.Equal(1, row.GetProperty("withoutReasonCount").GetInt32());
    }
}
