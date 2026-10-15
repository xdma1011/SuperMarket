using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using SupermarketSystem.IntegrationTests.Common;
using Xunit;

namespace SupermarketSystem.IntegrationTests.Catalog;

/// <summary>
/// باركودات إضافية لنفس الوحدة (9/10/2026): نفس الصنف بباركودات مختلفة (بلد تصنيع/موزّع/طبعة) = نفس الصنف والسعر والمخزون.
/// الباركود فريد بكل النظام، وكل تغيير بيزيد رقم نسخة الكتالوج عشان الكاشير يسحبه.
/// </summary>
[Collection(DatabaseCollection.Name)]
public sealed class ExtraBarcodesHttpTests : IntegrationTestBase
{
    public ExtraBarcodesHttpTests(DatabaseFixture fixture) : base(fixture) { }

    private async Task<(Guid ProductId, Guid UnitId, string PrimaryBarcode)> SeedAsync(string name, string primary)
    {
        Guid productId, unitId;
        using (var scope = CreateScope())
        {
            await TestDataBuilder.ActAsAdminAsync(scope, Fixture);
            var db = CreateDbContext(scope);
            // Respawn بيصفّر SystemSettings - الصف مبذور بالـMigrations بالإنتاج (نفس معالجة CashierSyncTests).
            if (!await db.SystemSettings.AnyAsync(s => s.Key == "Catalog.Version"))
            {
                db.SystemSettings.Add(new SupermarketSystem.Domain.Settings.SystemSetting("Catalog.Version", "1", null));
                await db.SaveChangesAsync();
            }

            var category = await TestDataBuilder.CreateCategoryAsync(db);
            var product = new SupermarketSystem.Domain.Catalog.Product(name, category.Id, false);
            product.ChangeStatus(SupermarketSystem.Domain.Catalog.ProductStatus.Active);
            var unit = product.AddUnit("حبة", 1m, isBaseUnit: true);
            product.AddBarcode(primary, unit.Id);
            db.Products.Add(product);
            await db.SaveChangesAsync();
            await TestDataBuilder.CreateProductBranchAsync(db, product.Id, Fixture.TestBranchId, 1.250m);
            await TestDataBuilder.SetStockAsync(db, product.Id, Fixture.TestBranchId, 50m);
            productId = product.Id;
            unitId = unit.Id;
        }

        return (productId, unitId, primary);
    }

    private static async Task<JsonElement> JsonAsync(HttpResponseMessage response)
    {
        var body = await response.Content.ReadAsStringAsync();
        Assert.True(response.IsSuccessStatusCode, $"{(int)response.StatusCode} {body}");
        return JsonDocument.Parse(body).RootElement;
    }

    private static async Task<long> CatalogVersionAsync(HttpClient client) =>
        (await JsonAsync(await client.GetAsync("/api/v1/cashier-sync/catalog-version"))).GetProperty("version").GetInt64();

    [Fact]
    public async Task باركود_إضافي_بيفتح_نفس_الصنف_وبيطلع_بالوحدات_وبيزيد_نسخة_الكتالوج()
    {
        var (productId, unitId, primary) = await SeedAsync("باونتي", "5000159000001");
        var admin = await CreateAuthenticatedClientAsync();
        var versionBefore = await CatalogVersionAsync(admin);

        var added = await JsonAsync(await admin.PostAsJsonAsync($"/api/v1/products/{productId}/units/{unitId}/barcodes", new { barcodeValue = "  6291003000002  " }));
        var extraId = added.GetProperty("barcodeId").GetGuid();
        Assert.True(await CatalogVersionAsync(admin) > versionBefore, "إضافة باركود لازم تزيد نسخة الكتالوج");

        // نفس الصنف من الباركودين.
        foreach (var code in new[] { primary, "6291003000002" })
        {
            var found = await JsonAsync(await admin.GetAsync($"/api/v1/products/by-barcode/{code}"));
            Assert.Equal(productId, found.GetProperty("productId").GetGuid());
        }

        // الوحدات: الأساسي كما هو، والإضافي بقائمة منفصلة.
        var units = await JsonAsync(await admin.GetAsync($"/api/v1/products/{productId}/units"));
        var unit = units.EnumerateArray().Single();
        Assert.Equal(primary, unit.GetProperty("barcodeValue").GetString());
        var extras = unit.GetProperty("extraBarcodes").EnumerateArray().ToList();
        Assert.Single(extras);
        Assert.Equal("6291003000002", extras[0].GetProperty("value").GetString());

        // مزامنة الكاشير: الوحدة بتحمل الباركودين.
        var page = await JsonAsync(await admin.GetAsync($"/api/v1/cashier-sync/catalog-page?branchId={Fixture.TestBranchId}&pageNumber=1&pageSize=500"));
        var syncedUnit = page.GetProperty("items").EnumerateArray().Single(i => i.GetProperty("productId").GetGuid() == productId)
            .GetProperty("units").EnumerateArray().Single();
        var syncedBarcodes = syncedUnit.GetProperty("barcodes").EnumerateArray().Select(b => b.GetString()).ToList();
        Assert.Contains(primary, syncedBarcodes);
        Assert.Contains("6291003000002", syncedBarcodes);

        // حذف الإضافي: ما عاد بيفتح الصنف، والأساسي بيضل.
        var versionMid = await CatalogVersionAsync(admin);
        Assert.Equal(HttpStatusCode.NoContent, (await admin.DeleteAsync($"/api/v1/products/{productId}/barcodes/{extraId}")).StatusCode);
        Assert.True(await CatalogVersionAsync(admin) > versionMid);
        Assert.Equal(HttpStatusCode.NotFound, (await admin.GetAsync("/api/v1/products/by-barcode/6291003000002")).StatusCode);
        Assert.Equal(HttpStatusCode.OK, (await admin.GetAsync($"/api/v1/products/by-barcode/{primary}")).StatusCode);
    }

    [Fact]
    public async Task الباركود_فريد_بكل_النظام_ومرفوض_مكرر_أو_فاضي_أو_لوحدة_مش_موجودة()
    {
        var (productA, unitA, primaryA) = await SeedAsync("سنكرز أ", "5000159111111");
        var (productB, unitB, primaryB) = await SeedAsync("سنكرز ب", "5000159222222");
        var admin = await CreateAuthenticatedClientAsync();

        // باركود صنف تاني.
        var other = await admin.PostAsJsonAsync($"/api/v1/products/{productA}/units/{unitA}/barcodes", new { barcodeValue = primaryB });
        Assert.Equal(HttpStatusCode.Conflict, other.StatusCode);
        Assert.Contains("لصنف آخر", await other.Content.ReadAsStringAsync());

        // نفس باركود الصنف نفسه.
        var same = await admin.PostAsJsonAsync($"/api/v1/products/{productA}/units/{unitA}/barcodes", new { barcodeValue = primaryA });
        Assert.Equal(HttpStatusCode.Conflict, same.StatusCode);
        Assert.Contains("هذا الصنف", await same.Content.ReadAsStringAsync());

        // إضافي مكرر بين صنفين.
        Assert.Equal(HttpStatusCode.OK, (await admin.PostAsJsonAsync($"/api/v1/products/{productA}/units/{unitA}/barcodes", new { barcodeValue = "7001" })).StatusCode);
        Assert.Equal(HttpStatusCode.Conflict, (await admin.PostAsJsonAsync($"/api/v1/products/{productB}/units/{unitB}/barcodes", new { barcodeValue = "7001" })).StatusCode);

        // فاضي، ووحدة مش للصنف، وصنف مش موجود.
        Assert.Equal(HttpStatusCode.BadRequest, (await admin.PostAsJsonAsync($"/api/v1/products/{productA}/units/{unitA}/barcodes", new { barcodeValue = "   " })).StatusCode);
        Assert.Equal(HttpStatusCode.NotFound, (await admin.PostAsJsonAsync($"/api/v1/products/{productA}/units/{unitB}/barcodes", new { barcodeValue = "7002" })).StatusCode);
        Assert.Equal(HttpStatusCode.NotFound, (await admin.PostAsJsonAsync($"/api/v1/products/{Guid.NewGuid()}/units/{unitA}/barcodes", new { barcodeValue = "7003" })).StatusCode);

        // حذف باركود مش لهذا الصنف.
        var units = await JsonAsync(await admin.GetAsync($"/api/v1/products/{productA}/units"));
        var extraOfA = units.EnumerateArray().Single().GetProperty("extraBarcodes").EnumerateArray().Single().GetProperty("id").GetGuid();
        Assert.Equal(HttpStatusCode.NotFound, (await admin.DeleteAsync($"/api/v1/products/{productB}/barcodes/{extraOfA}")).StatusCode);
    }

    [Fact]
    public async Task الكاشير_ما_بيقدر_يضيف_باركود_والبيع_بالباركود_الإضافي_عبر_مساعد_الكاشير_بيلاقي_الصنف()
    {
        var (productId, unitId, _) = await SeedAsync("شوكولاتة مستوردة", "5000159333333");
        var admin = await CreateAuthenticatedClientAsync();
        await JsonAsync(await admin.PostAsJsonAsync($"/api/v1/products/{productId}/units/{unitId}/barcodes", new { barcodeValue = "9300000000001" }));

        var (_, username) = await UsersTestDataHelper.CreateUserWithRoleReturningUsernameAsync(
            Fixture.Factory.Services, Fixture.TestBranchId, UsersTestDataHelper.CashierRoleId, "extra.barcode");
        var cashier = await LoginHelper.LoginAsAsync(Fixture, username, UsersTestDataHelper.DefaultPassword, appType: "Cashier");

        Assert.Equal(HttpStatusCode.Forbidden,
            (await cashier.PostAsJsonAsync($"/api/v1/products/{productId}/units/{unitId}/barcodes", new { barcodeValue = "9300000000002" })).StatusCode);

        var lookup = await cashier.GetAsync($"/api/v1/sales/scan-lookup?branchId={Fixture.TestBranchId}&term=9300000000001");
        var body = await lookup.Content.ReadAsStringAsync();
        Assert.True(lookup.IsSuccessStatusCode, body);
        Assert.Contains("شوكولاتة مستوردة", body);
    }
}
