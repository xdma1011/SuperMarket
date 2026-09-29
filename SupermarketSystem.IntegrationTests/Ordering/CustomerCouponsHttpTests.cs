using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using SupermarketSystem.Domain.Ordering;
using SupermarketSystem.IntegrationTests.Common;
using Xunit;

namespace SupermarketSystem.IntegrationTests.Ordering;

/// <summary>
/// كوبونات خصم تطبيق الزبائن (29/9/2026) عبر HTTP، بأرقام محسوبة:
///   منتج 2.000 · كوبون عام EID-2026 = 10% بسقف 1.000 على طلب 3.000 وأكتر، مرة لكل زبون · كوبون VIP-1 = 0.500 لأبو سامي بس.
///   أبو سامي بيطلب 2 حبة (4.000) بـEID → خصم تقديري 0.400. قبل التسليم السعر بيصير 2.500 → الخصم الفعلي على 5.000 = 0.500،
///   والفاتورة 4.500 (خصم الفاتورة = 0.500).
/// </summary>
[Collection(DatabaseCollection.Name)]
public sealed class CustomerCouponsHttpTests : IntegrationTestBase
{
    public CustomerCouponsHttpTests(DatabaseFixture fixture) : base(fixture) { }

    private const string PhoneA = "0795550001";
    private const string PhoneB = "0795550002";

    private static async Task<JsonElement> OkJsonAsync(HttpResponseMessage response, string what)
    {
        var body = await response.Content.ReadAsStringAsync();
        Assert.True(response.IsSuccessStatusCode, $"{what}: {(int)response.StatusCode} {body}");
        return string.IsNullOrWhiteSpace(body) ? default : JsonDocument.Parse(body).RootElement;
    }

    private static object CouponBody(string code, CouponDiscountType type, decimal value, decimal? cap, decimal minOrder, Guid? customerId) => new
    {
        code, title = $"كوبون {code}", discountType = (int)type, value, maxDiscountAmount = cap, minOrderAmount = minOrder,
        startAtUtc = DateTime.UtcNow.AddHours(-1), endAtUtc = DateTime.UtcNow.AddDays(7), customerId, maxUsesPerCustomer = 1,
        maxTotalUses = (int?)null
    };

    private Task<HttpResponseMessage> PlaceOrderAsync(HttpClient client, string phone, Guid productId, Guid unitId, decimal quantity, string? couponCode) =>
        client.PostAsJsonAsync("/api/v1/orders", new
        {
            customerPhone = phone, customerName = (string?)null, branchId = Fixture.TestBranchId, deliveryNote = (string?)null,
            deliveryLatitude = (decimal?)null, deliveryLongitude = (decimal?)null,
            items = new[] { new { productId, productUnitId = unitId, quantity } },
            couponCode
        });

    [Fact]
    public async Task كوبون_عام_ومخصص_حجز_مع_الطلب_ورجوع_مع_الرفض_وخصم_فعلي_بأسعار_التسليم()
    {
        Guid productId, unitId, customerA, customerB;
        using (var scope = CreateScope())
        {
            await TestDataBuilder.ActAsAdminAsync(scope, Fixture);
            var db = CreateDbContext(scope);
            var (product, unit) = await TestDataBuilder.CreateActiveProductAsync(db, "منتج كوبونات");
            await TestDataBuilder.CreateProductBranchAsync(db, product.Id, Fixture.TestBranchId, sellingPrice: 2.000m);
            await TestDataBuilder.SetStockAsync(db, product.Id, Fixture.TestBranchId, 100m);
            customerA = (await TestDataBuilder.CreateCustomerAsync(db, "أبو سامي", PhoneA)).Id;
            customerB = (await TestDataBuilder.CreateCustomerAsync(db, "أم علي", PhoneB)).Id;
            productId = product.Id;
            unitId = unit.Id;
        }

        var admin = await CreateAuthenticatedClientAsync();
        var anonymous = Fixture.Factory.CreateClient();

        // الإنشاء: كود غلط 400، مكرر 409
        Assert.Equal(HttpStatusCode.BadRequest,
            (await admin.PostAsJsonAsync("/api/v1/coupons", CouponBody("a b", CouponDiscountType.FixedAmount, 1m, null, 0m, null))).StatusCode);
        var eid = (await OkJsonAsync(await admin.PostAsJsonAsync("/api/v1/coupons",
            CouponBody("eid-2026", CouponDiscountType.Percent, 10m, 1.000m, 3.000m, null)), "كوبون عام")).GetProperty("couponId").GetGuid();
        Assert.Equal(HttpStatusCode.Conflict,
            (await admin.PostAsJsonAsync("/api/v1/coupons", CouponBody("EID-2026", CouponDiscountType.FixedAmount, 1m, null, 0m, null))).StatusCode);
        var vip = (await OkJsonAsync(await admin.PostAsJsonAsync("/api/v1/coupons",
            CouponBody("VIP-1", CouponDiscountType.FixedAmount, 0.500m, null, 0m, customerA)), "كوبون مخصص")).GetProperty("couponId").GetGuid();

        // كوبونات كل زبون بالتطبيق
        var couponsA = await OkJsonAsync(await anonymous.GetAsync($"/api/v1/customers/{customerA}/coupons"), "كوبونات أبو سامي");
        Assert.Equal(2, couponsA.GetArrayLength());
        var couponsB = await OkJsonAsync(await anonymous.GetAsync($"/api/v1/customers/{customerB}/coupons"), "كوبونات أم علي");
        Assert.Equal("EID-2026", Assert.Single(couponsB.EnumerateArray()).GetProperty("code").GetString());

        // فحص قبل الطلب
        var preview = await OkJsonAsync(await anonymous.PostAsJsonAsync($"/api/v1/customers/{customerA}/coupons/preview",
            new { code = "EID-2026", estimatedTotal = 4.000m }), "فحص");
        Assert.Equal(0.400m, preview.GetProperty("estimatedDiscount").GetDecimal());

        // تحت الحد الأدنى، وكوبون مخصص لغيرك = مرفوض
        Assert.Equal(HttpStatusCode.BadRequest, (await PlaceOrderAsync(anonymous, PhoneB, productId, unitId, 1m, "EID-2026")).StatusCode);
        Assert.Equal(HttpStatusCode.BadRequest, (await PlaceOrderAsync(anonymous, PhoneB, productId, unitId, 2m, "VIP-1")).StatusCode);

        // الطلب بالكوبون (بحروف صغيرة)
        var placed = await OkJsonAsync(await PlaceOrderAsync(anonymous, PhoneA, productId, unitId, 2m, "eid-2026"), "طلب بكوبون");
        var orderId = placed.GetProperty("orderId").GetGuid();
        Assert.Equal(4.000m, placed.GetProperty("estimatedTotal").GetDecimal());
        Assert.Equal(0.400m, placed.GetProperty("couponDiscount").GetDecimal());

        // نفس الكوبون مرة تانية = مستعمل (محجوز مع طلب مفتوح)
        Assert.Equal(HttpStatusCode.BadRequest, (await PlaceOrderAsync(anonymous, PhoneA, productId, unitId, 2m, "EID-2026")).StatusCode);

        // قائمة الطلبات بالإدارة بتبين الكوبون والخصم التقديري
        var pending = await OkJsonAsync(await admin.GetAsync($"/api/v1/orders?branchId={Fixture.TestBranchId}"), "الطلبات");
        var listed = pending.GetProperty("items").EnumerateArray().Single(o => o.GetProperty("id").GetGuid() == orderId);
        Assert.Equal("EID-2026", listed.GetProperty("couponCode").GetString());
        Assert.Equal(0.400m, listed.GetProperty("estimatedCouponDiscount").GetDecimal());

        // السعر بيتغيّر قبل التسليم: الخصم بينحسب على سعر لحظة التسليم
        using (var scope = CreateScope())
        {
            await TestDataBuilder.ActAsAdminAsync(scope, Fixture);
            var db = CreateDbContext(scope);
            await db.Database.ExecuteSqlInterpolatedAsync(
                $"UPDATE ProductBranches SET SellingPrice = 2.500 WHERE ProductId = {productId} AND BranchId = {Fixture.TestBranchId}");
        }

        await OkJsonAsync(await admin.PostAsync($"/api/v1/orders/{orderId}/accept", null), "قبول");
        var completed = await OkJsonAsync(await admin.PostAsJsonAsync($"/api/v1/orders/{orderId}/complete", new
        {
            payments = new[] { new { paymentMethodId = TestDataBuilder.CashPaymentMethodId, amount = 4.500m, externalReference = (string?)null } },
            clientRequestId = Guid.NewGuid()
        }), "تسليم");
        Assert.Equal(4.500m, completed.GetProperty("totalAmount").GetDecimal());

        using (var scope = CreateScope())
        {
            await TestDataBuilder.ActAsAdminAsync(scope, Fixture);
            var db = CreateDbContext(scope);
            var redemption = await db.CouponRedemptions.AsNoTracking().SingleAsync(r => r.OrderId == orderId);
            Assert.Equal(CouponRedemptionStatus.Redeemed, redemption.Status);
            Assert.Equal(0.500m, redemption.DiscountAmount);
            var invoice = await db.SaleInvoices.AsNoTracking().SingleAsync(s => s.Id == redemption.SaleInvoiceId);
            Assert.Equal(0.500m, invoice.DiscountAmountSnapshot);
        }

        // رفض الطلب بيرجّع الكوبون: VIP-1 بعد الرفض بينفع مرة تانية
        var vipOrder = (await OkJsonAsync(await PlaceOrderAsync(anonymous, PhoneA, productId, unitId, 1m, "VIP-1"), "طلب VIP"))
            .GetProperty("orderId").GetGuid();
        Assert.Equal(HttpStatusCode.BadRequest, (await PlaceOrderAsync(anonymous, PhoneA, productId, unitId, 1m, "VIP-1")).StatusCode);
        await OkJsonAsync(await admin.PostAsJsonAsync($"/api/v1/orders/{vipOrder}/reject", new { reason = "الصنف خلص" }), "رفض");
        await OkJsonAsync(await anonymous.PostAsJsonAsync($"/api/v1/customers/{customerA}/coupons/preview",
            new { code = "VIP-1", estimatedTotal = 2.500m }), "VIP رجع بعد الرفض");

        // الإدارة: الاستعمال والخصم الفعلي
        var coupons = await OkJsonAsync(await admin.GetAsync("/api/v1/coupons"), "قائمة الكوبونات");
        var eidRow = coupons.EnumerateArray().Single(c => c.GetProperty("id").GetGuid() == eid);
        Assert.Equal(1, eidRow.GetProperty("redeemedCount").GetInt32());
        Assert.Equal(0.500m, eidRow.GetProperty("redeemedDiscountTotal").GetDecimal());
        var vipRow = coupons.EnumerateArray().Single(c => c.GetProperty("id").GetGuid() == vip);
        Assert.Equal(0, vipRow.GetProperty("reservedCount").GetInt32());
        Assert.Equal("أبو سامي", vipRow.GetProperty("customerName").GetString());

        // الإرسال اليدوي: العام للكل (زبونين)، المخصص لصاحبه بس، ولزبون تاني مرفوض
        var sentAll = await OkJsonAsync(await admin.PostAsJsonAsync($"/api/v1/coupons/{eid}/send", new { customerId = (Guid?)null }), "إرسال للكل");
        Assert.Equal(2, sentAll.GetProperty("customersTargeted").GetInt32());
        var sentOne = await OkJsonAsync(await admin.PostAsJsonAsync($"/api/v1/coupons/{eid}/send", new { customerId = customerB }), "إرسال لزبون");
        Assert.Equal(1, sentOne.GetProperty("customersTargeted").GetInt32());
        Assert.Equal(HttpStatusCode.BadRequest,
            (await admin.PostAsJsonAsync($"/api/v1/coupons/{vip}/send", new { customerId = customerB })).StatusCode);

        // إيقاف الكوبون: ما عاد يبين ولا ينقبل
        await OkJsonAsync(await admin.PostAsJsonAsync($"/api/v1/coupons/{vip}/active", new { isActive = false }), "إيقاف");
        Assert.Equal(HttpStatusCode.BadRequest, (await PlaceOrderAsync(anonymous, PhoneA, productId, unitId, 1m, "VIP-1")).StatusCode);
        var afterStop = await OkJsonAsync(await anonymous.GetAsync($"/api/v1/customers/{customerA}/coupons"), "بعد الإيقاف");
        Assert.DoesNotContain(afterStop.EnumerateArray(), c => c.GetProperty("code").GetString() == "VIP-1");
    }
}
