using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using SupermarketSystem.Domain.Notifications;
using SupermarketSystem.Domain.Sales;
using SupermarketSystem.IntegrationTests.Common;
using Xunit;

namespace SupermarketSystem.IntegrationTests.Sales;

/// <summary>
/// صفحة التلفون (28/9/2026): مساعد الكاشير بيجهّز طلب والكاشير بيحاسب عليه، سحب الشريك بسعر التكلفة
/// (دفع حقها أو اخصمها مني)، وبحث الكاشير عن الفواتير بالوقت والصنف. كله عبر HTTP بصلاحيات حقيقية.
///
///   حليب: شراء 20 × 0.800، بيع 1.250، باركود 6251000000017 · جبنة: بلا أي شراء (بلا تكلفة).
/// </summary>
[Collection(DatabaseCollection.Name)]
public sealed class SaleAssistHttpTests : IntegrationTestBase
{
    private const string MilkBarcode = "6251000000017";

    public SaleAssistHttpTests(DatabaseFixture fixture) : base(fixture) { }

    private static string Iso(DateTime value) => Uri.EscapeDataString(value.ToString("o"));

    private static async Task<JsonElement> ReadJsonAsync(HttpResponseMessage response, string what)
    {
        var body = await response.Content.ReadAsStringAsync();
        Assert.True(response.IsSuccessStatusCode, $"{what}: {(int)response.StatusCode} {body}");
        return string.IsNullOrWhiteSpace(body) ? default : JsonDocument.Parse(body).RootElement;
    }

    private async Task<HttpClient> CashierAsync(string prefix)
    {
        var (_, username) = await UsersTestDataHelper.CreateUserWithRoleReturningUsernameAsync(
            Fixture.Factory.Services, Fixture.TestBranchId, UsersTestDataHelper.CashierRoleId, prefix);
        var client = Fixture.Factory.CreateClient();
        var login = await ReadJsonAsync(await client.PostAsJsonAsync("/api/v1/auth/login", new
        {
            username, password = UsersTestDataHelper.DefaultPassword, appType = 1,
            branchId = (Guid?)null, ipAddress = (string?)null, deviceInfo = "CASHIER-PC"
        }), "دخول");
        client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", login.GetProperty("accessToken").GetString());
        return client;
    }

    private sealed record Catalog(HttpClient Admin, Guid MilkId, Guid MilkUnit, Guid CheeseId, Guid CheeseUnit);

    private async Task<Catalog> SetUpAsync()
    {
        var branchId = Fixture.TestBranchId;
        using (var scope = CreateScope())
        {
            await TestDataBuilder.EnsureDocumentSequencesProvisionedAsync(CreateDbContext(scope), branchId);
        }

        var admin = await CreateAuthenticatedClientAsync();
        var category = await ReadJsonAsync(await admin.PostAsJsonAsync("/api/v1/product-categories",
            new { name = "ألبان", parentCategoryId = (Guid?)null }), "تصنيف");
        var categoryId = category.GetProperty("categoryId").GetGuid();

        async Task<(Guid, Guid)> ProductAsync(string name, decimal price, string? barcode)
        {
            var created = await ReadJsonAsync(await admin.PostAsJsonAsync("/api/v1/products", new
            {
                name, description = (string?)null, categoryId, isBatchTracked = false,
                suggestedRetailPrice = (decimal?)null, expectedShelfLifeDays = (int?)null,
                units = new[] { new { unitName = "حبة", conversionFactorToBase = 1m, isBaseUnit = true } },
                barcodes = barcode is null ? Array.Empty<object>() : new object[] { new { barcodeValue = barcode, unitName = "حبة" } }
            }), name);
            var productId = created.GetProperty("productId").GetGuid();
            await ReadJsonAsync(await admin.PostAsJsonAsync($"/api/v1/products/{productId}/branches",
                new { branchId, sellingPrice = price, minimumStock = 0m, maximumStock = (decimal?)null }), $"ربط {name}");
            var units = await ReadJsonAsync(await admin.GetAsync($"/api/v1/products/{productId}/units"), "وحدات");
            return (productId, units.EnumerateArray().Single().GetProperty("id").GetGuid());
        }

        var (milkId, milkUnit) = await ProductAsync("حليب طازج", 1.250m, MilkBarcode);
        var (cheeseId, cheeseUnit) = await ProductAsync("جبنة بيضاء", 2.000m, null);

        var supplier = await ReadJsonAsync(await admin.PostAsJsonAsync("/api/v1/suppliers", new
        {
            name = "شركة الألبان", contactName = (string?)null, phone = "0791111111", email = (string?)null,
            street = (string?)null, city = "عمّان", postalCode = (string?)null, country = "الأردن"
        }), "مورد");
        await ReadJsonAsync(await admin.PostAsJsonAsync("/api/v1/purchase-invoices", new
        {
            branchId, supplierId = supplier.GetProperty("supplierId").GetGuid(), supplierInvoiceReference = "S-1",
            items = new[]
            {
                new { productId = milkId, productUnitId = milkUnit, quantity = 20m, unitCost = 0.800m, existingProductBatchId = (Guid?)null, newBatchNumber = (string?)null, newBatchExpiryDate = (DateOnly?)null }
            },
            imageReferences = (string[]?)null, dueDate = (DateOnly?)null
        }), "شراء");

        return new Catalog(admin, milkId, milkUnit, cheeseId, cheeseUnit);
    }

    private async Task<decimal> MilkStockAsync(Guid milkId)
    {
        using var scope = CreateScope();
        var db = CreateDbContext(scope);
        return await db.Stocks.IgnoreQueryFilters().Where(s => s.ProductId == milkId && s.BranchId == Fixture.TestBranchId)
            .SumAsync(s => s.QuantityOnHand);
    }

    [Fact]
    public async Task المساعد_بيجهّز_طلب_برقم_والكاشير_بيحاسب_عليه_والطلب_بيختفي()
    {
        var c = await SetUpAsync();
        var branchId = Fixture.TestBranchId;
        var helper = await CashierAsync("helper");
        var cashier = await CashierAsync("cashier.assist");

        // ضرب الباركود من التلفون - الوحدة والسعر من الفرع.
        var scanned = await ReadJsonAsync(await helper.GetAsync($"/api/v1/sales/scan-lookup?branchId={branchId}&term={MilkBarcode}"), "باركود");
        var hit = scanned.EnumerateArray().Single();
        Assert.Equal(c.MilkUnit, hit.GetProperty("productUnitId").GetGuid());
        Assert.Equal(1.250m, hit.GetProperty("unitPrice").GetDecimal());

        var byName = await ReadJsonAsync(await helper.GetAsync($"/api/v1/sales/scan-lookup?branchId={branchId}&term={Uri.EscapeDataString("جبنة")}"), "بحث بالاسم");
        Assert.Equal(c.CheeseId, byName.EnumerateArray().Single().GetProperty("productId").GetGuid());

        object Order(decimal qty) => new
        {
            branchId, note = "زبون الكيس الأزرق",
            items = new[] { new { productId = c.MilkId, productUnitId = c.MilkUnit, quantity = qty } }
        };

        var first = await ReadJsonAsync(await helper.PostAsJsonAsync("/api/v1/prepared-orders", Order(3m)), "طلب 1");
        var second = await ReadJsonAsync(await helper.PostAsJsonAsync("/api/v1/prepared-orders", Order(1m)), "طلب 2");
        Assert.Equal(1, first.GetProperty("ticketNumber").GetInt32());
        Assert.Equal(2, second.GetProperty("ticketNumber").GetInt32());
        Assert.Equal(3.750m, first.GetProperty("estimatedTotal").GetDecimal());

        // الطلب ما بيلمس المخزون لحاله.
        Assert.Equal(20m, await MilkStockAsync(c.MilkId));

        // فرع غير فرعه - ممنوع.
        var otherBranch = await helper.PostAsJsonAsync("/api/v1/prepared-orders", new
        {
            branchId = Guid.NewGuid(), note = (string?)null,
            items = new[] { new { productId = c.MilkId, productUnitId = c.MilkUnit, quantity = 1m } }
        });
        Assert.Equal(HttpStatusCode.Forbidden, otherBranch.StatusCode);

        var open = await ReadJsonAsync(await cashier.GetAsync($"/api/v1/prepared-orders?branchId={branchId}"), "الطلبات الجاهزة");
        Assert.Equal(2, open.GetArrayLength());
        var firstOpen = open[0];
        Assert.Equal(1, firstOpen.GetProperty("ticketNumber").GetInt32());
        Assert.Equal("مستخدم اختبار helper", firstOpen.GetProperty("preparedByName").GetString());
        Assert.Equal("حليب طازج", firstOpen.GetProperty("items")[0].GetProperty("productName").GetString());
        Assert.Equal("زبون الكيس الأزرق", firstOpen.GetProperty("note").GetString());

        // الكاشير بيحاسب على الطلب 1 - فاتورة عادية باسمه، والطلب بيتسكّر.
        var firstId = first.GetProperty("preparedOrderId").GetGuid();
        await ReadJsonAsync(await cashier.PostAsJsonAsync("/api/v1/sales", new
        {
            branchId, clientRequestId = Guid.NewGuid(), customerId = (Guid?)null, invoiceLevelDiscountAmount = 0m,
            items = new[] { new { productId = c.MilkId, productUnitId = c.MilkUnit, quantity = 3m, manualDiscountAmount = 0m, productBatchId = (Guid?)null } },
            payments = new[] { new { paymentMethodId = TestDataBuilder.CashPaymentMethodId, amount = 3.750m, externalReference = (string?)null, clientRequestId = Guid.NewGuid() } },
            preparedOrderId = firstId
        }), "بيع الطلب 1");
        Assert.Equal(17m, await MilkStockAsync(c.MilkId));

        open = await ReadJsonAsync(await cashier.GetAsync($"/api/v1/prepared-orders?branchId={branchId}"), "بعد البيع");
        Assert.Equal(2, open.EnumerateArray().Single().GetProperty("ticketNumber").GetInt32());

        using (var scope = CreateScope())
        {
            var db = CreateDbContext(scope);
            var completed = await db.SuspendedSales.IgnoreQueryFilters().SingleAsync(s => s.Id == firstId);
            Assert.Equal(SuspendedSaleStatus.Completed, completed.Status);
            Assert.NotNull(completed.SaleInvoiceId);
        }

        // الزبون التاني فلّ - إلغاء، ومرة تانية = 409.
        var secondId = second.GetProperty("preparedOrderId").GetGuid();
        await ReadJsonAsync(await cashier.PostAsync($"/api/v1/prepared-orders/{secondId}/cancel", null), "إلغاء");
        var again = await cashier.PostAsync($"/api/v1/prepared-orders/{secondId}/cancel", null);
        Assert.Equal(HttpStatusCode.Conflict, again.StatusCode);

        open = await ReadJsonAsync(await cashier.GetAsync($"/api/v1/prepared-orders?branchId={branchId}"), "بعد الإلغاء");
        Assert.Equal(0, open.GetArrayLength());

        // الرقم ما بيتكرر بنفس اليوم حتى بعد البيع/الإلغاء.
        var third = await ReadJsonAsync(await helper.PostAsJsonAsync("/api/v1/prepared-orders", Order(1m)), "طلب 3");
        Assert.Equal(3, third.GetProperty("ticketNumber").GetInt32());
    }

    [Fact]
    public async Task سحب_بسعر_التكلفة_دفع_حقها_للصندوق_أو_اخصمها_مني_ومنتج_بلا_تكلفة_مرفوض()
    {
        var c = await SetUpAsync();
        var branchId = Fixture.TestBranchId;
        var admin = c.Admin;

        object Items(params (Guid P, Guid U, decimal Q)[] lines) =>
            lines.Select(l => new { productId = l.P, productUnitId = l.U, quantity = l.Q }).ToArray();

        // عرض السعر: حليب 2 × 0.800 = 1.600 (بسعر البيع 2.500)، والجبنة بلا تكلفة.
        var quote = await ReadJsonAsync(await admin.PostAsJsonAsync("/api/v1/sales/at-cost/quote", new
        {
            branchId, items = Items((c.MilkId, c.MilkUnit, 2m), (c.CheeseId, c.CheeseUnit, 1m))
        }), "عرض سعر");
        Assert.Equal(1.600m, quote.GetProperty("total").GetDecimal());
        Assert.Equal(4.500m, quote.GetProperty("sellingValue").GetDecimal());
        Assert.Equal("جبنة بيضاء", quote.GetProperty("missingCost").EnumerateArray().Single().GetString());

        var withCheese = await admin.PostAsJsonAsync("/api/v1/sales/at-cost", new
        {
            branchId, clientRequestId = Guid.NewGuid(), deductFromShare = false, paymentMethodId = TestDataBuilder.CashPaymentMethodId,
            items = Items((c.MilkId, c.MilkUnit, 2m), (c.CheeseId, c.CheeseUnit, 1m))
        });
        Assert.Equal(HttpStatusCode.UnprocessableEntity, withCheese.StatusCode);
        Assert.Contains("جبنة بيضاء", await withCheese.Content.ReadAsStringAsync());
        Assert.Equal(20m, await MilkStockAsync(c.MilkId));

        // دفع حقها: المبلغ بالتكلفة بيدخل الصندوق.
        var paid = await ReadJsonAsync(await admin.PostAsJsonAsync("/api/v1/sales/at-cost", new
        {
            branchId, clientRequestId = Guid.NewGuid(), deductFromShare = false, paymentMethodId = TestDataBuilder.CashPaymentMethodId,
            items = Items((c.MilkId, c.MilkUnit, 2m))
        }), "دفع حقها");
        Assert.Equal(1.600m, paid.GetProperty("totalAmount").GetDecimal());
        Assert.Equal(1.600m, paid.GetProperty("totalPaidAmount").GetDecimal());

        // اخصمها مني: بلا دفع، المبلغ كله مفتوح عليه.
        var deducted = await ReadJsonAsync(await admin.PostAsJsonAsync("/api/v1/sales/at-cost", new
        {
            branchId, clientRequestId = Guid.NewGuid(), deductFromShare = true, paymentMethodId = (Guid?)null,
            items = Items((c.MilkId, c.MilkUnit, 1m))
        }), "اخصمها مني");
        Assert.Equal(0.800m, deducted.GetProperty("totalAmount").GetDecimal());
        Assert.Equal(0m, deducted.GetProperty("totalPaidAmount").GetDecimal());
        Assert.Equal(17m, await MilkStockAsync(c.MilkId));

        using (var scope = CreateScope())
        {
            var db = CreateDbContext(scope);
            var invoices = await db.SaleInvoices.IgnoreQueryFilters().Include(s => s.Items)
                .Where(s => s.IsAtCostWithdrawal).ToListAsync();
            Assert.Equal(2, invoices.Count);
            Assert.Single(invoices, i => i.IsDeductedFromShare);
            // ربح صفر: التكلفة المسجّلة = السعر بالضبط.
            Assert.All(invoices.SelectMany(i => i.Items), item => Assert.Equal(item.UnitPriceSnapshot, item.UnitCostSnapshot));

            // الكاش المدفوع دخل الدرج (بينحسب بالتقفيل)، و"اخصمها مني" ما دخل إشي.
            var paidId = paid.GetProperty("saleInvoiceId").GetGuid();
            var drawerIn = await db.CashDrawerLogs.IgnoreQueryFilters()
                .Where(l => l.BranchId == branchId && l.MovementType == Domain.CashManagement.CashDrawerMovementType.SaleCashIn)
                .SumAsync(l => l.Amount);
            Assert.Equal(1.600m, drawerIn);
            Assert.Contains(invoices, i => i.Id == paidId);

            var alerts = await db.Notifications.IgnoreQueryFilters()
                .Where(n => n.Title.StartsWith("سحب بسعر التكلفة")).ToListAsync();
            // تنبيه لكل سحب (المُرسِل بيعمل نسخة لكل مستلم، فالعدّ بالرسالة).
            Assert.Equal(2, alerts.Select(a => a.Message).Distinct().Count());
            Assert.All(alerts, a => Assert.Equal(NotificationSeverity.Warning, a.Severity));
        }

        // قائمة المبيعات بتعلّمها.
        var list = await ReadJsonAsync(await admin.GetAsync($"/api/v1/sales?branchId={branchId}&pageSize=50"), "قائمة المبيعات");
        Assert.Equal(2, list.GetProperty("items").EnumerateArray().Count(i => i.GetProperty("isAtCostWithdrawal").GetBoolean()));

        // الكاشير ممنوع من السحب بسعر التكلفة.
        var cashier = await CashierAsync("cashier.atcost");
        var cashierQuote = await cashier.PostAsJsonAsync("/api/v1/sales/at-cost/quote", new { branchId, items = Items((c.MilkId, c.MilkUnit, 1m)) });
        Assert.Equal(HttpStatusCode.Forbidden, cashierQuote.StatusCode);
        var cashierWithdraw = await cashier.PostAsJsonAsync("/api/v1/sales/at-cost", new
        {
            branchId, clientRequestId = Guid.NewGuid(), deductFromShare = true, paymentMethodId = (Guid?)null,
            items = Items((c.MilkId, c.MilkUnit, 1m))
        });
        Assert.Equal(HttpStatusCode.Forbidden, cashierWithdraw.StatusCode);
    }

    [Fact]
    public async Task الكاشير_بيدوّر_على_فاتورة_بالوقت_والصنف_وبيشوف_مين_عملها()
    {
        var c = await SetUpAsync();
        var branchId = Fixture.TestBranchId;
        var cashier = await CashierAsync("cashier.search");

        await ReadJsonAsync(await cashier.PostAsJsonAsync("/api/v1/sales", new
        {
            branchId, clientRequestId = Guid.NewGuid(), customerId = (Guid?)null, invoiceLevelDiscountAmount = 0m,
            items = new[] { new { productId = c.MilkId, productUnitId = c.MilkUnit, quantity = 1m, manualDiscountAmount = 0m, productBatchId = (Guid?)null } },
            payments = new[] { new { paymentMethodId = TestDataBuilder.CashPaymentMethodId, amount = 1.250m, externalReference = (string?)null, clientRequestId = Guid.NewGuid() } }
        }), "بيع");

        async Task<List<JsonElement>> SearchAsync(string query) =>
            (await ReadJsonAsync(await cashier.GetAsync($"/api/v1/sales?branchId={branchId}&{query}"), query))
            .GetProperty("items").EnumerateArray().ToList();

        var now = DateTime.UtcNow;
        var inWindow = await SearchAsync($"fromUtc={Iso(now.AddMinutes(-5))}&toUtc={Iso(now.AddMinutes(5))}");
        var sale = Assert.Single(inWindow);
        Assert.Equal("مستخدم اختبار cashier.search", sale.GetProperty("cashierName").GetString());

        Assert.Empty(await SearchAsync($"fromUtc={Iso(now.AddHours(1))}&toUtc={Iso(now.AddHours(2))}"));
        Assert.Single(await SearchAsync($"productSearch={Uri.EscapeDataString("حليب")}"));
        Assert.Single(await SearchAsync($"productSearch={MilkBarcode}"));
        Assert.Empty(await SearchAsync($"productSearch={Uri.EscapeDataString("جبنة")}"));
    }
}
