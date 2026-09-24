using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using SupermarketSystem.IntegrationTests.Common;
using Xunit;

namespace SupermarketSystem.IntegrationTests.Reporting;

/// <summary>
/// أخطاء تقارير انمسكت باختبار "يوم كامل بالمحل" وشاشة الإدارة الفعلية (24/9/2026) - كل واحد هون
/// عبر HTTP بنفس الشكل اللي الواجهة بتبعته.
/// </summary>
[Collection(DatabaseCollection.Name)]
public sealed class ReportFixesHttpTests : IntegrationTestBase
{
    public ReportFixesHttpTests(DatabaseFixture fixture) : base(fixture) { }

    private static string Iso(DateTime value) => Uri.EscapeDataString(value.ToString("o"));

    private async Task<Guid> SellAsync(HttpClient admin, Guid productId, Guid unitId, decimal quantity, decimal amount)
    {
        var response = await admin.PostAsJsonAsync("/api/v1/sales", new
        {
            branchId = Fixture.TestBranchId, clientRequestId = Guid.NewGuid(), customerId = (Guid?)null,
            invoiceLevelDiscountAmount = 0m,
            items = new[] { new { productId, productUnitId = unitId, quantity, manualDiscountAmount = 0m, productBatchId = (Guid?)null } },
            payments = new[] { new { paymentMethodId = TestDataBuilder.CashPaymentMethodId, amount, externalReference = (string?)null, clientRequestId = Guid.NewGuid() } }
        });
        var body = await response.Content.ReadAsStringAsync();
        Assert.True(response.IsSuccessStatusCode, body);
        return JsonDocument.Parse(body).RootElement.GetProperty("saleInvoiceId").GetGuid();
    }

    [Fact]
    public async Task الاستهلاك_والركود_بيقبلوا_فترة_من_إلى_زي_الواجهة_وبيستثنوا_الملغاة_وبيحسبوا_بالوحدة_الأساسية()
    {
        Guid milkId, milkUnit, cartonUnit, voidedOnlyId, voidedOnlyUnit;
        using (var scope = CreateScope())
        {
            var db = CreateDbContext(scope);
            await TestDataBuilder.EnsureDocumentSequencesProvisionedAsync(db, Fixture.TestBranchId);

            // الوحدتين قبل أول حفظ - إضافة وحدة لمنتج محفوظ بنفس الـcontext بتتعامل معها EF كتعديل.
            var category = await TestDataBuilder.CreateCategoryAsync(db, "ألبان");
            var milk = new SupermarketSystem.Domain.Catalog.Product("حليب بكراتين", category.Id, false);
            var unit = milk.AddUnit("حبة", 1m, isBaseUnit: true);
            var carton = milk.AddUnit("كرتونة", 12m, isBaseUnit: false);
            db.Products.Add(milk);
            await db.SaveChangesAsync();
            await TestDataBuilder.CreateProductBranchAsync(db, milk.Id, Fixture.TestBranchId, 1m);
            await TestDataBuilder.SetStockAsync(db, milk.Id, Fixture.TestBranchId, 100m);
            (milkId, milkUnit, cartonUnit) = (milk.Id, unit.Id, carton.Id);

            var (voidedOnly, voidedUnit) = await TestDataBuilder.CreateActiveProductAsync(db, "صنف بيعته انلغت");
            await TestDataBuilder.CreateProductBranchAsync(db, voidedOnly.Id, Fixture.TestBranchId, 2m);
            await TestDataBuilder.SetStockAsync(db, voidedOnly.Id, Fixture.TestBranchId, 10m);
            (voidedOnlyId, voidedOnlyUnit) = (voidedOnly.Id, voidedUnit.Id);
        }

        var admin = await CreateAuthenticatedClientAsync();
        await SellAsync(admin, milkId, cartonUnit, 1m, 12m);    // كرتونة = 12 حبة
        await SellAsync(admin, milkId, milkUnit, 3m, 3m);       // 3 حبات
        var voidedSale = await SellAsync(admin, voidedOnlyId, voidedOnlyUnit, 2m, 4m);
        var voidResponse = await admin.PostAsJsonAsync($"/api/v1/sales/{voidedSale}/void", new { reason = "CashierError", notes = (string?)null });
        Assert.True(voidResponse.IsSuccessStatusCode, await voidResponse.Content.ReadAsStringAsync());

        // نفس الـquery string اللي شاشة التقارير بتبعته (من/إلى).
        var range = $"branchId={Fixture.TestBranchId}&fromUtc={Iso(DateTime.UtcNow.AddDays(-30))}&toUtc={Iso(DateTime.UtcNow.AddDays(1))}";

        var consumptionResponse = await admin.GetAsync($"/api/v1/reports/products/consumption-levels?{range}");
        Assert.Equal(HttpStatusCode.OK, consumptionResponse.StatusCode);
        var consumption = JsonDocument.Parse(await consumptionResponse.Content.ReadAsStringAsync()).RootElement
            .GetProperty("items").EnumerateArray().ToList();
        decimal Sold(Guid id) => consumption.Single(c => c.GetProperty("productId").GetGuid() == id).GetProperty("quantitySold").GetDecimal();
        Assert.Equal(15m, Sold(milkId));      // 12 + 3 بالحبة، لا 1 + 3
        Assert.Equal(0m, Sold(voidedOnlyId)); // الملغاة مش مبيعات

        var stagnantResponse = await admin.GetAsync($"/api/v1/reports/products/stagnant?{range}");
        Assert.Equal(HttpStatusCode.OK, stagnantResponse.StatusCode);
        var stagnantIds = JsonDocument.Parse(await stagnantResponse.Content.ReadAsStringAsync()).RootElement
            .GetProperty("items").EnumerateArray().Select(s => s.GetProperty("productId").GetGuid()).ToList();
        Assert.Contains(voidedOnlyId, stagnantIds);   // بيعته الوحيدة انلغت = راكد
        Assert.DoesNotContain(milkId, stagnantIds);
    }

    [Fact]
    public async Task إعادة_الطلب_بلا_فرع_للرئيسية_وبتجمع_دفعات_المنتج_قبل_المقارنة()
    {
        Guid twoBatchesId;
        using (var scope = CreateScope())
        {
            var db = CreateDbContext(scope);
            var (product, _) = await TestDataBuilder.CreateActiveProductAsync(db, "لبن بدفعتين", isBatchTracked: true);
            var productBranch = await TestDataBuilder.CreateProductBranchAsync(db, product.Id, Fixture.TestBranchId, 1m);
            productBranch.SetStockThresholds(minimumStock: 8m, maximumStock: null);
            await db.SaveChangesAsync();
            var batchA = await TestDataBuilder.CreateBatchAsync(db, product.Id, Fixture.TestBranchId, "A");
            var batchB = await TestDataBuilder.CreateBatchAsync(db, product.Id, Fixture.TestBranchId, "B");
            await TestDataBuilder.SetStockAsync(db, product.Id, Fixture.TestBranchId, 5m, batchA.Id);
            await TestDataBuilder.SetStockAsync(db, product.Id, Fixture.TestBranchId, 5m, batchB.Id);
            twoBatchesId = product.Id;
        }

        var admin = await CreateAuthenticatedClientAsync();

        // الرئيسية بتطلبه بلا فرع - كان 500.
        var noBranch = await admin.GetAsync("/api/v1/reports/products/reorder-needed?pageNumber=1&pageSize=1");
        Assert.Equal(HttpStatusCode.OK, noBranch.StatusCode);

        // 5 + 5 = 10 فوق الحد 8 - مش لازم يطلع (كان يطلع مرتين، كل دفعة لحالها).
        var withBranch = JsonDocument.Parse(await admin.GetStringAsync($"/api/v1/reports/products/reorder-needed?branchId={Fixture.TestBranchId}"))
            .RootElement.GetProperty("items").EnumerateArray().ToList();
        Assert.DoesNotContain(withBranch, r => r.GetProperty("productId").GetGuid() == twoBatchesId);
    }

    [Fact]
    public async Task بارامتر_إلزامي_ناقص_بيرجع_400_مش_500()
    {
        var admin = await CreateAuthenticatedClientAsync();

        var response = await admin.GetAsync("/api/v1/reports/suppliers/price-comparison?pageNumber=1&pageSize=20");

        // بالإنتاج ASP.NET بيرجع 400 لحاله؛ ببيئة التطوير كان بيرمي استثناء والـmiddleware يحوّله 500.
        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
    }
}
