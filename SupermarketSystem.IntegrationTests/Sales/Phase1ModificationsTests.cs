using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using SupermarketSystem.Application.Sales.CompleteSale;
using SupermarketSystem.Infrastructure.Persistence;
using SupermarketSystem.IntegrationTests.Common;
using Xunit;

namespace SupermarketSystem.IntegrationTests.Sales;

/// <summary>
/// المرحلة 1 من التعديلات (8/10/2026): بند 24 (بيعات مرفوضة)، بند 23 (تفسير فرق التقفيل + المعلّق)، بند 22 (وقت البيعة الأوفلاين).
/// </summary>
[Collection(DatabaseCollection.Name)]
public sealed class Phase1ModificationsTests : IntegrationTestBase
{
    public Phase1ModificationsTests(DatabaseFixture fixture) : base(fixture) { }

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

    // ───────── بند 24 ─────────

    [Fact]
    public async Task بيعة_مرفوضة_بتنسجّل_وإعادة_المحاولة_بتحدّث_نفس_السجل_والإدارة_بتعالجها()
    {
        var cashier = await CashierAsync("rejected.sale");
        var clientRequestId = Guid.NewGuid();
        var payload = new
        {
            branchId = Fixture.TestBranchId,
            clientRequestId,
            invoiceLevelDiscountAmount = 0m,
            items = new[] { new { productId = Guid.NewGuid(), productUnitId = Guid.NewGuid(), quantity = 1m, manualDiscountAmount = 0m } },
            payments = new[] { new { paymentMethodId = TestDataBuilder.CashPaymentMethodId, amount = 5m, clientRequestId = Guid.NewGuid() } }
        };

        var first = await cashier.PostAsJsonAsync("/api/v1/sales", payload);
        Assert.Equal(HttpStatusCode.NotFound, first.StatusCode);
        var retry = await cashier.PostAsJsonAsync("/api/v1/sales", payload);
        Assert.Equal(HttpStatusCode.NotFound, retry.StatusCode);

        // الكاشير ما بيشوف قائمة المرفوضة.
        Assert.Equal(HttpStatusCode.Forbidden, (await cashier.GetAsync("/api/v1/sales/rejected")).StatusCode);

        var admin = await CreateAuthenticatedClientAsync();
        var list = await admin.GetAsync("/api/v1/sales/rejected");
        Assert.Equal(HttpStatusCode.OK, list.StatusCode);
        var row = JsonDocument.Parse(await list.Content.ReadAsStringAsync()).RootElement.GetProperty("items").EnumerateArray()
            .Single(r => r.GetProperty("clientRequestId").GetGuid() == clientRequestId);
        Assert.Equal(2, row.GetProperty("attemptCount").GetInt32());
        Assert.Equal("Sale.ProductNotFound", row.GetProperty("errorCode").GetString());
        Assert.Equal(5m, row.GetProperty("paidAmountHint").GetDecimal());
        var id = row.GetProperty("id").GetGuid();

        var details = await admin.GetAsync($"/api/v1/sales/rejected/{id}");
        Assert.Equal(HttpStatusCode.OK, details.StatusCode);
        var detailsJson = JsonDocument.Parse(await details.Content.ReadAsStringAsync()).RootElement;
        Assert.Equal(1, detailsJson.GetProperty("lines").GetArrayLength());
        Assert.Equal(1, detailsJson.GetProperty("payments").GetArrayLength());

        var resolve = await admin.PostAsJsonAsync($"/api/v1/sales/rejected/{id}/resolve", new { note = "دخلتها يدويًا" });
        Assert.Equal(HttpStatusCode.OK, resolve.StatusCode);
        Assert.Equal(HttpStatusCode.Conflict, (await admin.PostAsJsonAsync($"/api/v1/sales/rejected/{id}/resolve", new { note = (string?)null })).StatusCode);

        var openAfter = JsonDocument.Parse(await (await admin.GetAsync("/api/v1/sales/rejected")).Content.ReadAsStringAsync())
            .RootElement.GetProperty("items").EnumerateArray();
        Assert.DoesNotContain(openAfter, r => r.GetProperty("clientRequestId").GetGuid() == clientRequestId);
        var all = JsonDocument.Parse(await (await admin.GetAsync("/api/v1/sales/rejected?includeResolved=true")).Content.ReadAsStringAsync())
            .RootElement.GetProperty("items").EnumerateArray();
        Assert.Contains(all, r => r.GetProperty("clientRequestId").GetGuid() == clientRequestId && r.GetProperty("isResolved").GetBoolean());
    }

    // ───────── بند 23 ─────────

    [Fact]
    public async Task تفسير_فرق_التقفيل_بيحسب_المفسَّر_وغير_المفسَّر_والمعلّق_بينحفظ()
    {
        var cashier = await CashierAsync("closing.notes");
        var closing = await cashier.PostAsJsonAsync("/api/v1/cash-closings", new
        {
            branchId = Fixture.TestBranchId,
            businessDate = DateOnly.FromDateTime(DateTime.UtcNow),
            countedCash = 5m,
            countedDetails = Array.Empty<object>(),
            pendingSalesCount = 2,
            pendingSalesAmount = 7.5m
        });
        var closingBody = await closing.Content.ReadAsStringAsync();
        Assert.Equal(HttpStatusCode.Created, closing.StatusCode);
        var closingId = JsonDocument.Parse(closingBody).RootElement.GetProperty("cashClosingId").GetGuid();

        // الكاشير ما بيفسّر فرق نفسه.
        Assert.Equal(HttpStatusCode.Forbidden,
            (await cashier.PostAsJsonAsync($"/api/v1/cash-closings/{closingId}/variance-notes", new { reason = "LateSales", explainedAmount = 1m })).StatusCode);

        var admin = await CreateAuthenticatedClientAsync();
        var first = await admin.PostAsJsonAsync($"/api/v1/cash-closings/{closingId}/variance-notes",
            new { reason = "LateSales", explainedAmount = 3m, note = (string?)null });
        Assert.Equal(HttpStatusCode.OK, first.StatusCode);
        var firstJson = JsonDocument.Parse(await first.Content.ReadAsStringAsync()).RootElement;
        Assert.Equal(5m, firstJson.GetProperty("variance").GetDecimal());
        Assert.Equal(3m, firstJson.GetProperty("explainedTotal").GetDecimal());
        Assert.Equal(2m, firstJson.GetProperty("unexplainedVariance").GetDecimal());
        Assert.Equal(2, firstJson.GetProperty("pendingSalesCount").GetInt32());

        // أكبر من المتبقي - مرفوض (اتساق بيانات). "سبب آخر" بلا ملاحظة - مرفوض.
        Assert.Equal(HttpStatusCode.UnprocessableEntity,
            (await admin.PostAsJsonAsync($"/api/v1/cash-closings/{closingId}/variance-notes", new { reason = "CountError", explainedAmount = 3m })).StatusCode);
        Assert.Equal(HttpStatusCode.BadRequest,
            (await admin.PostAsJsonAsync($"/api/v1/cash-closings/{closingId}/variance-notes", new { reason = "Other", explainedAmount = 1m })).StatusCode);

        var list = await admin.GetAsync("/api/v1/cash-closings?pageSize=50");
        var item = JsonDocument.Parse(await list.Content.ReadAsStringAsync()).RootElement.GetProperty("items").EnumerateArray()
            .Single(r => r.GetProperty("id").GetGuid() == closingId);
        Assert.Equal(2, item.GetProperty("pendingSalesCount").GetInt32());
        Assert.Equal(7.5m, item.GetProperty("pendingSalesAmount").GetDecimal());
        Assert.Equal(3m, item.GetProperty("explainedVariance").GetDecimal());
        Assert.Equal(2m, item.GetProperty("unexplainedVariance").GetDecimal());
    }

    // ───────── بند 22 ─────────

    private async Task<(Guid ProductId, Guid UnitId)> SeedSellableProductAsync(AppDbContext db)
    {
        var (product, unit) = await TestDataBuilder.CreateActiveProductAsync(db, "منتج للوقت");
        await TestDataBuilder.CreateProductBranchAsync(db, product.Id, Fixture.TestBranchId, 10m);
        await TestDataBuilder.SetStockAsync(db, product.Id, Fixture.TestBranchId, 100m);
        return (product.Id, unit.Id);
    }

    [Fact]
    public async Task وقت_البيعة_الأوفلاين_بينختم_على_الفاتورة_والمشبوه_بيتعلّم()
    {
        using var scope = CreateScope();
        await TestDataBuilder.ActAsAdminAsync(scope, Fixture);
        var db = CreateDbContext(scope);
        var (productId, unitId) = await SeedSellableProductAsync(db);
        var handler = scope.ServiceProvider.GetRequiredService<CompleteSaleHandler>();

        async Task<(CompleteSaleResponse Response, DateTime CreatedAt, DateTime? ReceivedAt)> SellAsync(DateTime? occurredAtUtc)
        {
            var result = await handler.HandleAsync(new CompleteSaleCommand(
                Fixture.TestBranchId, Guid.NewGuid(), null, 0m,
                new[] { new CompleteSaleItemDto(productId, unitId, 1m, 0m, null) },
                new[] { new CompleteSalePaymentDto(TestDataBuilder.CashPaymentMethodId, 10m, null, Guid.NewGuid()) },
                OccurredAtUtc: occurredAtUtc), CancellationToken.None);
            Assert.True(result.IsSuccess, result.Error?.Message);
            var invoice = await db.SaleInvoices.AsNoTracking().SingleAsync(i => i.Id == result.Value.SaleInvoiceId);
            return (result.Value, invoice.CreatedAtUtc, invoice.ReceivedAtUtc);
        }

        // بلا وقت: وقت الوصول، ولا تعليم.
        var plain = await SellAsync(null);
        Assert.Empty(plain.Response.ReviewFlags);
        Assert.True(Math.Abs((plain.CreatedAt - plain.ReceivedAt!.Value).TotalSeconds) < 1);

        // قبل 3 ساعات: بينحسب بوقته، الوصول محفوظ لحاله، بلا تعليم.
        var threeHoursAgo = DateTime.UtcNow.AddHours(-3);
        var recent = await SellAsync(threeHoursAgo);
        Assert.Empty(recent.Response.ReviewFlags);
        Assert.True(Math.Abs((recent.CreatedAt - threeHoursAgo).TotalSeconds) < 1);
        Assert.True((recent.ReceivedAt!.Value - recent.CreatedAt).TotalHours > 2.9);

        // قبل يومين: بينقبل بوقته + تعليم "وصلت متأخرة".
        var twoDaysAgo = DateTime.UtcNow.AddDays(-2);
        var late = await SellAsync(twoDaysAgo);
        Assert.Single(late.Response.ReviewFlags);
        Assert.True(Math.Abs((late.CreatedAt - twoDaysAgo).TotalSeconds) < 1);

        // بالمستقبل (ساعة غلط) أو أقدم من أسبوع: وقت الوصول + تعليم.
        var future = await SellAsync(DateTime.UtcNow.AddHours(5));
        Assert.Single(future.Response.ReviewFlags);
        Assert.True(Math.Abs((future.CreatedAt - future.ReceivedAt!.Value).TotalSeconds) < 1);
        var tooOld = await SellAsync(DateTime.UtcNow.AddDays(-10));
        Assert.Single(tooOld.Response.ReviewFlags);
        Assert.True(Math.Abs((tooOld.CreatedAt - tooOld.ReceivedAt!.Value).TotalSeconds) < 1);
    }
}
