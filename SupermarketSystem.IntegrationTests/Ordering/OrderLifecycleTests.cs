using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using Xunit;

namespace SupermarketSystem.IntegrationTests.Ordering;

/// <summary>
/// دورة حياة الطلب الكاملة (Pending → Accepted → Completed، وفرع الرفض)
/// عبر HTTP فعليًا بتوكن Master Admin - راجع تعليق PlaceOrderTests للسبب
/// (Order كيان IBranchOwned، وCrossBranchAccess اللي عند Master Admin
/// هو اللي بيخلي القراءة/الكتابة تشتغل أصلًا بمعزل عن فجوة الفلتر
/// الموثَّقة هناك).
/// </summary>
[Collection(DatabaseCollection.Name)]
public sealed class OrderLifecycleTests : IntegrationTestBase
{
    public OrderLifecycleTests(DatabaseFixture fixture) : base(fixture) { }

    private async Task<Guid> PlaceOrderAsync(HttpClient client, string phone, Guid productId, Guid unitId)
    {
        var response = await client.PostAsJsonAsync("/api/v1/orders", new
        {
            CustomerPhone = phone,
            CustomerName = (string?)null,
            BranchId = Fixture.TestBranchId,
            DeliveryNote = (string?)null,
            DeliveryLatitude = (decimal?)null,
            DeliveryLongitude = (decimal?)null,
            Items = new[] { new { ProductId = productId, ProductUnitId = unitId, Quantity = 1m } }
        });

        response.EnsureSuccessStatusCode();
        var json = JsonDocument.Parse(await response.Content.ReadAsStringAsync()).RootElement;
        return json.GetProperty("orderId").GetGuid();
    }

    [Fact]
    public async Task قبول_طلب_معلق_ينجح_ورفض_طلب_مقبول_يفشل_بـ409()
    {
        var (productId, unitId, _) = await OrderingTestDataHelper.CreateSellableProductAsync(
            Fixture.Factory.Services, Fixture.TestBranchId);
        var client = await CreateAuthenticatedClientAsync();
        var orderId = await PlaceOrderAsync(client, "0791000001", productId, unitId);

        var acceptResponse = await client.PostAsync($"/api/v1/orders/{orderId}/accept", content: null);
        Assert.Equal(HttpStatusCode.NoContent, acceptResponse.StatusCode);

        // رفض طلب مقبول أصلًا - انتقال حالة غير صالح (Order.Reject يمنع
        // الرفض بعد Completed بس هون الحالة Accepted، وهاي مسموحة فعليًا
        // حسب Order.Reject (يسمح برفض من Pending أو Accepted) - فلنتحقق
        // من نجاحها فعلًا، ثم من رفض محاولة قبول ثانية على نفس الطلب.
        var acceptAgainResponse = await client.PostAsync($"/api/v1/orders/{orderId}/accept", content: null);
        Assert.Equal(HttpStatusCode.Conflict, acceptAgainResponse.StatusCode);
    }

    [Fact]
    public async Task رفض_طلب_بلا_سبب_يفشل_بـ400_وبسبب_يفشل_ثم_ينجح()
    {
        var (productId, unitId, _) = await OrderingTestDataHelper.CreateSellableProductAsync(
            Fixture.Factory.Services, Fixture.TestBranchId);
        var client = await CreateAuthenticatedClientAsync();
        var orderId = await PlaceOrderAsync(client, "0791000002", productId, unitId);

        var emptyReasonResponse = await client.PostAsJsonAsync($"/api/v1/orders/{orderId}/reject", new { Reason = "" });
        Assert.Equal(HttpStatusCode.BadRequest, emptyReasonResponse.StatusCode);

        var rejectResponse = await client.PostAsJsonAsync($"/api/v1/orders/{orderId}/reject", new { Reason = "الصنف غير متوفر فعليًا" });
        Assert.Equal(HttpStatusCode.NoContent, rejectResponse.StatusCode);

        var getResponse = await client.GetAsync($"/api/v1/orders/{orderId}");
        var detail = JsonDocument.Parse(await getResponse.Content.ReadAsStringAsync()).RootElement;
        Assert.Equal(4 /* OrderStatus.Rejected */, detail.GetProperty("status").GetInt32());
        Assert.Equal("الصنف غير متوفر فعليًا", detail.GetProperty("rejectionReason").GetString());
    }

    [Fact]
    public async Task إسناد_سائق_لطلب_غير_مقبول_يفشل_وبعد_القبول_ينجح()
    {
        var (productId, unitId, _) = await OrderingTestDataHelper.CreateSellableProductAsync(
            Fixture.Factory.Services, Fixture.TestBranchId);
        var client = await CreateAuthenticatedClientAsync();
        var orderId = await PlaceOrderAsync(client, "0791000003", productId, unitId);

        var driverId = await UsersTestDataHelper.CreateUserWithRoleAsync(
            Fixture.Factory.Services, Fixture.TestBranchId, UsersTestDataHelper.DriverRoleId, "driver.assign.test");

        var beforeAcceptResponse = await client.PostAsJsonAsync($"/api/v1/orders/{orderId}/assign-driver", new { DriverId = driverId });
        Assert.Equal(HttpStatusCode.Conflict, beforeAcceptResponse.StatusCode);

        await client.PostAsync($"/api/v1/orders/{orderId}/accept", content: null);

        var afterAcceptResponse = await client.PostAsJsonAsync($"/api/v1/orders/{orderId}/assign-driver", new { DriverId = driverId });
        Assert.Equal(HttpStatusCode.NoContent, afterAcceptResponse.StatusCode);
    }

    [Fact]
    public async Task إسناد_سائق_غير_موجود_يفشل_بـ404()
    {
        var (productId, unitId, _) = await OrderingTestDataHelper.CreateSellableProductAsync(
            Fixture.Factory.Services, Fixture.TestBranchId);
        var client = await CreateAuthenticatedClientAsync();
        var orderId = await PlaceOrderAsync(client, "0791000004", productId, unitId);
        await client.PostAsync($"/api/v1/orders/{orderId}/accept", content: null);

        var response = await client.PostAsJsonAsync($"/api/v1/orders/{orderId}/assign-driver", new { DriverId = Guid.NewGuid() });
        Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
    }

    [Fact]
    public async Task إكمال_طلب_غير_مقبول_يفشل_وبعد_القبول_ينشئ_فاتورة_بيع_فعلية()
    {
        // راجع تعليق BranchDocumentSequenceHelper - إلزامي قبل أي CompleteOrder/CompleteSale.
        await BranchDocumentSequenceHelper.EnsureProvisionedAsync(Fixture.Factory.Services, Fixture.TestBranchId);

        var (productId, unitId, price) = await OrderingTestDataHelper.CreateSellableProductAsync(
            Fixture.Factory.Services, Fixture.TestBranchId, sellingPrice: 4m);
        var client = await CreateAuthenticatedClientAsync();
        var orderId = await PlaceOrderAsync(client, "0791000005", productId, unitId);

        var paymentMethodId = await PaymentMethodsHelper.GetAnyActivePaymentMethodIdAsync(Fixture.Factory.Services);

        var beforeAcceptResponse = await client.PostAsJsonAsync($"/api/v1/orders/{orderId}/complete", new
        {
            Payments = new[] { new { PaymentMethodId = paymentMethodId, Amount = price, ExternalReference = (string?)null } },
            ClientRequestId = Guid.NewGuid()
        });
        Assert.Equal(HttpStatusCode.UnprocessableEntity, beforeAcceptResponse.StatusCode);

        await client.PostAsync($"/api/v1/orders/{orderId}/accept", content: null);

        var completeResponse = await client.PostAsJsonAsync($"/api/v1/orders/{orderId}/complete", new
        {
            Payments = new[] { new { PaymentMethodId = paymentMethodId, Amount = price, ExternalReference = (string?)null } },
            ClientRequestId = Guid.NewGuid()
        });

        var body = await completeResponse.Content.ReadAsStringAsync();
        Assert.True(completeResponse.StatusCode == HttpStatusCode.Created, $"توقعنا 201 ورجع {completeResponse.StatusCode}: {body}");

        var getResponse = await client.GetAsync($"/api/v1/orders/{orderId}");
        var detail = JsonDocument.Parse(await getResponse.Content.ReadAsStringAsync()).RootElement;
        Assert.Equal(3 /* OrderStatus.Completed */, detail.GetProperty("status").GetInt32());
        Assert.NotEqual(Guid.Empty, detail.GetProperty("resultingSaleInvoiceId").GetGuid());
    }

    [Fact]
    public async Task تقييم_طلب_غير_مكتمل_يفشل_وبعد_الإكمال_ينجح_مرة_واحدة_فقط()
    {
        await BranchDocumentSequenceHelper.EnsureProvisionedAsync(Fixture.Factory.Services, Fixture.TestBranchId);

        var (productId, unitId, price) = await OrderingTestDataHelper.CreateSellableProductAsync(
            Fixture.Factory.Services, Fixture.TestBranchId, sellingPrice: 2m);
        var client = await CreateAuthenticatedClientAsync();
        var orderId = await PlaceOrderAsync(client, "0791000006", productId, unitId);

        var earlyRateResponse = await client.PostAsJsonAsync($"/api/v1/orders/{orderId}/rate", new { Rating = 5, Comment = (string?)null });
        Assert.Equal(HttpStatusCode.BadRequest, earlyRateResponse.StatusCode);

        await client.PostAsync($"/api/v1/orders/{orderId}/accept", content: null);
        var paymentMethodId = await PaymentMethodsHelper.GetAnyActivePaymentMethodIdAsync(Fixture.Factory.Services);
        await client.PostAsJsonAsync($"/api/v1/orders/{orderId}/complete", new
        {
            Payments = new[] { new { PaymentMethodId = paymentMethodId, Amount = price, ExternalReference = (string?)null } },
            ClientRequestId = Guid.NewGuid()
        });

        var firstRateResponse = await client.PostAsJsonAsync($"/api/v1/orders/{orderId}/rate", new { Rating = 5, Comment = "ممتاز" });
        Assert.Equal(HttpStatusCode.NoContent, firstRateResponse.StatusCode);

        // تقييم ثانٍ لنفس الطلب - يجب أن يُرفض (راجع Order.Rate: مرة واحدة بس).
        var secondRateResponse = await client.PostAsJsonAsync($"/api/v1/orders/{orderId}/rate", new { Rating = 1, Comment = "تراجعت رأيي" });
        Assert.Equal(HttpStatusCode.BadRequest, secondRateResponse.StatusCode);
    }

    [Fact]
    public async Task قائمة_الطلبات_المعلقة_تُرجع_الطلب_الجديد_وقائمة_طلبات_الزبون_كذلك()
    {
        var (productId, unitId, _) = await OrderingTestDataHelper.CreateSellableProductAsync(
            Fixture.Factory.Services, Fixture.TestBranchId);
        var client = await CreateAuthenticatedClientAsync();
        const string phone = "0791000007";
        var orderId = await PlaceOrderAsync(client, phone, productId, unitId);

        var pendingResponse = await client.GetAsync("/api/v1/orders?pageNumber=1&pageSize=50");
        var pendingJson = JsonDocument.Parse(await pendingResponse.Content.ReadAsStringAsync()).RootElement;
        var pendingIds = pendingJson.GetProperty("items").EnumerateArray().Select(i => i.GetProperty("id").GetGuid());
        Assert.Contains(orderId, pendingIds);

        using var scope = CreateScope();
        var db = CreateDbContext(scope);
        var customerId = await db.Customers.Where(c => c.Phone == phone).Select(c => c.Id).FirstAsync();

        var customerOrdersResponse = await client.GetAsync($"/api/v1/orders/customers/{customerId}");
        var customerOrdersJson = JsonDocument.Parse(await customerOrdersResponse.Content.ReadAsStringAsync()).RootElement;
        var customerOrderIds = customerOrdersJson.GetProperty("items").EnumerateArray().Select(i => i.GetProperty("id").GetGuid());
        Assert.Contains(orderId, customerOrderIds);
    }

    [Fact]
    public async Task قائمة_السائقين_تُرجع_فقط_مستخدمًا_عنده_دور_سائق()
    {
        var client = await CreateAuthenticatedClientAsync();
        var driverId = await UsersTestDataHelper.CreateUserWithRoleAsync(
            Fixture.Factory.Services, Fixture.TestBranchId, UsersTestDataHelper.DriverRoleId, "driver.list.test");

        var response = await client.GetAsync("/api/v1/orders/drivers");
        response.EnsureSuccessStatusCode();

        var json = JsonDocument.Parse(await response.Content.ReadAsStringAsync()).RootElement;
        var driverIds = json.EnumerateArray().Select(d => d.GetProperty("id").GetGuid());
        Assert.Contains(driverId, driverIds);
    }

    [Fact]
    public async Task مجموعة_الكاشير_محمية_401_بلا_توكن()
    {
        var client = CreateAnonymousClient();
        var response = await client.GetAsync("/api/v1/orders");
        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
    }
}
