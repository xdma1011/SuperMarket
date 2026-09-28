using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text.Json;
using SupermarketSystem.IntegrationTests.Common;
using Xunit;

namespace SupermarketSystem.IntegrationTests.Notifications;

/// <summary>
/// طلب صاحب المشروع (24/9/2026): "اي شيء يكشف السرقة او يحذر من شيء خليها تظهر في التنبيهات".
/// كل إشارة هون بتنعمل عبر HTTP حقيقي، والتحقق من صفحة التنبيهات نفسها (GET /notifications) -
/// العنوان، درجة الخطورة، وإنه النص عربي مفهوم (بلا أسماء enums إنجليزية).
/// </summary>
[Collection(DatabaseCollection.Name)]
public sealed class TheftAlertsTests : IntegrationTestBase
{
    public TheftAlertsTests(DatabaseFixture fixture) : base(fixture) { }

    private sealed record Alert(Guid Id, string Title, string Message, string Severity, string? LinkRoute, string Status);

    private static async Task<List<Alert>> GetAlertsAsync(HttpClient admin, string query = "")
    {
        var json = JsonDocument.Parse(await admin.GetStringAsync($"/api/v1/notifications?pageSize=100{query}")).RootElement;
        return json.GetProperty("items").EnumerateArray()
            .Select(n => new Alert(
                n.GetProperty("id").GetGuid(), n.GetProperty("title").GetString()!, n.GetProperty("message").GetString()!,
                n.GetProperty("severity").GetString()!,
                n.TryGetProperty("linkRoute", out var link) && link.ValueKind == JsonValueKind.String ? link.GetString() : null,
                n.GetProperty("status").GetString()!))
            .ToList();
    }

    private static async Task<JsonElement> OkJsonAsync(HttpResponseMessage response, string what)
    {
        var body = await response.Content.ReadAsStringAsync();
        Assert.True(response.IsSuccessStatusCode, $"{what}: {(int)response.StatusCode} {body}");
        return string.IsNullOrWhiteSpace(body) ? default : JsonDocument.Parse(body).RootElement;
    }

    private async Task<(Guid ProductId, Guid UnitId, Guid ProductBranchId)> SeedProductAsync(string name, decimal price, decimal stock)
    {
        using var scope = CreateScope();
        var db = CreateDbContext(scope);
        await TestDataBuilder.EnsureDocumentSequencesProvisionedAsync(db, Fixture.TestBranchId);
        var (product, unit) = await TestDataBuilder.CreateActiveProductAsync(db, name, isComplimentaryAllowed: true);
        var productBranch = await TestDataBuilder.CreateProductBranchAsync(db, product.Id, Fixture.TestBranchId, price);
        await TestDataBuilder.SetStockAsync(db, product.Id, Fixture.TestBranchId, stock);
        return (product.Id, unit.Id, productBranch.Id);
    }

    private async Task<HttpClient> LoginCashierAsync()
    {
        var (_, username) = await UsersTestDataHelper.CreateUserWithRoleReturningUsernameAsync(
            Fixture.Factory.Services, Fixture.TestBranchId, UsersTestDataHelper.CashierRoleId, "alerts.cashier");
        return await LoginHelper.LoginAsAsync(Fixture, username, UsersTestDataHelper.DefaultPassword, appType: "Cashier");
    }

    private async Task<JsonElement> SellAsync(HttpClient client, Guid productId, Guid unitId, decimal quantity, decimal amount) =>
        await OkJsonAsync(await client.PostAsJsonAsync("/api/v1/sales", new
        {
            branchId = Fixture.TestBranchId, clientRequestId = Guid.NewGuid(), customerId = (Guid?)null, invoiceLevelDiscountAmount = 0m,
            items = new[] { new { productId, productUnitId = unitId, quantity, manualDiscountAmount = 0m, productBatchId = (Guid?)null } },
            payments = new[] { new { paymentMethodId = TestDataBuilder.CashPaymentMethodId, amount, externalReference = (string?)null, clientRequestId = Guid.NewGuid() } }
        }), "بيع");

    [Fact]
    public async Task كل_إشارة_سرقة_أو_خطر_بتطلع_بالتنبيهات_بدرجة_خطورتها_وبنص_عربي()
    {
        var (productId, unitId, productBranchId) = await SeedProductAsync("حليب التنبيهات", 1.000m, 200m);
        var admin = await CreateAuthenticatedClientAsync();
        var cashier = await LoginCashierAsync();

        // --- إلغاء فاتورة ---
        var toVoid = await SellAsync(cashier, productId, unitId, 2m, 2.000m);
        await OkJsonAsync(await cashier.PostAsJsonAsync($"/api/v1/sales/{toVoid.GetProperty("saleInvoiceId").GetGuid()}/void",
            new { reason = "CashierError", notes = "غلط" }), "إلغاء");

        // --- إرجاع ---
        var toReturn = await SellAsync(cashier, productId, unitId, 3m, 3.000m);
        var saleId = toReturn.GetProperty("saleInvoiceId").GetGuid();
        var saleInvoiceNumber = toReturn.GetProperty("invoiceNumber").GetString()!;
        var detail = await OkJsonAsync(await cashier.GetAsync($"/api/v1/sales/{saleId}"), "تفاصيل");
        await OkJsonAsync(await cashier.PostAsJsonAsync("/api/v1/returns", new
        {
            originalSaleInvoiceId = saleId, clientRequestId = Guid.NewGuid(), reason = "Defective", notes = (string?)null,
            items = new[] { new { saleInvoiceItemId = detail.GetProperty("items")[0].GetProperty("saleInvoiceItemId").GetGuid(), quantity = 1m } },
            refunds = new[] { new { paymentMethodId = TestDataBuilder.CashPaymentMethodId, amount = 1.000m, externalReference = (string?)null, clientRequestId = Guid.NewGuid() } }
        }), "إرجاع");

        // --- ضيافة وتلف فوق الحد اليومي (الافتراضي 10) ---
        await OkJsonAsync(await admin.PostAsJsonAsync("/api/v1/inventory/complimentary-issues", new
        {
            productId, productUnitId = unitId, branchId = Fixture.TestBranchId, quantity = 11m, reason = "ضيافة عرس"
        }), "ضيافة");
        await OkJsonAsync(await admin.PostAsJsonAsync("/api/v1/inventory/waste-issues", new
        {
            productId, productUnitId = unitId, branchId = Fixture.TestBranchId, quantity = 11m, reason = "Broken", notes = (string?)null,
            isReplacedBySupplier = false
        }), "تلف");

        // --- سعر شراء أعلى من المعتاد (المعتاد 0.500، الجديد 1.000 = +100% فوق حد 15%) ---
        var supplier = await OkJsonAsync(await admin.PostAsJsonAsync("/api/v1/suppliers", new
        {
            name = "مورد التنبيهات", contactName = (string?)null, phone = "0790000001", email = (string?)null,
            street = (string?)null, city = (string?)null, postalCode = (string?)null, country = (string?)null
        }), "مورد");
        object PurchaseBody(decimal unitCost) => new
        {
            branchId = Fixture.TestBranchId, supplierId = supplier.GetProperty("supplierId").GetGuid(), supplierInvoiceReference = (string?)null,
            items = new[] { new { productId, productUnitId = unitId, quantity = 10m, unitCost, existingProductBatchId = (Guid?)null, newBatchNumber = (string?)null, newBatchExpiryDate = (DateOnly?)null } }
        };
        await OkJsonAsync(await admin.PostAsJsonAsync("/api/v1/purchase-invoices", PurchaseBody(0.500m)), "شراء عادي");
        await OkJsonAsync(await admin.PostAsJsonAsync("/api/v1/purchase-invoices", PurchaseBody(1.000m)), "شراء مرتفع");

        // --- تنزيل سعر (ورفع ما بينبّه) ---
        string PriceUrl() => $"/api/v1/products/{productId}/branches/{productBranchId}/price-change-requests";
        await OkJsonAsync(await admin.PostAsJsonAsync(PriceUrl(), new { requestedPrice = 1.200m }), "رفع سعر");
        await OkJsonAsync(await admin.PostAsJsonAsync(PriceUrl(), new { requestedPrice = 0.900m }), "تنزيل سعر");

        // --- نقص بالجرد ---
        var stocktake = await OkJsonAsync(await admin.PostAsJsonAsync("/api/v1/stocktakes",
            new { branchId = Fixture.TestBranchId, includeAllProductsAtBranch = false, productIds = new[] { productId } }), "جرد");
        var stocktakeId = stocktake.GetProperty("stocktakeId").GetGuid();
        var stocktakeDetail = await OkJsonAsync(await admin.GetAsync($"/api/v1/stocktakes/{stocktakeId}"), "تفاصيل الجرد");
        var stocktakeItem = stocktakeDetail.GetProperty("items")[0];
        var expected = stocktakeItem.GetProperty("expectedQuantity").GetDecimal();
        await OkJsonAsync(await admin.PostAsJsonAsync(
            $"/api/v1/stocktakes/{stocktakeId}/items/{stocktakeItem.GetProperty("stocktakeItemId").GetGuid()}/count",
            new { countedQuantity = expected - 4m }), "عد");
        await OkJsonAsync(await admin.PostAsync($"/api/v1/stocktakes/{stocktakeId}/complete", null), "إنهاء الجرد");
        await OkJsonAsync(await admin.PostAsync($"/api/v1/stocktakes/{stocktakeId}/approve", null), "اعتماد الجرد");

        // --- عجز صندوق (الإعداد الافتراضي 0 = أي فرق بينبّه) ---
        var closing = await OkJsonAsync(await cashier.PostAsJsonAsync("/api/v1/cash-closings", new
        {
            branchId = Fixture.TestBranchId, businessDate = DateOnly.FromDateTime(DateTime.UtcNow), countedCash = 0m,
            countedDetails = Array.Empty<object>()
        }), "تقفيل");
        Assert.True(closing.GetProperty("variance").GetDecimal() < 0);

        // --- فاتورة أوفلاين انحذفت من الكاشير ---
        await OkJsonAsync(await cashier.PostAsJsonAsync("/api/v1/cashier-sync/report-discarded-pending-sale", new
        {
            clientRequestId = Guid.NewGuid(), branchId = Fixture.TestBranchId, createdAtLocal = DateTime.UtcNow, attemptCount = 7,
            lastErrorMessage = "422"
        }), "حذف فاتورة معلّقة");

        // --- قفل حساب بعد 5 محاولات فاشلة (تنبيه واحد بس، حتى لو كمّل يحاول) ---
        var (_, victim) = await UsersTestDataHelper.CreateUserWithRoleReturningUsernameAsync(
            Fixture.Factory.Services, Fixture.TestBranchId, UsersTestDataHelper.CashierRoleId, "alerts.victim");
        for (var attempt = 0; attempt < 7; attempt++)
        {
            await LoginHelper.LoginRawAsync(Fixture, victim, "wrong-password", appType: "Cashier");
        }

        // ================= التحقق =================
        var alerts = await GetAlertsAsync(admin);
        void AssertAlert(string titleStart, string severity, params string[] messageContains)
        {
            var alert = alerts.FirstOrDefault(a => a.Title.StartsWith(titleStart));
            Assert.True(alert is not null, $"ما في تنبيه يبلش بـ\"{titleStart}\". الموجود: {string.Join(" | ", alerts.Select(a => a.Title))}");
            Assert.Equal(severity, alert!.Severity);
            foreach (var text in messageContains)
            {
                Assert.Contains(text, alert.Message);
            }
        }

        AssertAlert("إلغاء فاتورة", "Warning", "خطأ كاشير", "فرع الاختبار");
        AssertAlert("إرجاع", "Warning", saleInvoiceNumber, "تالف/معيب", "مستخدم اختبار alerts.cashier");
        AssertAlert("ضيافة فوق الحد اليومي — حليب التنبيهات", "Warning", "ضيافة عرس");
        AssertAlert("تلف فوق الحد اليومي", "Warning", "مكسور");
        AssertAlert("سعر شراء أعلى من المعتاد", "Warning", "1.000", "0.500");
        AssertAlert("تنزيل سعر", "Warning", "1.200", "0.900");
        AssertAlert("نقص بالجرد", "Critical", "ناقص 4");
        // نفس المادة إلها إرجاع قبل الجرد (إرجاع 1 فوق) - ضد الإرجاع الوهمي (28/9/2026).
        AssertAlert("فرق جرد بمادة مرتجعة", "Critical", "ناقص 4", "مرتجع منها 1", saleInvoiceNumber, "مستخدم اختبار alerts.cashier");
        AssertAlert("تقفيل صندوق — عجز", "Critical", "فرع الاختبار");
        AssertAlert("حذف فاتورة كاشير أوفلاين نهائيًا", "Critical");
        AssertAlert($"قفل حساب — {victim}", "Critical", "الكاشير");

        Assert.Single(alerts, a => a.Title.StartsWith("قفل حساب"));
        Assert.Single(alerts, a => a.Title.StartsWith("تنزيل سعر")); // الرفع ما نبّه
        Assert.DoesNotContain(alerts, a => a.Message.Contains("CashierError") || a.Message.Contains("Defective") || a.Message.Contains("Broken"));

        // فلتر "الخطيرة بس" + عدّاد الرئيسية (آخر 24 ساعة)
        var critical = await GetAlertsAsync(admin, "&minSeverity=Critical");
        Assert.All(critical, a => Assert.Equal("Critical", a.Severity));
        Assert.Equal(5, critical.Count);
        var future = await GetAlertsAsync(admin, $"&minSeverity=Critical&sinceUtc={Uri.EscapeDataString(DateTime.UtcNow.AddHours(1).ToString("o"))}");
        Assert.Empty(future);

        // ================= لوحة التنبيهات الموحّدة (28/9/2026): كل تنبيه بيودّي على صفحته والفلتر جاهز =================
        string? LinkOf(string titleStart) => alerts.First(a => a.Title.StartsWith(titleStart)).LinkRoute;
        Assert.StartsWith("/sales?search=", LinkOf("إلغاء فاتورة"));
        Assert.Equal($"/returns?search={Uri.EscapeDataString(saleInvoiceNumber)}", LinkOf("إرجاع"));
        Assert.Equal("/reviews", LinkOf("ضيافة فوق الحد اليومي"));
        Assert.Equal("/reviews", LinkOf("تلف فوق الحد اليومي"));
        Assert.Equal("/purchases", LinkOf("سعر شراء أعلى من المعتاد"));
        Assert.Equal("/price-change-requests", LinkOf("تنزيل سعر"));
        Assert.Equal($"/stocktakes/{stocktakeId}", LinkOf("نقص بالجرد"));
        Assert.Equal("/cash-closings", LinkOf("تقفيل صندوق — عجز"));
        Assert.Equal("/sessions", LinkOf("قفل حساب"));
        Assert.Equal($"/returns?search={Uri.EscapeDataString(saleInvoiceNumber)}", LinkOf("فرق جرد بمادة مرتجعة"));

        // تبويب درجة وحدة بالضبط (severity) - مش "لحد درجة" (minSeverity).
        var warnings = await GetAlertsAsync(admin, "&severity=Warning");
        Assert.NotEmpty(warnings);
        Assert.All(warnings, a => Assert.Equal("Warning", a.Severity));

        // عدّاد غير المقروء لكل درجة + تعليم كمقروء
        async Task<(int Critical, int Warning, int Info)> SummaryAsync()
        {
            var json = await OkJsonAsync(await admin.GetAsync("/api/v1/notifications/summary"), "ملخّص");
            return (json.GetProperty("unreadCritical").GetInt32(), json.GetProperty("unreadWarning").GetInt32(), json.GetProperty("unreadInfo").GetInt32());
        }

        var before = await SummaryAsync();
        Assert.Equal(5, before.Critical);
        Assert.Equal(alerts.Count(a => a.Severity == "Warning"), before.Warning);

        var lockAlert = alerts.First(a => a.Title.StartsWith("قفل حساب"));
        Assert.Equal(HttpStatusCode.NoContent, (await admin.PostAsync($"/api/v1/notifications/{lockAlert.Id}/read", null)).StatusCode);
        Assert.Equal(HttpStatusCode.NoContent, (await admin.PostAsync($"/api/v1/notifications/{lockAlert.Id}/read", null)).StatusCode); // مرتين = عادي
        Assert.Equal((4, before.Warning, before.Info), await SummaryAsync());
        Assert.Equal("Read", (await GetAlertsAsync(admin)).First(a => a.Id == lockAlert.Id).Status);
        Assert.DoesNotContain(await GetAlertsAsync(admin, "&unreadOnly=true"), a => a.Id == lockAlert.Id);
        Assert.Equal(HttpStatusCode.NotFound, (await admin.PostAsync($"/api/v1/notifications/{Guid.NewGuid()}/read", null)).StatusCode);

        // "لحد درجة": المتوسطة وأقل بس - العالية بتضل
        await OkJsonAsync(await admin.PostAsync("/api/v1/notifications/read-all?maxSeverity=Warning", null), "تعليم المتوسطة");
        Assert.Equal((4, 0, 0), await SummaryAsync());
        await OkJsonAsync(await admin.PostAsync("/api/v1/notifications/read-all", null), "تعليم الكل");
        Assert.Equal((0, 0, 0), await SummaryAsync());

        // الكاشير بيشوف (Notifications.View من CashierDefaults) بس ما بيعلّم - القراءة مشتركة، فكان رح يقدر يخفي
        // تنبيه عجز صندوقه عن صاحب المحل (Notifications.Manage: Master Admin + مساعد أدمن بس).
        Assert.Equal(HttpStatusCode.OK, (await cashier.GetAsync("/api/v1/notifications/summary")).StatusCode);
        Assert.Equal(HttpStatusCode.Forbidden, (await cashier.PostAsync($"/api/v1/notifications/{lockAlert.Id}/read", null)).StatusCode);
        Assert.Equal(HttpStatusCode.Forbidden, (await cashier.PostAsync("/api/v1/notifications/read-all", null)).StatusCode);
    }

    [Fact]
    public async Task زيادة_بالصندوق_بتنبّه_كمهمة_مش_خطيرة_وبلا_فرق_ما_في_تنبيه()
    {
        var (productId, unitId, _) = await SeedProductAsync("منتج الزيادة", 2.000m, 10m);
        var admin = await CreateAuthenticatedClientAsync();
        var cashier = await LoginCashierAsync();
        await SellAsync(cashier, productId, unitId, 1m, 2.000m);

        var today = DateOnly.FromDateTime(DateTime.UtcNow);
        await OkJsonAsync(await cashier.PostAsJsonAsync("/api/v1/cash-closings", new
        {
            branchId = Fixture.TestBranchId, businessDate = today, countedCash = 2.000m, countedDetails = Array.Empty<object>()
        }), "تقفيل مطابق");
        Assert.DoesNotContain(await GetAlertsAsync(admin), a => a.Title.StartsWith("تقفيل صندوق"));

        await SellAsync(cashier, productId, unitId, 1m, 2.000m);
        await OkJsonAsync(await cashier.PostAsJsonAsync("/api/v1/cash-closings", new
        {
            branchId = Fixture.TestBranchId, businessDate = today, shiftNumber = 2, countedCash = 2.500m, countedDetails = Array.Empty<object>()
        }), "تقفيل بزيادة");

        var surplus = Assert.Single(await GetAlertsAsync(admin), a => a.Title.StartsWith("تقفيل صندوق"));
        Assert.Equal("Warning", surplus.Severity);
        Assert.Contains("زيادة", surplus.Title);
        Assert.Contains("بيعات ما انسجّلت", surplus.Message);
    }

    /// <summary>
    /// ضد الإرجاع الوهمي (28/9/2026): إرجاع ← المادة بتطلع "بانتظار جرد" ← جرد جزئي لقى نقص = تنبيه عالي الأولوية مربوط
    /// بفاتورة الإرجاع والكاشير ← المادة بتختفي من القائمة ← إرجاع جديد بيرجّعها (مع تاريخ آخر جرد).
    /// </summary>
    [Fact]
    public async Task مادة_مرتجعة_بتطلع_بانتظار_جرد_وفرق_بجردها_بينبّه_مربوط_بالإرجاع_والكاشير()
    {
        var (productId, unitId, _) = await SeedProductAsync("جبنة الإرجاع", 2.000m, 50m);
        var (otherId, otherUnitId, _) = await SeedProductAsync("خبز بلا إرجاع", 0.500m, 50m);
        var admin = await CreateAuthenticatedClientAsync();
        var cashier = await LoginCashierAsync();

        async Task<List<JsonElement>> PendingAsync() =>
            (await OkJsonAsync(await admin.GetAsync($"/api/v1/stocktakes/returned-pending?branchId={Fixture.TestBranchId}"), "بانتظار جرد"))
                .EnumerateArray().ToList();

        async Task<string> ReturnAsync(Guid product, Guid unit, decimal price)
        {
            var sale = await SellAsync(cashier, product, unit, 3m, 3 * price);
            var saleId = sale.GetProperty("saleInvoiceId").GetGuid();
            var detail = await OkJsonAsync(await cashier.GetAsync($"/api/v1/sales/{saleId}"), "تفاصيل");
            await OkJsonAsync(await cashier.PostAsJsonAsync("/api/v1/returns", new
            {
                originalSaleInvoiceId = saleId, clientRequestId = Guid.NewGuid(), reason = "Defective", notes = (string?)null,
                items = new[] { new { saleInvoiceItemId = detail.GetProperty("items")[0].GetProperty("saleInvoiceItemId").GetGuid(), quantity = 2m } },
                refunds = new[] { new { paymentMethodId = TestDataBuilder.CashPaymentMethodId, amount = 2 * price, externalReference = (string?)null, clientRequestId = Guid.NewGuid() } }
            }), "إرجاع");
            return sale.GetProperty("invoiceNumber").GetString()!;
        }

        Assert.DoesNotContain(await PendingAsync(), p => p.GetProperty("productId").GetGuid() == productId);
        await SellAsync(cashier, otherId, otherUnitId, 1m, 0.500m); // بيع بلا إرجاع ما بيطلع بالقائمة
        var saleNumber = await ReturnAsync(productId, unitId, 2.000m);

        var pending = Assert.Single(await PendingAsync(), p => p.GetProperty("productId").GetGuid() == productId);
        Assert.DoesNotContain(await PendingAsync(), p => p.GetProperty("productId").GetGuid() == otherId);
        Assert.Equal(2m, pending.GetProperty("quantityReturnedBase").GetDecimal());
        Assert.Equal(1, pending.GetProperty("returnCount").GetInt32());
        Assert.Equal(JsonValueKind.Null, pending.GetProperty("lastCountedAtUtc").ValueKind);
        var returnLine = pending.GetProperty("returns")[0];
        Assert.Equal(saleNumber, returnLine.GetProperty("originalSaleInvoiceNumber").GetString());
        Assert.Contains("alerts.cashier", returnLine.GetProperty("cashierName").GetString());
        var returnNumber = returnLine.GetProperty("returnInvoiceNumber").GetString()!;

        // جرد جزئي للمادة: الإرجاع كان وهمي - البضاعة (2) ما رجعت فعليًا → نقص 2.
        var stocktake = await OkJsonAsync(await admin.PostAsJsonAsync("/api/v1/stocktakes",
            new { branchId = Fixture.TestBranchId, includeAllProductsAtBranch = false, productIds = new[] { productId } }), "جرد جزئي");
        var stocktakeId = stocktake.GetProperty("stocktakeId").GetGuid();
        var item = (await OkJsonAsync(await admin.GetAsync($"/api/v1/stocktakes/{stocktakeId}"), "تفاصيل الجرد")).GetProperty("items")[0];
        await OkJsonAsync(await admin.PostAsJsonAsync(
            $"/api/v1/stocktakes/{stocktakeId}/items/{item.GetProperty("stocktakeItemId").GetGuid()}/count",
            new { countedQuantity = item.GetProperty("expectedQuantity").GetDecimal() - 2m }), "عد");
        await OkJsonAsync(await admin.PostAsync($"/api/v1/stocktakes/{stocktakeId}/complete", null), "إنهاء");
        await OkJsonAsync(await admin.PostAsync($"/api/v1/stocktakes/{stocktakeId}/approve", null), "اعتماد");

        var alert = Assert.Single(await GetAlertsAsync(admin), a => a.Title.StartsWith("فرق جرد بمادة مرتجعة"));
        Assert.Equal("Critical", alert.Severity);
        Assert.Contains("جبنة الإرجاع", alert.Message);
        Assert.Contains("ناقص 2", alert.Message);
        Assert.Contains(returnNumber, alert.Message);
        Assert.Contains("alerts.cashier", alert.Message);
        Assert.Contains("إرجاع وهمي", alert.Message);
        Assert.Equal($"/returns?search={Uri.EscapeDataString(saleNumber)}", alert.LinkRoute);

        // انعدّت = بتختفي. إرجاع جديد بيرجّعها بتاريخ آخر جرد.
        Assert.DoesNotContain(await PendingAsync(), p => p.GetProperty("productId").GetGuid() == productId);
        await ReturnAsync(productId, unitId, 2.000m);
        var again = Assert.Single(await PendingAsync(), p => p.GetProperty("productId").GetGuid() == productId);
        Assert.Equal(1, again.GetProperty("returnCount").GetInt32());
        Assert.NotEqual(JsonValueKind.Null, again.GetProperty("lastCountedAtUtc").ValueKind);

        // جرد بلا فرق ما بينبّه حتى لو المادة مرتجعة (الإرجاع حقيقي).
        var clean = await OkJsonAsync(await admin.PostAsJsonAsync("/api/v1/stocktakes",
            new { branchId = Fixture.TestBranchId, includeAllProductsAtBranch = false, productIds = new[] { productId } }), "جرد نظيف");
        var cleanId = clean.GetProperty("stocktakeId").GetGuid();
        var cleanItem = (await OkJsonAsync(await admin.GetAsync($"/api/v1/stocktakes/{cleanId}"), "تفاصيل")).GetProperty("items")[0];
        await OkJsonAsync(await admin.PostAsJsonAsync(
            $"/api/v1/stocktakes/{cleanId}/items/{cleanItem.GetProperty("stocktakeItemId").GetGuid()}/count",
            new { countedQuantity = cleanItem.GetProperty("expectedQuantity").GetDecimal() }), "عد مطابق");
        await OkJsonAsync(await admin.PostAsync($"/api/v1/stocktakes/{cleanId}/complete", null), "إنهاء");
        await OkJsonAsync(await admin.PostAsync($"/api/v1/stocktakes/{cleanId}/approve", null), "اعتماد");
        Assert.Single(await GetAlertsAsync(admin), a => a.Title.StartsWith("فرق جرد بمادة مرتجعة"));
        Assert.DoesNotContain(await PendingAsync(), p => p.GetProperty("productId").GetGuid() == productId);

        // الكاشير ما بيشوف القائمة (Stocktake.Manage)
        Assert.Equal(HttpStatusCode.Forbidden, (await cashier.GetAsync("/api/v1/stocktakes/returned-pending")).StatusCode);
    }
}
