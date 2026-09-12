using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using Xunit;

namespace SupermarketSystem.IntegrationTests.Driver;

/// <summary>
/// صفحة السائق - محمية بصلاحية Orders.Deliver وحدها (راجع تعليق
/// DriverEndpoints.cs). هويّة السائق تُحسم من التوكن (ICurrentUserContext)
/// لا من أي معامل بالطلب، فكل الاختبارات هون بتسجّل دخول فعليًا كمستخدم
/// بدور "سائق" حقيقي (لا Master Admin) للتحقق من هذا الحصر بدقة.
/// </summary>
[Collection(DatabaseCollection.Name)]
public sealed class DriverEndpointTests : IntegrationTestBase
{
    public DriverEndpointTests(DatabaseFixture fixture) : base(fixture) { }

    [Fact]
    public async Task مجموعة_السائق_محمية_401_بلا_توكن()
    {
        var client = CreateAnonymousClient();
        var response = await client.GetAsync("/api/v1/driver/my-deliveries");
        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
    }

    [Fact]
    public async Task كاشير_بلا_صلاحية_Orders_Deliver_يُرفض_بـ403()
    {
        var (_, username) = await UsersTestDataHelper.CreateUserWithRoleReturningUsernameAsync(
            Fixture.Factory.Services, Fixture.TestBranchId, UsersTestDataHelper.CashierRoleId, "cashier.driverpage.test");

        var cashierClient = await LoginHelper.LoginAsAsync(Fixture, username, UsersTestDataHelper.DefaultPassword, "Cashier");

        var response = await cashierClient.GetAsync("/api/v1/driver/my-deliveries");
        Assert.Equal(HttpStatusCode.Forbidden, response.StatusCode);
    }

    [Fact]
    public async Task سائق_يشوف_طلبه_المسند_فقط_ويكمل_تسليمه_وتُنشأ_فاتورة_فعلية()
    {
        await BranchDocumentSequenceHelper.EnsureProvisionedAsync(Fixture.Factory.Services, Fixture.TestBranchId);

        var (productId, unitId, price) = await Ordering.OrderingTestDataHelper.CreateSellableProductAsync(
            Fixture.Factory.Services, Fixture.TestBranchId, sellingPrice: 5m);

        var adminClient = await CreateAuthenticatedClientAsync();

        var placeResponse = await adminClient.PostAsJsonAsync("/api/v1/orders", new
        {
            CustomerPhone = "0792000001",
            CustomerName = (string?)null,
            BranchId = Fixture.TestBranchId,
            DeliveryNote = (string?)null,
            DeliveryLatitude = (decimal?)null,
            DeliveryLongitude = (decimal?)null,
            Items = new[] { new { ProductId = productId, ProductUnitId = unitId, Quantity = 1m } }
        });
        var orderId = JsonDocument.Parse(await placeResponse.Content.ReadAsStringAsync()).RootElement.GetProperty("orderId").GetGuid();

        await adminClient.PostAsync($"/api/v1/orders/{orderId}/accept", content: null);

        var (driverUserId, driverUsername) = await UsersTestDataHelper.CreateUserWithRoleReturningUsernameAsync(
            Fixture.Factory.Services, Fixture.TestBranchId, UsersTestDataHelper.DriverRoleId, "driver.deliver.test");

        var assignResponse = await adminClient.PostAsJsonAsync($"/api/v1/orders/{orderId}/assign-driver", new { DriverId = driverUserId });
        Assert.Equal(HttpStatusCode.NoContent, assignResponse.StatusCode);

        // سائق ثانٍ (بلا إسناد) - يجب ألا يشوف هذا الطلب إطلاقًا (فحص حصر الملكية).
        var (_, otherDriverUsername) = await UsersTestDataHelper.CreateUserWithRoleReturningUsernameAsync(
            Fixture.Factory.Services, Fixture.TestBranchId, UsersTestDataHelper.DriverRoleId, "driver.other.test");
        var otherDriverClient = await LoginHelper.LoginAsAsync(Fixture, otherDriverUsername, UsersTestDataHelper.DefaultPassword, "Cashier");
        var otherDeliveriesResponse = await otherDriverClient.GetAsync("/api/v1/driver/my-deliveries");
        var otherDeliveries = JsonDocument.Parse(await otherDeliveriesResponse.Content.ReadAsStringAsync()).RootElement;
        Assert.Equal(0, otherDeliveries.GetArrayLength());

        var driverClient = await LoginHelper.LoginAsAsync(Fixture, driverUsername, UsersTestDataHelper.DefaultPassword, "Cashier");

        var myDeliveriesResponse = await driverClient.GetAsync("/api/v1/driver/my-deliveries");
        myDeliveriesResponse.EnsureSuccessStatusCode();
        var myDeliveries = JsonDocument.Parse(await myDeliveriesResponse.Content.ReadAsStringAsync()).RootElement;
        Assert.Equal(1, myDeliveries.GetArrayLength());
        Assert.Equal(orderId, myDeliveries[0].GetProperty("orderId").GetGuid());

        var paymentMethodId = await PaymentMethodsHelper.GetAnyActivePaymentMethodIdAsync(Fixture.Factory.Services);

        // السائق الآخر ما يقدر يكمّل توصيل طلب مو مسند له (403).
        var otherCompleteResponse = await otherDriverClient.PostAsJsonAsync($"/api/v1/driver/deliveries/{orderId}/complete", new
        {
            PaymentMethodId = paymentMethodId,
            AmountCollected = price,
            ClientRequestId = Guid.NewGuid()
        });
        Assert.Equal(HttpStatusCode.Forbidden, otherCompleteResponse.StatusCode);

        var completeResponse = await driverClient.PostAsJsonAsync($"/api/v1/driver/deliveries/{orderId}/complete", new
        {
            PaymentMethodId = paymentMethodId,
            AmountCollected = price,
            ClientRequestId = Guid.NewGuid()
        });

        var body = await completeResponse.Content.ReadAsStringAsync();
        Assert.True(completeResponse.StatusCode == HttpStatusCode.OK, $"توقعنا 200 ورجع {completeResponse.StatusCode}: {body}");

        var afterResponse = await driverClient.GetAsync("/api/v1/driver/my-deliveries");
        var afterDeliveries = JsonDocument.Parse(await afterResponse.Content.ReadAsStringAsync()).RootElement;
        // بعد الإكمال الطلب صار Completed - ما عاد يظهر بقائمة "بانتظار التوصيل".
        Assert.Equal(0, afterDeliveries.GetArrayLength());
    }
}
