using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using SupermarketSystem.IntegrationTests.Common;
using Xunit;

namespace SupermarketSystem.IntegrationTests.BusinessTimeTests;

/// <summary>
/// توقيت المحل (29/9/2026): المنطقة من الإعدادات (افتراضي Asia/Amman) + فرق يدوي بيغلبها (التوقيت الشتوي بالأردن
/// مرات بيتطبّق ومرات لأ). التخزين UTC، وحدود الشهر بكشف الربح بتوقيت المحل.
/// </summary>
[Collection(DatabaseCollection.Name)]
public sealed class BusinessTimeHttpTests : IntegrationTestBase
{
    public BusinessTimeHttpTests(DatabaseFixture fixture) : base(fixture) { }

    private static async Task<JsonElement> OkJsonAsync(HttpResponseMessage response, string what)
    {
        var body = await response.Content.ReadAsStringAsync();
        Assert.True(response.IsSuccessStatusCode, $"{what}: {(int)response.StatusCode} {body}");
        return JsonDocument.Parse(body).RootElement;
    }

    [Fact]
    public async Task الإعدادات_الافتراضية_بلا_دخول_والتعديل_للإدارة_بس_مع_التحقق()
    {
        var anonymous = Fixture.Factory.CreateClient();
        var defaults = await OkJsonAsync(await anonymous.GetAsync("/api/v1/system/time-settings"), "قراءة بلا دخول");
        Assert.Equal("Asia/Amman", defaults.GetProperty("timeZoneId").GetString());
        Assert.Equal(JsonValueKind.Null, defaults.GetProperty("fixedUtcOffsetMinutes").ValueKind);
        Assert.True(defaults.GetProperty("isTimeZoneKnown").GetBoolean());

        var admin = await CreateAuthenticatedClientAsync();
        var manual = await OkJsonAsync(await admin.PutAsJsonAsync("/api/v1/system/time-settings",
            new { timeZoneId = "Asia/Amman", fixedUtcOffsetMinutes = 120 }), "فرق يدوي +2");
        Assert.Equal(120, manual.GetProperty("currentOffsetMinutes").GetInt32());
        var serverUtc = manual.GetProperty("serverUtcNow").GetDateTime();
        var local = manual.GetProperty("localNow").GetDateTime();
        Assert.Equal(TimeSpan.FromHours(2), local - serverUtc);

        var back = await OkJsonAsync(await admin.PutAsJsonAsync("/api/v1/system/time-settings",
            new { timeZoneId = "Asia/Dubai", fixedUtcOffsetMinutes = (int?)null }), "رجوع للمنطقة");
        Assert.Equal(240, back.GetProperty("currentOffsetMinutes").GetInt32());

        Assert.Equal(HttpStatusCode.BadRequest, (await admin.PutAsJsonAsync("/api/v1/system/time-settings",
            new { timeZoneId = "Mars/Olympus", fixedUtcOffsetMinutes = (int?)null })).StatusCode);
        Assert.Equal(HttpStatusCode.BadRequest, (await admin.PutAsJsonAsync("/api/v1/system/time-settings",
            new { timeZoneId = "Asia/Amman", fixedUtcOffsetMinutes = 50 })).StatusCode);

        var (_, cashierName) = await UsersTestDataHelper.CreateUserWithRoleReturningUsernameAsync(
            Fixture.Factory.Services, Fixture.TestBranchId, UsersTestDataHelper.CashierRoleId, "time.cashier");
        var cashier = await LoginHelper.LoginAsAsync(Fixture, cashierName, UsersTestDataHelper.DefaultPassword, appType: "Cashier");
        Assert.Equal(HttpStatusCode.Forbidden, (await cashier.PutAsJsonAsync("/api/v1/system/time-settings",
            new { timeZoneId = "Asia/Amman", fixedUtcOffsetMinutes = (int?)null })).StatusCode);
    }

    [Fact]
    public async Task بيعة_الساعة_12_ونص_بليل_أول_الشهر_محليًا_بتنحسب_عالشهر_الجديد()
    {
        var admin = await CreateAuthenticatedClientAsync();
        Guid productId, unitId;
        using (var scope = CreateScope())
        {
            var db = CreateDbContext(scope);
            await TestDataBuilder.EnsureDocumentSequencesProvisionedAsync(db, Fixture.TestBranchId);
            var (product, unit) = await TestDataBuilder.CreateActiveProductAsync(db, "منتج حدود الشهر");
            await TestDataBuilder.CreateProductBranchAsync(db, product.Id, Fixture.TestBranchId, 5.000m);
            await TestDataBuilder.SetStockAsync(db, product.Id, Fixture.TestBranchId, 10m);
            (productId, unitId) = (product.Id, unit.Id);
        }

        var sale = await OkJsonAsync(await admin.PostAsJsonAsync("/api/v1/sales", new
        {
            branchId = Fixture.TestBranchId, clientRequestId = Guid.NewGuid(), customerId = (Guid?)null, invoiceLevelDiscountAmount = 0m,
            items = new[] { new { productId, productUnitId = unitId, quantity = 1m, manualDiscountAmount = 0m, productBatchId = (Guid?)null } },
            payments = new[] { new { paymentMethodId = TestDataBuilder.CashPaymentMethodId, amount = 5.000m, externalReference = (string?)null, clientRequestId = Guid.NewGuid() } }
        }), "بيع");
        var invoiceId = sale.GetProperty("saleInvoiceId").GetGuid();

        // 1 آذار 2031 الساعة 00:30 بتوقيت +3 = 28 شباط 21:30 UTC
        var soldAtUtc = new DateTime(2031, 2, 28, 21, 30, 0, DateTimeKind.Utc);
        using (var scope = CreateScope())
        {
            await CreateDbContext(scope).SaleInvoices.IgnoreQueryFilters()
                .Where(s => s.Id == invoiceId).ExecuteUpdateAsync(s => s.SetProperty(x => x.CreatedAtUtc, soldAtUtc));
        }

        async Task<decimal> SalesOfAsync(int month) => (await OkJsonAsync(await admin.GetAsync(
            $"/api/v1/finance/profit-statement?branchId={Fixture.TestBranchId}&year=2031&month={month}"), $"كشف {month}"))
            .GetProperty("totalSales").GetDecimal();

        // الافتراضي (Asia/Amman = +3): آذار
        Assert.Equal(5.000m, await SalesOfAsync(3));
        Assert.Equal(0m, await SalesOfAsync(2));

        // فرق يدوي +2: الساعة 23:30 بشباط - بترجع لشباط
        await OkJsonAsync(await admin.PutAsJsonAsync("/api/v1/system/time-settings",
            new { timeZoneId = "Asia/Amman", fixedUtcOffsetMinutes = 120 }), "+2");
        Assert.Equal(5.000m, await SalesOfAsync(2));
        Assert.Equal(0m, await SalesOfAsync(3));
    }
}
