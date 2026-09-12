using System.Net;
using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using Xunit;

namespace SupermarketSystem.IntegrationTests.CashierSync;

/// <summary>
/// ProductBranch/ProductBatch/Stock كيانات IBranchOwned - كل الاختبارات
/// هون عبر HTTP بتوكن Master Admin (CrossBranchAccess، راجع تعليق
/// PlaceOrderTests بمجلد Ordering للتفصيل الكامل عن فلتر الفرع العام).
/// </summary>
[Collection(DatabaseCollection.Name)]
public sealed class CashierSyncTests : IntegrationTestBase
{
    public CashierSyncTests(DatabaseFixture fixture) : base(fixture) { }

    [Fact]
    public async Task رقم_نسخة_الكتالوج_يزيد_فعليًا_بعد_إنشاء_منتج_جديد()
    {
        // ⚠️ ملاحظة بنية اختبار (لا علاقة بكود الإنتاج - راجع تفصيل كامل
        // بتقرير الجلسة): صف SystemSettings["Catalog.Version"] مبذور
        // بالـmigration الأولى (قيمة ابتدائية "1")، بس جدول SystemSettings
        // بالكامل **غير مستثنى** من تصفير Respawn بين الاختبارات
        // (DatabaseFixture.TablesToIgnore ما يشمله) - فالصف المبذور
        // بينمسح كل اختبار. SqlCatalogVersionService.IncrementVersionAsync
        // جملة UPDATE خام بلا INSERT احتياطي لو الصف مفقود - فلو الصف مو
        // موجود، الزيادة "تنجح" بصمت بلا ما تأثّر ولا صف (0 rows affected،
        // بلا استثناء)، والنسخة تضل 0 للأبد. هذا احتمال حقيقي بالإنتاج
        // كمان لو انحذف هذا الصف بالذات (لا فقط بيئة الاختبار) - فجوة
        // متانة صغيرة موثَّقة بالتقرير، لم تُصلَح (تغيير تصميم يحتاج قرار
        // - INSERT-if-missing بيكسر ذرّية UPDATE الحالية). هون بس نضمن
        // وجود الصف قبل الاختبار (محاكاة نفس بذر الـmigration تمامًا).
        using (var seedScope = CreateScope())
        {
            var seedDb = CreateDbContext(seedScope);
            if (!await seedDb.SystemSettings.AnyAsync(s => s.Key == "Catalog.Version"))
            {
                seedDb.SystemSettings.Add(new SupermarketSystem.Domain.Settings.SystemSetting("Catalog.Version", "1", null));
                await seedDb.SaveChangesAsync();
            }
        }

        var client = await CreateAuthenticatedClientAsync();

        var beforeResponse = await client.GetAsync("/api/v1/cashier-sync/catalog-version");
        beforeResponse.EnsureSuccessStatusCode();
        var before = JsonDocument.Parse(await beforeResponse.Content.ReadAsStringAsync()).RootElement.GetProperty("version").GetInt64();

        await Ordering.OrderingTestDataHelper.CreateSellableProductAsync(Fixture.Factory.Services, Fixture.TestBranchId);

        var afterResponse = await client.GetAsync("/api/v1/cashier-sync/catalog-version");
        var after = JsonDocument.Parse(await afterResponse.Content.ReadAsStringAsync()).RootElement.GetProperty("version").GetInt64();

        Assert.True(after > before, $"توقعنا زيادة النسخة، قبل={before} بعد={after}");
    }

    [Fact]
    public async Task صفحة_مزامنة_الكتالوج_تُرجع_المنتج_بسعره_ووحدته_الأساسية_فعليًا()
    {
        var (productId, unitId, price) = await Ordering.OrderingTestDataHelper.CreateSellableProductAsync(
            Fixture.Factory.Services, Fixture.TestBranchId, sellingPrice: 7.5m);

        var client = await CreateAuthenticatedClientAsync();
        var response = await client.GetAsync($"/api/v1/cashier-sync/catalog-page?branchId={Fixture.TestBranchId}&pageNumber=1&pageSize=200");
        response.EnsureSuccessStatusCode();

        var json = JsonDocument.Parse(await response.Content.ReadAsStringAsync()).RootElement;
        var items = json.GetProperty("items").EnumerateArray().ToList();
        var product = items.Single(i => i.GetProperty("productId").GetGuid() == productId);

        Assert.Equal(7.5m, product.GetProperty("sellingPrice").GetDecimal());
        Assert.True(product.GetProperty("isAvailableForSale").GetBoolean());

        var units = product.GetProperty("units").EnumerateArray().ToList();
        Assert.Single(units);
        Assert.Equal(unitId, units[0].GetProperty("unitId").GetGuid());
        Assert.True(units[0].GetProperty("isBaseUnit").GetBoolean());
    }

    [Fact]
    public async Task منتج_غير_متاح_للبيع_بالفرع_يظهر_بعلامة_IsAvailableForSale_false()
    {
        var (productId, _, _) = await Ordering.OrderingTestDataHelper.CreateSellableProductAsync(
            Fixture.Factory.Services, Fixture.TestBranchId);

        using (var scope = CreateScope())
        {
            var db = CreateDbContext(scope);
            var productBranch = await db.ProductBranches
                .IgnoreQueryFilters()
                .SingleAsync(pb => pb.ProductId == productId && pb.BranchId == Fixture.TestBranchId);
            productBranch.MakeUnavailable();
            await db.SaveChangesAsync();
        }

        var client = await CreateAuthenticatedClientAsync();
        var response = await client.GetAsync($"/api/v1/cashier-sync/catalog-page?branchId={Fixture.TestBranchId}&pageNumber=1&pageSize=200");
        var json = JsonDocument.Parse(await response.Content.ReadAsStringAsync()).RootElement;
        var product = json.GetProperty("items").EnumerateArray().Single(i => i.GetProperty("productId").GetGuid() == productId);

        Assert.False(product.GetProperty("isAvailableForSale").GetBoolean());
    }

    [Fact]
    public async Task مجموعة_مزامنة_الكاشير_محمية_401_بلا_توكن()
    {
        var client = CreateAnonymousClient();
        var response = await client.GetAsync("/api/v1/cashier-sync/catalog-version");
        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
    }

    [Fact]
    public async Task سائق_بلا_صلاحية_Sales_Create_يُرفض_بـ403()
    {
        var (_, username) = await UsersTestDataHelper.CreateUserWithRoleReturningUsernameAsync(
            Fixture.Factory.Services, Fixture.TestBranchId, UsersTestDataHelper.DriverRoleId, "driver.cashiersync.test");

        var driverClient = await LoginHelper.LoginAsAsync(Fixture, username, UsersTestDataHelper.DefaultPassword);

        var response = await driverClient.GetAsync("/api/v1/cashier-sync/catalog-version");
        Assert.Equal(HttpStatusCode.Forbidden, response.StatusCode);
    }
}
