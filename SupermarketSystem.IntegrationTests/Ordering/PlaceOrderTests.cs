using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using SupermarketSystem.Application.System.UpdateAdminSetting;
using Xunit;

namespace SupermarketSystem.IntegrationTests.Ordering;

/// <summary>
/// PlaceOrder مُعفى من المصادقة عمدًا (AllowAnonymous - راجع تحذير
/// OrderingEndpoints.cs).
///
/// ═══════════════════════════════════════════════════════════════════
/// خطأ إنتاج حقيقي انلقى هون، وانصلح: PlaceOrder (وأي endpoint آخر
/// AllowAnonymous بيلمس كيان IBranchOwned) ما كان يشتغل إطلاقًا لطلب
/// مجهول الهوية حقيقي - فلتر الفرع العام (AppDbContext.SetBranchFilter)
/// بيقرأ BranchId من claim JWT غير موجود أصلًا لطلب مجهول، فيحجب كل صف
/// ProductBranch/Order دائمًا. الإصلاح: IgnoreQueryFilters() صريح
/// بـPlaceOrderCommand.cs (وGetCustomerOrdersQuery/RateOrderCommand/
/// GetOrderByIdQuery بنفس السبب بالضبط) - آمن لأن كل واحد منهم مقيَّد
/// أصلًا بـWhere صريح (BranchId أو CustomerId أو OrderId معروف)، فما في
/// أي تسريب بيانات فرع تاني.
/// ═══════════════════════════════════════════════════════════════════
/// راجع اختبار تقديم_طلب_مجهول_الهوية_ينجح_فعليًا_بعد_إصلاح_فلتر_الفرع_العام
/// بالأسفل لإثبات الإصلاح.
/// </summary>
[Collection(DatabaseCollection.Name)]
public sealed class PlaceOrderTests : IntegrationTestBase
{
    public PlaceOrderTests(DatabaseFixture fixture) : base(fixture) { }

    [Fact]
    public async Task تقديم_طلب_مجهول_الهوية_ينجح_فعليًا_بعد_إصلاح_فلتر_الفرع_العام()
    {
        var (productId, unitId, _) = await OrderingTestDataHelper.CreateSellableProductAsync(
            Fixture.Factory.Services, Fixture.TestBranchId, sellingPrice: 5m);

        var anonymousClient = CreateAnonymousClient();

        var response = await anonymousClient.PostAsJsonAsync("/api/v1/orders", new
        {
            CustomerPhone = "0790000099",
            CustomerName = (string?)null,
            BranchId = Fixture.TestBranchId,
            DeliveryNote = (string?)null,
            DeliveryLatitude = (decimal?)null,
            DeliveryLongitude = (decimal?)null,
            Items = new[] { new { ProductId = productId, ProductUnitId = unitId, Quantity = 1m } }
        });

        var body = await response.Content.ReadAsStringAsync();
        Assert.True(response.StatusCode == HttpStatusCode.Created, $"توقعنا 201 ورجع {response.StatusCode}: {body}");

        var json = JsonDocument.Parse(body).RootElement;
        Assert.Equal(5m, json.GetProperty("estimatedTotal").GetDecimal());
    }

    [Fact]
    public async Task تقديم_طلب_بصنف_متوفر_ينجح_ويرجع_سعرًا_تقديريًا_صحيحًا()
    {
        var (productId, unitId, _) = await OrderingTestDataHelper.CreateSellableProductAsync(
            Fixture.Factory.Services, Fixture.TestBranchId, sellingPrice: 3m);

        // راجع تعليق الصنف - نستخدم توكن Master Admin (CrossBranchAccess)
        // لنتجاوز فجوة الفلتر الموثَّقة أعلاه ونفحص منطق الـhandler نفسه.
        var client = await CreateAuthenticatedClientAsync();

        var response = await client.PostAsJsonAsync("/api/v1/orders", new
        {
            CustomerPhone = "0790000001",
            CustomerName = "زبون اختبار",
            BranchId = Fixture.TestBranchId,
            DeliveryNote = (string?)null,
            DeliveryLatitude = (decimal?)null,
            DeliveryLongitude = (decimal?)null,
            Items = new[] { new { ProductId = productId, ProductUnitId = unitId, Quantity = 2m } }
        });

        var body = await response.Content.ReadAsStringAsync();
        Assert.True(response.StatusCode == HttpStatusCode.Created, $"توقعنا 201 ورجع {response.StatusCode}: {body}");

        var json = JsonDocument.Parse(body).RootElement;
        Assert.Equal(6m, json.GetProperty("estimatedTotal").GetDecimal());
        Assert.NotEqual(Guid.Empty, json.GetProperty("orderId").GetGuid());
    }

    [Fact]
    public async Task تقديم_طلب_بلا_أصناف_يفشل_بخطأ_تحقق_400()
    {
        var client = CreateAnonymousClient();

        var response = await client.PostAsJsonAsync("/api/v1/orders", new
        {
            CustomerPhone = "0790000002",
            CustomerName = (string?)null,
            BranchId = Fixture.TestBranchId,
            DeliveryNote = (string?)null,
            DeliveryLatitude = (decimal?)null,
            DeliveryLongitude = (decimal?)null,
            Items = Array.Empty<object>()
        });

        var body = await response.Content.ReadAsStringAsync();
        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        Assert.Contains("Order.ItemsRequired", body);
    }

    [Fact]
    public async Task تقديم_طلب_لفرع_غير_موجود_يفشل_بـ404()
    {
        var (productId, unitId, _) = await OrderingTestDataHelper.CreateSellableProductAsync(
            Fixture.Factory.Services, Fixture.TestBranchId);

        var client = CreateAnonymousClient();

        var response = await client.PostAsJsonAsync("/api/v1/orders", new
        {
            CustomerPhone = "0790000003",
            CustomerName = (string?)null,
            BranchId = Guid.NewGuid(),
            DeliveryNote = (string?)null,
            DeliveryLatitude = (decimal?)null,
            DeliveryLongitude = (decimal?)null,
            Items = new[] { new { ProductId = productId, ProductUnitId = unitId, Quantity = 1m } }
        });

        Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
    }

    [Fact]
    public async Task تقديم_طلب_من_زبون_محظور_يُرفض_بـ403()
    {
        var (productId, unitId, _) = await OrderingTestDataHelper.CreateSellableProductAsync(
            Fixture.Factory.Services, Fixture.TestBranchId);

        const string phone = "0790000004";
        var client = await CreateAuthenticatedClientAsync();

        // أول طلب ينشئ الزبون تلقائيًا (نفس نمط PlaceOrderHandler الموثَّق).
        var first = await client.PostAsJsonAsync("/api/v1/orders", new
        {
            CustomerPhone = phone,
            CustomerName = (string?)null,
            BranchId = Fixture.TestBranchId,
            DeliveryNote = (string?)null,
            DeliveryLatitude = (decimal?)null,
            DeliveryLongitude = (decimal?)null,
            Items = new[] { new { ProductId = productId, ProductUnitId = unitId, Quantity = 1m } }
        });
        Assert.Equal(HttpStatusCode.Created, first.StatusCode);

        using (var scope = CreateScope())
        {
            var db = CreateDbContext(scope);
            var customer = await db.Customers.IgnoreQueryFilters().FirstAsync(c => c.Phone == phone);
            customer.Block();
            await db.SaveChangesAsync();
        }

        var second = await client.PostAsJsonAsync("/api/v1/orders", new
        {
            CustomerPhone = phone,
            CustomerName = (string?)null,
            BranchId = Fixture.TestBranchId,
            DeliveryNote = (string?)null,
            DeliveryLatitude = (decimal?)null,
            DeliveryLongitude = (decimal?)null,
            Items = new[] { new { ProductId = productId, ProductUnitId = unitId, Quantity = 1m } }
        });

        Assert.Equal(HttpStatusCode.Forbidden, second.StatusCode);
    }

    [Fact]
    public async Task تقديم_طلب_أقل_من_الحد_الأدنى_المسموح_يفشل_بالسبب_الصحيح()
    {
        var (productId, unitId, _) = await OrderingTestDataHelper.CreateSellableProductAsync(
            Fixture.Factory.Services, Fixture.TestBranchId, sellingPrice: 1m);

        using var scope = CreateScope();
        var updateSettingHandler = scope.ServiceProvider.GetRequiredService<UpdateAdminSettingHandler>();

        var setResult = await updateSettingHandler.HandleAsync(
            new UpdateAdminSettingCommand("Ordering.MinimumOrderAmount", "10"), CancellationToken.None);
        Assert.True(setResult.IsSuccess);

        try
        {
            var client = await CreateAuthenticatedClientAsync();
            var response = await client.PostAsJsonAsync("/api/v1/orders", new
            {
                CustomerPhone = "0790000005",
                CustomerName = (string?)null,
                BranchId = Fixture.TestBranchId,
                DeliveryNote = (string?)null,
                DeliveryLatitude = (decimal?)null,
                DeliveryLongitude = (decimal?)null,
                Items = new[] { new { ProductId = productId, ProductUnitId = unitId, Quantity = 1m } }
            });

            var body = await response.Content.ReadAsStringAsync();
            Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
            Assert.Contains("Order.BelowMinimumAmount", body);
        }
        finally
        {
            // إلزامي: نرجّع الإعداد لقيمته الافتراضية (0 = بلا حد أدنى) -
            // ISettingsProvider مبني على IMemoryCache Singleton بعمر
            // التشغيلة كلها (راجع CachedSettingsProvider)، ما بينصفّر مع
            // Respawn بين الاختبارات، فقيمة متروكة هون بتؤثر على أي اختبار
            // Ordering تاني يشتغل بعده بنفس التشغيلة.
            await updateSettingHandler.HandleAsync(
                new UpdateAdminSettingCommand("Ordering.MinimumOrderAmount", "0"), CancellationToken.None);
        }
    }
}
