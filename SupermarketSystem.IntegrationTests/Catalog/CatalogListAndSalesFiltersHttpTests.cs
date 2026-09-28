using System.Net.Http.Json;
using System.Text.Json;
using SupermarketSystem.IntegrationTests.Common;
using Xunit;

namespace SupermarketSystem.IntegrationTests.Catalog;

/// <summary>
/// مراجعة UI/UX (24/9، انبنت 28/9/2026): قائمة الكتالوج بسعر الفرع الفعلي والرصيد والباركود وبحث بالباركود،
/// وفلاتر صفحة المبيعات (الكاشير، طريقة الدفع) عبر HTTP حقيقي.
/// </summary>
[Collection(DatabaseCollection.Name)]
public sealed class CatalogListAndSalesFiltersHttpTests : IntegrationTestBase
{
    public CatalogListAndSalesFiltersHttpTests(DatabaseFixture fixture) : base(fixture) { }

    private static async Task<JsonElement> OkJsonAsync(HttpResponseMessage response, string what)
    {
        var body = await response.Content.ReadAsStringAsync();
        Assert.True(response.IsSuccessStatusCode, $"{what}: {(int)response.StatusCode} {body}");
        return JsonDocument.Parse(body).RootElement;
    }

    [Fact]
    public async Task قائمة_الكتالوج_بتعرض_سعر_الفرع_والرصيد_والباركود_وبتبحث_بالباركود()
    {
        Guid linkedId, unlinkedId;
        using (var scope = CreateScope())
        {
            var db = CreateDbContext(scope);
            var (linked, linkedUnit) = await TestDataBuilder.CreateActiveProductAsync(db, "زيت الكتالوج");
            // Id من العميل: ولد جديد لكيان متتبَّع EF بيحسبه Modified - لازم Add صريح.
            db.ProductBarcodes.Add(linked.AddBarcode("6250000999001", linkedUnit.Id));
            db.ProductBarcodes.Add(linked.AddBarcode("6250000999002", linkedUnit.Id));
            await db.SaveChangesAsync();
            await TestDataBuilder.CreateProductBranchAsync(db, linked.Id, Fixture.TestBranchId, 3.750m);
            await TestDataBuilder.SetStockAsync(db, linked.Id, Fixture.TestBranchId, 12m);
            var (unlinked, _) = await TestDataBuilder.CreateActiveProductAsync(db, "سكر الكتالوج بلا فرع");
            linkedId = linked.Id;
            unlinkedId = unlinked.Id;
        }

        var admin = await CreateAuthenticatedClientAsync();
        var list = await OkJsonAsync(await admin.GetAsync($"/api/v1/products?pageSize=100&search=الكتالوج&branchId={Fixture.TestBranchId}"), "قائمة");
        var items = list.GetProperty("items").EnumerateArray().ToList();

        var linkedRow = items.Single(i => i.GetProperty("id").GetGuid() == linkedId);
        Assert.Equal(3.750m, linkedRow.GetProperty("branchSellingPrice").GetDecimal());
        Assert.True(linkedRow.GetProperty("isAvailableAtBranch").GetBoolean());
        Assert.Equal(12m, linkedRow.GetProperty("stockOnHand").GetDecimal());
        Assert.Equal("6250000999001", linkedRow.GetProperty("primaryBarcode").GetString());
        Assert.Equal(2, linkedRow.GetProperty("barcodeCount").GetInt32());

        // غير مربوط بالفرع = ما بيبين بالكاشير (§1.8) - الواجهة بتعلّمه.
        var unlinkedRow = items.Single(i => i.GetProperty("id").GetGuid() == unlinkedId);
        Assert.Equal(JsonValueKind.Null, unlinkedRow.GetProperty("branchSellingPrice").ValueKind);
        Assert.False(unlinkedRow.GetProperty("isAvailableAtBranch").GetBoolean());
        Assert.Equal(0m, unlinkedRow.GetProperty("stockOnHand").GetDecimal());

        // بحث بالباركود
        var byBarcode = await OkJsonAsync(await admin.GetAsync("/api/v1/products?search=6250000999002"), "بحث بالباركود");
        Assert.Equal(linkedId, Assert.Single(byBarcode.GetProperty("items").EnumerateArray()).GetProperty("id").GetGuid());

        // بلا فرع: السعر والرصيد null (زي قبل)
        var noBranch = await OkJsonAsync(await admin.GetAsync("/api/v1/products?search=زيت الكتالوج"), "بلا فرع");
        var row = Assert.Single(noBranch.GetProperty("items").EnumerateArray());
        Assert.Equal(JsonValueKind.Null, row.GetProperty("stockOnHand").ValueKind);
    }

    [Fact]
    public async Task فلاتر_المبيعات_بالكاشير_وطريقة_الدفع()
    {
        Guid productId, unitId;
        using (var scope = CreateScope())
        {
            var db = CreateDbContext(scope);
            await TestDataBuilder.EnsureDocumentSequencesProvisionedAsync(db, Fixture.TestBranchId);
            var (product, unit) = await TestDataBuilder.CreateActiveProductAsync(db, "منتج الفلاتر");
            await TestDataBuilder.CreateProductBranchAsync(db, product.Id, Fixture.TestBranchId, 1.000m);
            await TestDataBuilder.SetStockAsync(db, product.Id, Fixture.TestBranchId, 100m);
            productId = product.Id;
            unitId = unit.Id;
        }

        var (cashierAId, cashierA) = await UsersTestDataHelper.CreateUserWithRoleReturningUsernameAsync(
            Fixture.Factory.Services, Fixture.TestBranchId, UsersTestDataHelper.CashierRoleId, "filters.a");
        var (cashierBId, cashierB) = await UsersTestDataHelper.CreateUserWithRoleReturningUsernameAsync(
            Fixture.Factory.Services, Fixture.TestBranchId, UsersTestDataHelper.CashierRoleId, "filters.b");
        var clientA = await LoginHelper.LoginAsAsync(Fixture, cashierA, UsersTestDataHelper.DefaultPassword, appType: "Cashier");
        var clientB = await LoginHelper.LoginAsAsync(Fixture, cashierB, UsersTestDataHelper.DefaultPassword, appType: "Cashier");

        async Task<string> SellAsync(HttpClient client, Guid paymentMethodId, string? reference) =>
            (await OkJsonAsync(await client.PostAsJsonAsync("/api/v1/sales", new
            {
                branchId = Fixture.TestBranchId, clientRequestId = Guid.NewGuid(), customerId = (Guid?)null, invoiceLevelDiscountAmount = 0m,
                items = new[] { new { productId, productUnitId = unitId, quantity = 1m, manualDiscountAmount = 0m, productBatchId = (Guid?)null } },
                payments = new[] { new { paymentMethodId, amount = 1.000m, externalReference = reference, clientRequestId = Guid.NewGuid() } }
            }), "بيع")).GetProperty("invoiceNumber").GetString()!;

        var cashSaleA = await SellAsync(clientA, TestDataBuilder.CashPaymentMethodId, null);
        var visaSaleA = await SellAsync(clientA, TestDataBuilder.VisaPaymentMethodId, "REF-1");
        var cashSaleB = await SellAsync(clientB, TestDataBuilder.CashPaymentMethodId, null);

        var admin = await CreateAuthenticatedClientAsync();
        async Task<List<string>> NumbersAsync(string query) =>
            (await OkJsonAsync(await admin.GetAsync($"/api/v1/sales?pageSize=100{query}"), "قائمة"))
                .GetProperty("items").EnumerateArray().Select(i => i.GetProperty("invoiceNumber").GetString()!).ToList();

        var byA = await NumbersAsync($"&cashierUserId={cashierAId}");
        Assert.Equal(new[] { visaSaleA, cashSaleA }.OrderBy(x => x), byA.OrderBy(x => x));

        var visa = await NumbersAsync($"&paymentMethodId={TestDataBuilder.VisaPaymentMethodId}");
        Assert.Contains(visaSaleA, visa);
        Assert.DoesNotContain(cashSaleA, visa);
        Assert.DoesNotContain(cashSaleB, visa);

        var bCash = await NumbersAsync($"&cashierUserId={cashierBId}&paymentMethodId={TestDataBuilder.CashPaymentMethodId}");
        Assert.Equal(new[] { cashSaleB }, bCash);

        // خيارات الفلاتر: الكاشيرية اللي إلهم فواتير + طرق الدفع (بصلاحية البيع، بلا Users.Manage)
        var options = await OkJsonAsync(await clientA.GetAsync("/api/v1/sales/filter-options"), "خيارات");
        var cashierIds = options.GetProperty("cashiers").EnumerateArray().Select(c => c.GetProperty("id").GetGuid()).ToList();
        Assert.Contains(cashierAId, cashierIds);
        Assert.Contains(cashierBId, cashierIds);
        Assert.Contains(options.GetProperty("paymentMethods").EnumerateArray(), m => m.GetProperty("id").GetGuid() == TestDataBuilder.VisaPaymentMethodId);
    }
}
