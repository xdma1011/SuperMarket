using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using Xunit;

namespace SupermarketSystem.IntegrationTests.CashierSync;

/// <summary>
/// إشعار فقط (لا سجل بزنس) لما الكاشير يحذف بيع أوفلاين معلَّق محليًا
/// قبل ما يوصل السيرفر - راجع تعليق ReportDiscardedPendingSaleCommand.cs
/// للسياق الكامل (فجوة حقيقية مكتشفة: الحذف المحلي كان صفر أثر).
/// </summary>
[Collection(DatabaseCollection.Name)]
public sealed class ReportDiscardedPendingSaleTests : IntegrationTestBase
{
    public ReportDiscardedPendingSaleTests(DatabaseFixture fixture) : base(fixture) { }

    [Fact]
    public async Task إشعار_حذف_بيع_معلَّق_ينجح_ويرجع_تأكيد_بلا_أي_سجل_بزنس()
    {
        var client = await CreateAuthenticatedClientAsync();

        var response = await client.PostAsJsonAsync("/api/v1/cashier-sync/report-discarded-pending-sale", new
        {
            ClientRequestId = Guid.NewGuid(),
            BranchId = Fixture.TestBranchId,
            CreatedAtLocal = DateTime.UtcNow.AddMinutes(-10),
            AttemptCount = 3,
            LastErrorMessage = "Sale.ProductNotActive"
        });

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);

        var body = await response.Content.ReadFromJsonAsync<JsonElement>();
        Assert.True(body.GetProperty("acknowledged").GetBoolean());
    }

    [Fact]
    public async Task إشعار_حذف_بيع_معلَّق_بفرع_غير_موجود_ينجح_برسالة_احتياطية()
    {
        // الـHandler ما بيرفض فرع غير موجود - بيستخدم اسم احتياطي ويكمل
        // الإشعار، لأن الهدف بلغ صاحب المشروع حتى لو الفرع اتحذف لاحقًا.
        var client = await CreateAuthenticatedClientAsync();

        var response = await client.PostAsJsonAsync("/api/v1/cashier-sync/report-discarded-pending-sale", new
        {
            ClientRequestId = Guid.NewGuid(),
            BranchId = Guid.NewGuid(),
            CreatedAtLocal = DateTime.UtcNow,
            AttemptCount = 1,
            LastErrorMessage = (string?)null
        });

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
    }

    [Fact]
    public async Task endpoint_محمي_بلا_توكن_يرجع_401()
    {
        var client = CreateAnonymousClient();

        var response = await client.PostAsJsonAsync("/api/v1/cashier-sync/report-discarded-pending-sale", new
        {
            ClientRequestId = Guid.NewGuid(),
            BranchId = Fixture.TestBranchId,
            CreatedAtLocal = DateTime.UtcNow,
            AttemptCount = 1,
            LastErrorMessage = (string?)null
        });

        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
    }
}
