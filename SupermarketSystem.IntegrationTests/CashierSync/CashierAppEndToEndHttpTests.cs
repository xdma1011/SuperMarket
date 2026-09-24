using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;
using Microsoft.EntityFrameworkCore;
using SupermarketSystem.IntegrationTests.Common;
using Xunit;

namespace SupermarketSystem.IntegrationTests.CashierSync;

/// <summary>
/// دورة تطبيق الكاشير (WPF) كاملة عبر HTTP حقيقي، بمستخدم دوره "كاشير" فعلي
/// (لا Master Admin)، وبنفس شكل الطلبات اللي التطبيق بيبعتها حرفيًا - راجع
/// SupermarketSystem.CashierApp/Services/ApiClient.cs وSaleWindow.CompleteSaleButton_Click:
/// - الدخول: AppType رقم (1)، BranchId=null (الفرع من الفرع الافتراضي للمستخدم).
/// - البيع: JSON camelCase خام (نفس PendingSale.RequestPayloadJson)، الدفعة = المجموع المحلي.
/// - الإرجاع/الإلغاء: enums كنصوص (RequestJsonOptions بالتطبيق).
///
/// فرع مخصص لهالملف (CSH-E2E) عشان حساب الكاش المتوقع بالتقفيل ما يتأثر ببيعات
/// اختبارات تانية على فرع الاختبار العام.
/// </summary>
[Collection(DatabaseCollection.Name)]
public sealed class CashierAppEndToEndHttpTests : IntegrationTestBase
{
    public CashierAppEndToEndHttpTests(DatabaseFixture fixture) : base(fixture) { }

    // نفس SaleWindow.JsonOptions بالضبط.
    private static readonly JsonSerializerOptions SalePayloadOptions = new() { PropertyNamingPolicy = JsonNamingPolicy.CamelCase };

    // نفس ApiClient.RequestJsonOptions بالضبط (للإرجاع والإلغاء).
    private static readonly JsonSerializerOptions ReturnVoidOptions = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        Converters = { new JsonStringEnumConverter() }
    };

    private sealed record CashierContext(HttpClient Client, Guid BranchId, string RefreshToken);

    private async Task<(Guid BranchId, Guid ProductId, Guid UnitId, Guid ProductBranchId)> SeedBranchWithProductAsync(
        string branchCode, decimal price, decimal stock)
    {
        using var scope = CreateScope();
        var db = CreateDbContext(scope);

        var branch = await TestDataBuilder.CreateBranchAsync(db, $"فرع {branchCode}", branchCode);
        await TestDataBuilder.EnsureDocumentSequencesProvisionedAsync(db, branch.Id);

        var (product, unit) = await TestDataBuilder.CreateActiveProductAsync(db, $"منتج كاشير {branchCode} {Guid.NewGuid():N}");
        var productBranch = await TestDataBuilder.CreateProductBranchAsync(db, product.Id, branch.Id, price);
        await TestDataBuilder.SetStockAsync(db, product.Id, branch.Id, stock);

        return (branch.Id, product.Id, unit.Id, productBranch.Id);
    }

    private async Task<CashierContext> LoginAsCashierLikeWpfAsync(Guid branchId)
    {
        var (_, username) = await UsersTestDataHelper.CreateUserWithRoleReturningUsernameAsync(
            Fixture.Factory.Services, branchId, UsersTestDataHelper.CashierRoleId, "cashier.e2e");

        var client = Fixture.Factory.CreateClient();
        var login = await client.PostAsJsonAsync("/api/v1/auth/login", new
        {
            username,
            password = UsersTestDataHelper.DefaultPassword,
            appType = 1,
            branchId = (Guid?)null,
            ipAddress = (string?)null,
            deviceInfo = "CASHIER-PC-TEST"
        });
        Assert.Equal(HttpStatusCode.OK, login.StatusCode);

        var body = JsonDocument.Parse(await login.Content.ReadAsStringAsync()).RootElement;
        Assert.Equal(branchId, body.GetProperty("branchId").GetGuid());

        client.DefaultRequestHeaders.Authorization =
            new AuthenticationHeaderValue("Bearer", body.GetProperty("accessToken").GetString());
        return new CashierContext(client, branchId, body.GetProperty("refreshToken").GetString()!);
    }

    private static string BuildSalePayload(
        Guid branchId, Guid clientRequestId, Guid productId, Guid unitId, decimal quantity, decimal localTotal, long? catalogVersion = null)
    {
        var payload = new
        {
            branchId,
            clientRequestId,
            customerId = (Guid?)null,
            invoiceLevelDiscountAmount = 0m,
            items = new[]
            {
                new { productId, productUnitId = unitId, quantity, manualDiscountAmount = 0m, productBatchId = (Guid?)null, catalogVersion }
            },
            payments = new[]
            {
                new
                {
                    paymentMethodId = TestDataBuilder.CashPaymentMethodId,
                    amount = localTotal,
                    externalReference = (string?)null,
                    clientRequestId = Guid.NewGuid()
                }
            }
        };
        return JsonSerializer.Serialize(payload, SalePayloadOptions);
    }

    private static Task<HttpResponseMessage> SendPendingSaleAsync(HttpClient client, string payloadJson) =>
        client.PostAsync("/api/v1/sales", new StringContent(payloadJson, Encoding.UTF8, "application/json"));

    private async Task<decimal> GetStockAsync(Guid productId, Guid branchId)
    {
        using var scope = CreateScope();
        var db = CreateDbContext(scope);
        return await db.Stocks.IgnoreQueryFilters().AsNoTracking()
            .Where(s => s.ProductId == productId && s.BranchId == branchId)
            .SumAsync(s => s.QuantityOnHand);
    }

    [Fact]
    public async Task دورة_كاشير_كاملة_مزامنة_بيع_تكرار_بحث_إرجاع_إلغاء_تقفيل_خروج()
    {
        var (branchId, productId, unitId, _) = await SeedBranchWithProductAsync("CSH-E2E", price: 1.250m, stock: 50m);
        var cashier = await LoginAsCashierLikeWpfAsync(branchId);
        var client = cashier.Client;

        // --- المزامنة: نفس الطلبات اللي BackgroundSyncService/CatalogSyncService بيبعتها ---
        var version = await client.GetFromJsonAsync<JsonElement>("/api/v1/cashier-sync/catalog-version");
        // بذر مباشر بقاعدة البيانات هون (مش عبر endpoints الإدارة) فالرقم ممكن يكون 0 - المهم الشكل.
        Assert.True(version.GetProperty("version").GetInt64() >= 0);

        var page = await client.GetFromJsonAsync<JsonElement>(
            $"/api/v1/cashier-sync/catalog-page?branchId={branchId}&pageNumber=1&pageSize=200");
        var synced = page.GetProperty("items").EnumerateArray().Single(i => i.GetProperty("productId").GetGuid() == productId);
        Assert.Equal(1.250m, synced.GetProperty("sellingPrice").GetDecimal());
        Assert.True(synced.GetProperty("isAvailableForSale").GetBoolean());

        var paymentMethods = await client.GetFromJsonAsync<JsonElement>("/api/v1/payment-methods");
        Assert.Contains(paymentMethods.EnumerateArray(), m => m.GetProperty("id").GetGuid() == TestDataBuilder.CashPaymentMethodId);

        Assert.Equal(HttpStatusCode.OK, (await client.GetAsync("/api/v1/cashier-sync/store-branding")).StatusCode);
        Assert.Equal(HttpStatusCode.OK, (await client.GetAsync("/api/v1/cashier-sync/payment-settings")).StatusCode);

        // --- البيع: خانات الفلس الثلاث تنحفظ بدقة (2 × 1.250 = 2.500) ---
        var saleRequestId = Guid.NewGuid();
        var salePayload = BuildSalePayload(branchId, saleRequestId, productId, unitId, 2m, localTotal: 2.500m);
        var saleResponse = await SendPendingSaleAsync(client, salePayload);
        Assert.Equal(HttpStatusCode.Created, saleResponse.StatusCode);
        var sale = JsonDocument.Parse(await saleResponse.Content.ReadAsStringAsync()).RootElement;
        Assert.Equal(2.500m, sale.GetProperty("totalAmount").GetDecimal());
        Assert.False(sale.GetProperty("wasReplay").GetBoolean());
        var saleId = sale.GetProperty("saleInvoiceId").GetGuid();
        var invoiceNumber = sale.GetProperty("invoiceNumber").GetString()!;
        Assert.Equal(48m, await GetStockAsync(productId, branchId));

        // --- إعادة إرسال نفس البيع المعلَّق (الرد ضاع) - لا بيع مكرر، لا خصم مخزون تاني ---
        var replayResponse = await SendPendingSaleAsync(client, salePayload);
        Assert.Equal(HttpStatusCode.OK, replayResponse.StatusCode);
        var replay = JsonDocument.Parse(await replayResponse.Content.ReadAsStringAsync()).RootElement;
        Assert.True(replay.GetProperty("wasReplay").GetBoolean());
        Assert.Equal(saleId, replay.GetProperty("saleInvoiceId").GetGuid());
        Assert.Equal(48m, await GetStockAsync(productId, branchId));

        // --- البحث عن الفاتورة وتفاصيلها (شاشة الإرجاع) ---
        var search = await client.GetFromJsonAsync<JsonElement>(
            $"/api/v1/sales?pageNumber=1&pageSize=20&search={Uri.EscapeDataString(invoiceNumber)}&branchId={branchId}");
        Assert.Contains(search.GetProperty("items").EnumerateArray(), i => i.GetProperty("id").GetGuid() == saleId);

        var detail = await client.GetFromJsonAsync<JsonElement>($"/api/v1/sales/{saleId}");
        var saleItemId = detail.GetProperty("items")[0].GetProperty("saleInvoiceItemId").GetGuid();

        // --- إرجاع حبة وحدة، enums كنصوص زي التطبيق ---
        var returnResponse = await client.PostAsJsonAsync("/api/v1/returns", new
        {
            originalSaleInvoiceId = saleId,
            clientRequestId = Guid.NewGuid(),
            reason = "Defective",
            notes = "مكسور",
            items = new[] { new { saleInvoiceItemId = saleItemId, quantity = 1m } },
            refunds = new[]
            {
                new { paymentMethodId = TestDataBuilder.CashPaymentMethodId, amount = 1.250m, externalReference = (string?)null, clientRequestId = Guid.NewGuid() }
            }
        }, ReturnVoidOptions);
        Assert.True(returnResponse.IsSuccessStatusCode, await returnResponse.Content.ReadAsStringAsync());
        var returned = JsonDocument.Parse(await returnResponse.Content.ReadAsStringAsync()).RootElement;
        Assert.Equal(1.250m, returned.GetProperty("totalRefundedAmount").GetDecimal());
        Assert.Equal(49m, await GetStockAsync(productId, branchId));

        // --- بيع تاني ثم إلغاؤه ---
        var secondSale = await SendPendingSaleAsync(client, BuildSalePayload(branchId, Guid.NewGuid(), productId, unitId, 4m, localTotal: 5.000m));
        Assert.Equal(HttpStatusCode.Created, secondSale.StatusCode);
        var secondSaleId = JsonDocument.Parse(await secondSale.Content.ReadAsStringAsync()).RootElement.GetProperty("saleInvoiceId").GetGuid();
        Assert.Equal(45m, await GetStockAsync(productId, branchId));

        var voidResponse = await client.PostAsJsonAsync($"/api/v1/sales/{secondSaleId}/void",
            new { reason = "CashierError", notes = "غلط بالكمية" }, ReturnVoidOptions);
        Assert.True(voidResponse.IsSuccessStatusCode, await voidResponse.Content.ReadAsStringAsync());
        var voided = JsonDocument.Parse(await voidResponse.Content.ReadAsStringAsync()).RootElement;
        Assert.Equal(5.000m, voided.GetProperty("cashReturnedToDrawer").GetDecimal());
        Assert.Equal(49m, await GetStockAsync(productId, branchId));

        // --- التقفيل: الكاش المتوقع = 2.500 بيع - 1.250 استرجاع + 5.000 بيع - 5.000 إلغاء = 1.250 ---
        var closingRequest = new
        {
            branchId,
            businessDate = DateOnly.FromDateTime(DateTime.UtcNow),
            countedCash = 1.200m,
            countedDetails = Array.Empty<object>()
        };

        // الكاشير نفسه بيقفّل (CashClosing.Manage صارت من CashierDefaults - قرار صاحب المشروع
        // 24/9/2026، كانت 403 لأن زر التقفيل بالتطبيق بيبعت الطلب بتوكن الكاشير).
        var cashierClosing = await client.PostAsJsonAsync("/api/v1/cash-closings", closingRequest);
        Assert.Equal(HttpStatusCode.Created, cashierClosing.StatusCode);
        var closing = JsonDocument.Parse(await cashierClosing.Content.ReadAsStringAsync()).RootElement;
        Assert.Equal(1.250m, closing.GetProperty("expectedCash").GetDecimal());
        Assert.Equal(-0.050m, closing.GetProperty("variance").GetDecimal());

        // --- التقارير بتشوف الإرجاع والإلغاء. الـenums بتطلع كنصوص (JsonStringEnumConverter
        // عام بـProgram.cs) - الفرونت إند بيترجمها بخرائط بالاسم (report-configs.ts). ---
        var adminClient = await CreateAuthenticatedClientAsync();
        var recentReturns = await adminClient.GetFromJsonAsync<JsonElement>($"/api/v1/reports/returns/recent?branchId={branchId}");
        Assert.Equal("Defective", recentReturns.GetProperty("items")[0].GetProperty("reason").GetString());
        var voidedSales = await adminClient.GetFromJsonAsync<JsonElement>($"/api/v1/reports/sales/voided?branchId={branchId}");
        Assert.Equal("CashierError", voidedSales.GetProperty("items")[0].GetProperty("voidReason").GetString());

        // --- الخروج: التوكن الحالي بيوقف فورًا ---
        var logout = await Fixture.Factory.CreateClient().PostAsJsonAsync("/api/v1/auth/logout", new { refreshToken = cashier.RefreshToken });
        Assert.True(logout.IsSuccessStatusCode);
        Assert.Equal(HttpStatusCode.Unauthorized, (await client.GetAsync("/api/v1/cashier-sync/catalog-version")).StatusCode);
    }

    [Fact]
    public async Task الكاشير_ما_بيوصل_لشاشات_الإدارة()
    {
        var (branchId, _, _, _) = await SeedBranchWithProductAsync("CSH-PERM", price: 1m, stock: 1m);
        var client = (await LoginAsCashierLikeWpfAsync(branchId)).Client;

        Assert.Equal(HttpStatusCode.Forbidden, (await client.GetAsync("/api/v1/finance/capital-transactions")).StatusCode);
        Assert.Equal(HttpStatusCode.Forbidden, (await client.GetAsync($"/api/v1/reports/sales/summary?branchId={branchId}")).StatusCode);
        Assert.Equal(HttpStatusCode.Forbidden, (await client.GetAsync("/api/v1/products")).StatusCode);
        Assert.Equal(HttpStatusCode.Forbidden, (await client.GetAsync("/api/v1/users")).StatusCode);
    }

    [Fact]
    public async Task الكاشير_ما_بيقدر_يبيع_بفرع_غير_فرعه()
    {
        var (ownBranchId, _, _, _) = await SeedBranchWithProductAsync("CSH-OWN", price: 1m, stock: 5m);
        var (otherBranchId, productId, unitId, _) = await SeedBranchWithProductAsync("CSH-OTHER", price: 1m, stock: 5m);
        var client = (await LoginAsCashierLikeWpfAsync(ownBranchId)).Client;

        var response = await SendPendingSaleAsync(client, BuildSalePayload(otherBranchId, Guid.NewGuid(), productId, unitId, 1m, localTotal: 1m));

        Assert.False(response.IsSuccessStatusCode);
        Assert.Equal(5m, await GetStockAsync(productId, otherBranchId));
    }

    [Fact]
    public async Task بيع_بسعر_غلط_من_العميل_ما_بيغيّر_السعر_والفاتورة_بتنرفض()
    {
        // السعر من السيرفر دايمًا (§3.6) - عميل بيبعت دفعة أقل من السعر الحقيقي بينرفض، ما بينباع أرخص.
        var (branchId, productId, unitId, _) = await SeedBranchWithProductAsync("CSH-PRICE", price: 2m, stock: 5m);
        var client = (await LoginAsCashierLikeWpfAsync(branchId)).Client;

        var response = await SendPendingSaleAsync(client, BuildSalePayload(branchId, Guid.NewGuid(), productId, unitId, 1m, localTotal: 0.500m));

        Assert.Equal(HttpStatusCode.UnprocessableEntity, response.StatusCode);
        Assert.Contains("Sale.PaymentsDoNotSettleTotal", await response.Content.ReadAsStringAsync());
        Assert.Equal(5m, await GetStockAsync(productId, branchId));
    }

    private async Task<long> GetCatalogVersionAsync(HttpClient client) =>
        (await client.GetFromJsonAsync<JsonElement>("/api/v1/cashier-sync/catalog-version")).GetProperty("version").GetInt64();

    private async Task ChangePriceAsAdminAsync(Guid productId, Guid productBranchId, decimal newPrice)
    {
        var adminClient = await CreateAuthenticatedClientAsync();
        var response = await adminClient.PostAsJsonAsync(
            $"/api/v1/products/{productId}/branches/{productBranchId}/price-change-requests", new { requestedPrice = newPrice });
        Assert.True(response.IsSuccessStatusCode, await response.Content.ReadAsStringAsync());
    }

    [Fact]
    public async Task بيع_أوفلاين_بسعر_النسخة_المحلية_بعد_رفع_السعر_بالسيرفر()
    {
        // قرار صاحب المشروع: البيع الأوفلاين بينحسب بسعر النسخة المحلية اللي انباع منها،
        // والسعر الجديد للبيعات الجاية بعد المزامنة - غير هيك الكاشير بيطلع عنده عجز وهمي.
        var (branchId, productId, unitId, productBranchId) = await SeedBranchWithProductAsync("CSH-OFFL", price: 1.000m, stock: 10m);
        var client = (await LoginAsCashierLikeWpfAsync(branchId)).Client;
        var localVersion = await GetCatalogVersionAsync(client);

        // النت انقطع، الكاشير باع حبتين بالسعر المحلي (1.000) وانحفظ البيع بالطابور...
        var queuedPayload = BuildSalePayload(branchId, Guid.NewGuid(), productId, unitId, 2m, localTotal: 2.000m, catalogVersion: localVersion);

        // ...وبنفس الوقت الأدمن رفع السعر.
        await ChangePriceAsAdminAsync(productId, productBranchId, 1.250m);
        var newVersion = await GetCatalogVersionAsync(client);
        Assert.True(newVersion > localVersion);

        // رجع النت: الطابور بيبعت البيع المعلَّق - بينقبل بسعر نسخته، ومعلَّم للمراجعة.
        var response = await SendPendingSaleAsync(client, queuedPayload);
        Assert.Equal(HttpStatusCode.Created, response.StatusCode);
        var sale = JsonDocument.Parse(await response.Content.ReadAsStringAsync()).RootElement;
        Assert.Equal(2.000m, sale.GetProperty("totalAmount").GetDecimal());
        Assert.Contains(sale.GetProperty("reviewFlags").EnumerateArray(), f => f.GetString()!.Contains("سعر نسخة الكاشير"));

        // بعد المزامنة: بيع جديد بالنسخة الجديدة بالسعر الجديد، بلا أي علامة مراجعة.
        var afterSync = await SendPendingSaleAsync(client,
            BuildSalePayload(branchId, Guid.NewGuid(), productId, unitId, 2m, localTotal: 2.500m, catalogVersion: newVersion));
        Assert.Equal(HttpStatusCode.Created, afterSync.StatusCode);
        var afterSyncSale = JsonDocument.Parse(await afterSync.Content.ReadAsStringAsync()).RootElement;
        Assert.Equal(2.500m, afterSyncSale.GetProperty("totalAmount").GetDecimal());
        Assert.Empty(afterSyncSale.GetProperty("reviewFlags").EnumerateArray());

        // الكاشير ما بيختار السعر: نسخة قديمة بسعر جديد (مش سعر نسختها) بتنرفض.
        var mismatched = await SendPendingSaleAsync(client,
            BuildSalePayload(branchId, Guid.NewGuid(), productId, unitId, 1m, localTotal: 1.250m, catalogVersion: localVersion));
        Assert.Equal(HttpStatusCode.UnprocessableEntity, mismatched.StatusCode);
        Assert.Equal(6m, await GetStockAsync(productId, branchId));
    }

    [Fact]
    public async Task بيع_أوفلاين_بعد_تنزيل_السعر_بينحسب_بالسعر_القديم_الأعلى()
    {
        var (branchId, productId, unitId, productBranchId) = await SeedBranchWithProductAsync("CSH-OFFD", price: 2.000m, stock: 10m);
        var client = (await LoginAsCashierLikeWpfAsync(branchId)).Client;
        var localVersion = await GetCatalogVersionAsync(client);

        await ChangePriceAsAdminAsync(productId, productBranchId, 1.500m);

        var response = await SendPendingSaleAsync(client,
            BuildSalePayload(branchId, Guid.NewGuid(), productId, unitId, 1m, localTotal: 2.000m, catalogVersion: localVersion));

        Assert.Equal(HttpStatusCode.Created, response.StatusCode);
        Assert.Equal(2.000m, JsonDocument.Parse(await response.Content.ReadAsStringAsync()).RootElement.GetProperty("totalAmount").GetDecimal());
    }

    [Fact]
    public async Task تغييرين_متتاليين_للسعر_النسخة_الوسطى_بتاخد_سعرها_هي()
    {
        var (branchId, productId, unitId, productBranchId) = await SeedBranchWithProductAsync("CSH-OFF2", price: 1.000m, stock: 10m);
        var client = (await LoginAsCashierLikeWpfAsync(branchId)).Client;

        await ChangePriceAsAdminAsync(productId, productBranchId, 1.250m);
        var middleVersion = await GetCatalogVersionAsync(client);
        await ChangePriceAsAdminAsync(productId, productBranchId, 1.500m);

        var response = await SendPendingSaleAsync(client,
            BuildSalePayload(branchId, Guid.NewGuid(), productId, unitId, 2m, localTotal: 2.500m, catalogVersion: middleVersion));

        Assert.Equal(HttpStatusCode.Created, response.StatusCode);
        Assert.Equal(2.500m, JsonDocument.Parse(await response.Content.ReadAsStringAsync()).RootElement.GetProperty("totalAmount").GetDecimal());
    }

    [Fact]
    public async Task بيع_بلا_رقم_نسخة_بيضل_بالسعر_الحالي_زي_قبل()
    {
        // لوحة الإدارة، الطلبات، وبيعات معلّقة من قبل هالتحديث - كلها بلا catalogVersion.
        var (branchId, productId, unitId, productBranchId) = await SeedBranchWithProductAsync("CSH-NOVR", price: 1.000m, stock: 10m);
        var client = (await LoginAsCashierLikeWpfAsync(branchId)).Client;
        await ChangePriceAsAdminAsync(productId, productBranchId, 1.250m);

        var oldPrice = await SendPendingSaleAsync(client, BuildSalePayload(branchId, Guid.NewGuid(), productId, unitId, 1m, localTotal: 1.000m));
        Assert.Equal(HttpStatusCode.UnprocessableEntity, oldPrice.StatusCode);

        var currentPrice = await SendPendingSaleAsync(client, BuildSalePayload(branchId, Guid.NewGuid(), productId, unitId, 1m, localTotal: 1.250m));
        Assert.Equal(HttpStatusCode.Created, currentPrice.StatusCode);
    }

    [Fact]
    public async Task طلب_سعر_بانتظار_موافقة_بينختم_برقم_النسخة_وقت_الموافقة_مش_وقت_الطلب()
    {
        var (branchId, productId, unitId, productBranchId) = await SeedBranchWithProductAsync("CSH-APPR", price: 1.000m, stock: 10m);
        var client = (await LoginAsCashierLikeWpfAsync(branchId)).Client;

        Guid requestId;
        using (var scope = CreateScope())
        {
            var db = CreateDbContext(scope);
            var pending = new SupermarketSystem.Domain.Catalog.PriceChangeRequest(productBranchId, 1.000m, 1.250m, Fixture.AdminUserId, DateTime.UtcNow);
            db.PriceChangeRequests.Add(pending);
            await db.SaveChangesAsync();
            requestId = pending.Id;
        }

        // الطلب معلَّق - السعر لسه 1.000، فالنسخة الحالية سعرها 1.000.
        var versionBeforeApproval = await GetCatalogVersionAsync(client);

        var adminClient = await CreateAuthenticatedClientAsync();
        var approve = await adminClient.PostAsJsonAsync($"/api/v1/price-change-requests/{requestId}/approve", new { note = (string?)null });
        Assert.True(approve.IsSuccessStatusCode, await approve.Content.ReadAsStringAsync());

        var response = await SendPendingSaleAsync(client,
            BuildSalePayload(branchId, Guid.NewGuid(), productId, unitId, 1m, localTotal: 1.000m, catalogVersion: versionBeforeApproval));
        Assert.Equal(HttpStatusCode.Created, response.StatusCode);

        using (var scope = CreateScope())
        {
            var db = CreateDbContext(scope);
            var approved = await db.PriceChangeRequests.AsNoTracking().FirstAsync(r => r.Id == requestId);
            Assert.True(approved.AppliedAtCatalogVersion > versionBeforeApproval);
        }
    }

    [Fact]
    public async Task قائمة_المنتجات_للأدمن_بلا_فلتر_فرع_ترجع_200()
    {
        var adminClient = await CreateAuthenticatedClientAsync();
        var response = await adminClient.GetAsync("/api/v1/products");
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
    }
}
