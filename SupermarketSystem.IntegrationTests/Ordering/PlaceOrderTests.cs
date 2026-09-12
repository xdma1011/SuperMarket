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
/// OrderingEndpoints.cs). أغلب الاختبارات هون بتستخدم HttpClient مسجَّل
/// دخول (Master Admin) رغم إن الـendpoint AllowAnonymous - سبب توثيقي
/// إلزامي:
///
/// ═══════════════════════════════════════════════════════════════════
/// خطأ إنتاج حقيقي مكتشَف هون: PlaceOrder (وأي endpoint آخر AllowAnonymous
/// بيلمس كيان IBranchOwned) ما بيشتغل فعليًا لطلب مجهول الهوية حقيقي.
/// ═══════════════════════════════════════════════════════════════════
/// راجع اختبار تقديم_طلب_مجهول_الهوية_يفشل_فعليًا_بسبب_فلتر_الفرع_العام
/// بالأسفل لإثبات وتوثيق كامل. لهذا السبب بالتحديد، بقية اختبارات هذا
/// الملف بتستخدم توكن Master Admin (صلاحية CrossBranchAccess بتتجاوز
/// الفلتر) لتقدر فعليًا تفحص منطق PlaceOrderHandler نفسه (التحقق، حساب
/// السعر، حظر الزبون...) بمعزل عن هذه الفجوة المعمارية المنفصلة تمامًا.
/// </summary>
[Collection(DatabaseCollection.Name)]
public sealed class PlaceOrderTests : IntegrationTestBase
{
    public PlaceOrderTests(DatabaseFixture fixture) : base(fixture) { }

    [Fact]
    public async Task تقديم_طلب_مجهول_الهوية_يفشل_فعليًا_بسبب_فلتر_الفرع_العام()
    {
        // ═══════════════════════════════════════════════════════════════
        // توثيق خطأ إنتاج حقيقي (لم نُصلحه - غير تافه، قرار معماري):
        // ═══════════════════════════════════════════════════════════════
        // AppDbContext.SetBranchFilter (راجع Persistence/AppDbContext.cs)
        // يطبّق فلتر عام على كل كيان IBranchOwned:
        //   e => _isCrossBranchAccessAllowed || e.BranchId == _currentBranchId
        // و_currentBranchId يُقرأ من ICurrentUserContext.BranchId، اللي
        // بدوره claim من الـJWT (RealCurrentUserContext). طلب بلا توكن
        // إطلاقًا (AllowAnonymous فعليًا، لا مستخدم مسجَّل دخول) يعني
        // _currentBranchId = null و_isCrossBranchAccessAllowed = false -
        // فالفلتر يصير (false || e.BranchId == null)، وهذا ما يتحقق أبدًا
        // لأي صف ProductBranch/Order حقيقي (BranchId عندهم قيمة فعلية دومًا).
        //
        // النتيجة: PlaceOrderHandler (المصمَّم عمدًا AllowAnonymous - هذا
        // بالضبط سلوك تطبيق الزبائن الحقيقي المتوقَّع اليوم قبل بناء
        // مصادقة الزبون - راجع تحذير OrderingEndpoints.cs) ما بيقدر يلقى
        // ولا صف ProductBranch واحد مهما كان الصنف متوفرًا فعليًا بالفرع -
        // كل طلب مجهول الهوية برجع "Order.ProductNotAvailable" دائمًا،
        // بغض النظر عن البيانات الفعلية. نفس المشكلة تنطبق على
        // GetPublicCatalogHandler وGetCustomerOrdersHandler/GetOrderByIdHandler
        // (endpoint العميل customer-view)/RateOrderHandler - كلها بتلمس
        // Order أو ProductBranch (كيانات IBranchOwned) وAllowAnonymous بنفس
        // الوقت.
        //
        // الإصلاح الصحيح خارج نطاق هذه المهمة (قرار معماري: يحتاج إما
        // IgnoreQueryFilters() صريح بكل هالـhandlers مع تبرير أمني واضح
        // ليش هذا آمن هون تحديدًا، أو مصادقة زبون حقيقية تحقن BranchId
        // بالتوكن - المذكورة كخطوة قادمة غير مبنية بعد بتعليقات الكود
        // نفسها). ذُكر لصاحب المشروع بالتقرير النهائي بالتفصيل.
        var (productId, unitId, _) = await OrderingTestDataHelper.CreateSellableProductAsync(
            Fixture.Factory.Services, Fixture.TestBranchId);

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
        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        Assert.Contains("Order.ProductNotAvailable", body);
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
