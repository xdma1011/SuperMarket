using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text.Json;
using SupermarketSystem.IntegrationTests.Common;
using Xunit;

namespace SupermarketSystem.IntegrationTests.Notifications;

/// <summary>
/// طلب صاحب المشروع (24/9/2026): "اي شيء يكشف السرقة او يحذر من شيء خليها تظهر في التنبيهات".
/// كل إشارة هون بتنعمل عبر HTTP حقيقي، والتحقق من صفحة التنبيهات نفسها (GET /notifications) -
/// العنوان، درجة الخطورة، وإنه النص عربي مفهوم (بلا أسماء enums إنجليزية).
/// </summary>
[Collection(DatabaseCollection.Name)]
public sealed class TheftAlertsTests : IntegrationTestBase
{
    public TheftAlertsTests(DatabaseFixture fixture) : base(fixture) { }

    private sealed record Alert(string Title, string Message, string Severity);

    private static async Task<List<Alert>> GetAlertsAsync(HttpClient admin, string query = "")
    {
        var json = JsonDocument.Parse(await admin.GetStringAsync($"/api/v1/notifications?pageSize=100{query}")).RootElement;
        return json.GetProperty("items").EnumerateArray()
            .Select(n => new Alert(n.GetProperty("title").GetString()!, n.GetProperty("message").GetString()!, n.GetProperty("severity").GetString()!))
            .ToList();
    }

    private static async Task<JsonElement> OkJsonAsync(HttpResponseMessage response, string what)
    {
        var body = await response.Content.ReadAsStringAsync();
        Assert.True(response.IsSuccessStatusCode, $"{what}: {(int)response.StatusCode} {body}");
        return string.IsNullOrWhiteSpace(body) ? default : JsonDocument.Parse(body).RootElement;
    }

    private async Task<(Guid ProductId, Guid UnitId, Guid ProductBranchId)> SeedProductAsync(string name, decimal price, decimal stock)
    {
        using var scope = CreateScope();
        var db = CreateDbContext(scope);
        await TestDataBuilder.EnsureDocumentSequencesProvisionedAsync(db, Fixture.TestBranchId);
        var (product, unit) = await TestDataBuilder.CreateActiveProductAsync(db, name, isComplimentaryAllowed: true);
        var productBranch = await TestDataBuilder.CreateProductBranchAsync(db, product.Id, Fixture.TestBranchId, price);
        await TestDataBuilder.SetStockAsync(db, product.Id, Fixture.TestBranchId, stock);
        return (product.Id, unit.Id, productBranch.Id);
    }

    private async Task<HttpClient> LoginCashierAsync()
    {
        var (_, username) = await UsersTestDataHelper.CreateUserWithRoleReturningUsernameAsync(
            Fixture.Factory.Services, Fixture.TestBranchId, UsersTestDataHelper.CashierRoleId, "alerts.cashier");
        return await LoginHelper.LoginAsAsync(Fixture, username, UsersTestDataHelper.DefaultPassword, appType: "Cashier");
    }

    private async Task<JsonElement> SellAsync(HttpClient client, Guid productId, Guid unitId, decimal quantity, decimal amount) =>
        await OkJsonAsync(await client.PostAsJsonAsync("/api/v1/sales", new
        {
            branchId = Fixture.TestBranchId, clientRequestId = Guid.NewGuid(), customerId = (Guid?)null, invoiceLevelDiscountAmount = 0m,
            items = new[] { new { productId, productUnitId = unitId, quantity, manualDiscountAmount = 0m, productBatchId = (Guid?)null } },
            payments = new[] { new { paymentMethodId = TestDataBuilder.CashPaymentMethodId, amount, externalReference = (string?)null, clientRequestId = Guid.NewGuid() } }
        }), "بيع");

    [Fact]
    public async Task كل_إشارة_سرقة_أو_خطر_بتطلع_بالتنبيهات_بدرجة_خطورتها_وبنص_عربي()
    {
        var (productId, unitId, productBranchId) = await SeedProductAsync("حليب التنبيهات", 1.000m, 200m);
        var admin = await CreateAuthenticatedClientAsync();
        var cashier = await LoginCashierAsync();

        // --- إلغاء فاتورة ---
        var toVoid = await SellAsync(cashier, productId, unitId, 2m, 2.000m);
        await OkJsonAsync(await cashier.PostAsJsonAsync($"/api/v1/sales/{toVoid.GetProperty("saleInvoiceId").GetGuid()}/void",
            new { reason = "CashierError", notes = "غلط" }), "إلغاء");

        // --- إرجاع ---
        var toReturn = await SellAsync(cashier, productId, unitId, 3m, 3.000m);
        var saleId = toReturn.GetProperty("saleInvoiceId").GetGuid();
        var saleInvoiceNumber = toReturn.GetProperty("invoiceNumber").GetString()!;
        var detail = await OkJsonAsync(await cashier.GetAsync($"/api/v1/sales/{saleId}"), "تفاصيل");
        await OkJsonAsync(await cashier.PostAsJsonAsync("/api/v1/returns", new
        {
            originalSaleInvoiceId = saleId, clientRequestId = Guid.NewGuid(), reason = "Defective", notes = (string?)null,
            items = new[] { new { saleInvoiceItemId = detail.GetProperty("items")[0].GetProperty("saleInvoiceItemId").GetGuid(), quantity = 1m } },
            refunds = new[] { new { paymentMethodId = TestDataBuilder.CashPaymentMethodId, amount = 1.000m, externalReference = (string?)null, clientRequestId = Guid.NewGuid() } }
        }), "إرجاع");

        // --- ضيافة وتلف فوق الحد اليومي (الافتراضي 10) ---
        await OkJsonAsync(await admin.PostAsJsonAsync("/api/v1/inventory/complimentary-issues", new
        {
            productId, productUnitId = unitId, branchId = Fixture.TestBranchId, quantity = 11m, reason = "ضيافة عرس"
        }), "ضيافة");
        await OkJsonAsync(await admin.PostAsJsonAsync("/api/v1/inventory/waste-issues", new
        {
            productId, productUnitId = unitId, branchId = Fixture.TestBranchId, quantity = 11m, reason = "Broken", notes = (string?)null,
            isReplacedBySupplier = false
        }), "تلف");

        // --- سعر شراء أعلى من المعتاد (المعتاد 0.500، الجديد 1.000 = +100% فوق حد 15%) ---
        var supplier = await OkJsonAsync(await admin.PostAsJsonAsync("/api/v1/suppliers", new
        {
            name = "مورد التنبيهات", contactName = (string?)null, phone = "0790000001", email = (string?)null,
            street = (string?)null, city = (string?)null, postalCode = (string?)null, country = (string?)null
        }), "مورد");
        object PurchaseBody(decimal unitCost) => new
        {
            branchId = Fixture.TestBranchId, supplierId = supplier.GetProperty("supplierId").GetGuid(), supplierInvoiceReference = (string?)null,
            items = new[] { new { productId, productUnitId = unitId, quantity = 10m, unitCost, existingProductBatchId = (Guid?)null, newBatchNumber = (string?)null, newBatchExpiryDate = (DateOnly?)null } }
        };
        await OkJsonAsync(await admin.PostAsJsonAsync("/api/v1/purchase-invoices", PurchaseBody(0.500m)), "شراء عادي");
        await OkJsonAsync(await admin.PostAsJsonAsync("/api/v1/purchase-invoices", PurchaseBody(1.000m)), "شراء مرتفع");

        // --- تنزيل سعر (ورفع ما بينبّه) ---
        string PriceUrl() => $"/api/v1/products/{productId}/branches/{productBranchId}/price-change-requests";
        await OkJsonAsync(await admin.PostAsJsonAsync(PriceUrl(), new { requestedPrice = 1.200m }), "رفع سعر");
        await OkJsonAsync(await admin.PostAsJsonAsync(PriceUrl(), new { requestedPrice = 0.900m }), "تنزيل سعر");

        // --- نقص بالجرد ---
        var stocktake = await OkJsonAsync(await admin.PostAsJsonAsync("/api/v1/stocktakes",
            new { branchId = Fixture.TestBranchId, includeAllProductsAtBranch = false, productIds = new[] { productId } }), "جرد");
        var stocktakeId = stocktake.GetProperty("stocktakeId").GetGuid();
        var stocktakeDetail = await OkJsonAsync(await admin.GetAsync($"/api/v1/stocktakes/{stocktakeId}"), "تفاصيل الجرد");
        var stocktakeItem = stocktakeDetail.GetProperty("items")[0];
        var expected = stocktakeItem.GetProperty("expectedQuantity").GetDecimal();
        await OkJsonAsync(await admin.PostAsJsonAsync(
            $"/api/v1/stocktakes/{stocktakeId}/items/{stocktakeItem.GetProperty("stocktakeItemId").GetGuid()}/count",
            new { countedQuantity = expected - 4m }), "عد");
        await OkJsonAsync(await admin.PostAsync($"/api/v1/stocktakes/{stocktakeId}/complete", null), "إنهاء الجرد");
        await OkJsonAsync(await admin.PostAsync($"/api/v1/stocktakes/{stocktakeId}/approve", null), "اعتماد الجرد");

        // --- عجز صندوق (الإعداد الافتراضي 0 = أي فرق بينبّه) ---
        var closing = await OkJsonAsync(await cashier.PostAsJsonAsync("/api/v1/cash-closings", new
        {
            branchId = Fixture.TestBranchId, businessDate = DateOnly.FromDateTime(DateTime.UtcNow), countedCash = 0m,
            countedDetails = Array.Empty<object>()
        }), "تقفيل");
        Assert.True(closing.GetProperty("variance").GetDecimal() < 0);

        // --- فاتورة أوفلاين انحذفت من الكاشير ---
        await OkJsonAsync(await cashier.PostAsJsonAsync("/api/v1/cashier-sync/report-discarded-pending-sale", new
        {
            clientRequestId = Guid.NewGuid(), branchId = Fixture.TestBranchId, createdAtLocal = DateTime.UtcNow, attemptCount = 7,
            lastErrorMessage = "422"
        }), "حذف فاتورة معلّقة");

        // --- قفل حساب بعد 5 محاولات فاشلة (تنبيه واحد بس، حتى لو كمّل يحاول) ---
        var (_, victim) = await UsersTestDataHelper.CreateUserWithRoleReturningUsernameAsync(
            Fixture.Factory.Services, Fixture.TestBranchId, UsersTestDataHelper.CashierRoleId, "alerts.victim");
        for (var attempt = 0; attempt < 7; attempt++)
        {
            await LoginHelper.LoginRawAsync(Fixture, victim, "wrong-password", appType: "Cashier");
        }

        // ================= التحقق =================
        var alerts = await GetAlertsAsync(admin);
        void AssertAlert(string titleStart, string severity, params string[] messageContains)
        {
            var alert = alerts.FirstOrDefault(a => a.Title.StartsWith(titleStart));
            Assert.True(alert is not null, $"ما في تنبيه يبلش بـ\"{titleStart}\". الموجود: {string.Join(" | ", alerts.Select(a => a.Title))}");
            Assert.Equal(severity, alert!.Severity);
            foreach (var text in messageContains)
            {
                Assert.Contains(text, alert.Message);
            }
        }

        AssertAlert("إلغاء فاتورة", "Warning", "خطأ كاشير", "فرع الاختبار");
        AssertAlert("إرجاع", "Warning", saleInvoiceNumber, "تالف/معيب", "مستخدم اختبار alerts.cashier");
        AssertAlert("ضيافة فوق الحد اليومي — حليب التنبيهات", "Warning", "ضيافة عرس");
        AssertAlert("تلف فوق الحد اليومي", "Warning", "مكسور");
        AssertAlert("سعر شراء أعلى من المعتاد", "Warning", "1.000", "0.500");
        AssertAlert("تنزيل سعر", "Warning", "1.200", "0.900");
        AssertAlert("نقص بالجرد", "Critical", "ناقص 4");
        AssertAlert("تقفيل صندوق — عجز", "Critical", "فرع الاختبار");
        AssertAlert("حذف فاتورة كاشير أوفلاين نهائيًا", "Critical");
        AssertAlert($"قفل حساب — {victim}", "Critical", "الكاشير");

        Assert.Single(alerts, a => a.Title.StartsWith("قفل حساب"));
        Assert.Single(alerts, a => a.Title.StartsWith("تنزيل سعر")); // الرفع ما نبّه
        Assert.DoesNotContain(alerts, a => a.Message.Contains("CashierError") || a.Message.Contains("Defective") || a.Message.Contains("Broken"));

        // فلتر "الخطيرة بس" + عدّاد الرئيسية (آخر 24 ساعة)
        var critical = await GetAlertsAsync(admin, "&minSeverity=Critical");
        Assert.All(critical, a => Assert.Equal("Critical", a.Severity));
        Assert.Equal(4, critical.Count);
        var future = await GetAlertsAsync(admin, $"&minSeverity=Critical&sinceUtc={Uri.EscapeDataString(DateTime.UtcNow.AddHours(1).ToString("o"))}");
        Assert.Empty(future);
    }

    [Fact]
    public async Task زيادة_بالصندوق_بتنبّه_كمهمة_مش_خطيرة_وبلا_فرق_ما_في_تنبيه()
    {
        var (productId, unitId, _) = await SeedProductAsync("منتج الزيادة", 2.000m, 10m);
        var admin = await CreateAuthenticatedClientAsync();
        var cashier = await LoginCashierAsync();
        await SellAsync(cashier, productId, unitId, 1m, 2.000m);

        var today = DateOnly.FromDateTime(DateTime.UtcNow);
        await OkJsonAsync(await cashier.PostAsJsonAsync("/api/v1/cash-closings", new
        {
            branchId = Fixture.TestBranchId, businessDate = today, countedCash = 2.000m, countedDetails = Array.Empty<object>()
        }), "تقفيل مطابق");
        Assert.DoesNotContain(await GetAlertsAsync(admin), a => a.Title.StartsWith("تقفيل صندوق"));

        await SellAsync(cashier, productId, unitId, 1m, 2.000m);
        await OkJsonAsync(await cashier.PostAsJsonAsync("/api/v1/cash-closings", new
        {
            branchId = Fixture.TestBranchId, businessDate = today, shiftNumber = 2, countedCash = 2.500m, countedDetails = Array.Empty<object>()
        }), "تقفيل بزيادة");

        var surplus = Assert.Single(await GetAlertsAsync(admin), a => a.Title.StartsWith("تقفيل صندوق"));
        Assert.Equal("Warning", surplus.Severity);
        Assert.Contains("زيادة", surplus.Title);
        Assert.Contains("بيعات ما انسجّلت", surplus.Message);
    }
}
