using System.Globalization;
using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using System.Text.Json.Serialization;
using SupermarketSystem.Application.Common.Policies;
using SupermarketSystem.IntegrationTests.Common;
using Xunit;

namespace SupermarketSystem.IntegrationTests.Sales;

/// <summary>
/// طلب صاحب المشروع (24/9/2026): "اعمل تست لعمليات الارجاع وشوف هل بتطلع زي ما بدنا". كله HTTP
/// حقيقي بمستخدم كاشير فعلي، enums كنصوص زي تطبيق الويندوز، وكل أثر بيتفحص برقم محسوب مسبقًا:
/// المخزون، حالة الفاتورة، الكاش بالتقفيل، ملخص المبيعات، كشف الربح، التقارير، المراجعات، والتنبيهات.
/// </summary>
[Collection(DatabaseCollection.Name)]
public sealed class ReturnsEndToEndHttpTests : IntegrationTestBase
{
    public ReturnsEndToEndHttpTests(DatabaseFixture fixture) : base(fixture) { }

    private static readonly JsonSerializerOptions StringEnums = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        Converters = { new JsonStringEnumConverter() }
    };

    private sealed record Line(Guid SaleInvoiceItemId, Guid ProductId);

    private static async Task<JsonElement> OkAsync(HttpResponseMessage response, string what)
    {
        var body = await response.Content.ReadAsStringAsync();
        Assert.True(response.IsSuccessStatusCode, $"{what}: {(int)response.StatusCode} {body}");
        return string.IsNullOrWhiteSpace(body) ? default : JsonDocument.Parse(body).RootElement;
    }

    private async Task<(Guid MilkId, Guid MilkUnit, Guid RiceId, Guid RiceUnit)> SeedAsync(HttpClient admin)
    {
        Guid milkId, milkUnit, riceId, riceUnit;
        using (var scope = CreateScope())
        {
            var db = CreateDbContext(scope);
            await TestDataBuilder.EnsureDocumentSequencesProvisionedAsync(db, Fixture.TestBranchId);
            var (milk, mUnit) = await TestDataBuilder.CreateActiveProductAsync(db, "حليب الإرجاع");
            var (rice, rUnit) = await TestDataBuilder.CreateActiveProductAsync(db, "رز الإرجاع");
            await TestDataBuilder.CreateProductBranchAsync(db, milk.Id, Fixture.TestBranchId, 1.000m);
            await TestDataBuilder.CreateProductBranchAsync(db, rice.Id, Fixture.TestBranchId, 2.500m);
            (milkId, milkUnit, riceId, riceUnit) = (milk.Id, mUnit.Id, rice.Id, rUnit.Id);
        }

        // المخزون والتكلفة من فاتورة شراء حقيقية (حليب 0.600، رز 1.500) - عشان كشف الربح يكون إله تكلفة.
        var supplier = await OkAsync(await admin.PostAsJsonAsync("/api/v1/suppliers", new
        {
            name = "مورد الإرجاع", contactName = (string?)null, phone = "0790000002", email = (string?)null,
            street = (string?)null, city = (string?)null, postalCode = (string?)null, country = (string?)null
        }), "مورد");
        await OkAsync(await admin.PostAsJsonAsync("/api/v1/purchase-invoices", new
        {
            branchId = Fixture.TestBranchId, supplierId = supplier.GetProperty("supplierId").GetGuid(), supplierInvoiceReference = (string?)null,
            items = new[]
            {
                new { productId = milkId, productUnitId = milkUnit, quantity = 20m, unitCost = 0.600m, existingProductBatchId = (Guid?)null, newBatchNumber = (string?)null, newBatchExpiryDate = (DateOnly?)null },
                new { productId = riceId, productUnitId = riceUnit, quantity = 10m, unitCost = 1.500m, existingProductBatchId = (Guid?)null, newBatchNumber = (string?)null, newBatchExpiryDate = (DateOnly?)null }
            }
        }), "شراء");
        return (milkId, milkUnit, riceId, riceUnit);
    }

    private async Task<HttpClient> CashierAsync()
    {
        var (_, username) = await UsersTestDataHelper.CreateUserWithRoleReturningUsernameAsync(
            Fixture.Factory.Services, Fixture.TestBranchId, UsersTestDataHelper.CashierRoleId, "returns.cashier");
        return await LoginHelper.LoginAsAsync(Fixture, username, UsersTestDataHelper.DefaultPassword, appType: "Cashier");
    }

    private async Task<(Guid SaleId, string InvoiceNumber, List<Line> Lines)> SellAsync(
        HttpClient cashier, Guid paymentMethodId, decimal amount, params (Guid ProductId, Guid UnitId, decimal Quantity)[] items)
    {
        var sale = await OkAsync(await cashier.PostAsJsonAsync("/api/v1/sales", new
        {
            branchId = Fixture.TestBranchId, clientRequestId = Guid.NewGuid(), customerId = (Guid?)null, invoiceLevelDiscountAmount = 0m,
            items = items.Select(i => new { productId = i.ProductId, productUnitId = i.UnitId, quantity = i.Quantity, manualDiscountAmount = 0m, productBatchId = (Guid?)null }),
            payments = new[] { new { paymentMethodId, amount, externalReference = paymentMethodId == TestDataBuilder.VisaPaymentMethodId ? "VISA-1" : null, clientRequestId = Guid.NewGuid() } }
        }), "بيع");
        var saleId = sale.GetProperty("saleInvoiceId").GetGuid();
        var detail = await OkAsync(await cashier.GetAsync($"/api/v1/sales/{saleId}"), "تفاصيل");
        var lines = detail.GetProperty("items").EnumerateArray()
            .Select(i => new Line(i.GetProperty("saleInvoiceItemId").GetGuid(), i.GetProperty("productId").GetGuid())).ToList();
        return (saleId, sale.GetProperty("invoiceNumber").GetString()!, lines);
    }

    private static object ReturnBody(Guid saleId, Guid clientRequestId, string reason, Guid refundMethod, decimal refund,
        params (Guid SaleInvoiceItemId, decimal Quantity)[] items) => new
    {
        originalSaleInvoiceId = saleId, clientRequestId, reason, notes = (string?)null,
        items = items.Select(i => new { saleInvoiceItemId = i.SaleInvoiceItemId, quantity = i.Quantity }),
        refunds = new[] { new { paymentMethodId = refundMethod, amount = refund, externalReference = refundMethod == TestDataBuilder.VisaPaymentMethodId ? "VISA-R1" : null, clientRequestId = Guid.NewGuid() } }
    };

    private async Task<decimal> StockAsync(HttpClient admin, Guid productId)
    {
        var page = JsonDocument.Parse(await admin.GetStringAsync($"/api/v1/inventory/current-stock?branchId={Fixture.TestBranchId}&pageSize=100")).RootElement;
        return page.GetProperty("items").EnumerateArray().Single(s => s.GetProperty("productId").GetGuid() == productId).GetProperty("quantityOnHand").GetDecimal();
    }

    private static async Task<string> ErrorCodeAsync(HttpResponseMessage response) =>
        await response.Content.ReadAsStringAsync();

    [Fact]
    public async Task إرجاع_جزئي_ثم_كامل_مع_كل_الحالات_المرفوضة_وأثره_على_كل_مكان()
    {
        // الإعدادات الافتراضية صراحة - كاش الإعدادات بالتطبيق مشترك بين الاختبارات (Respawn بيصفّر
        // القاعدة بس)، فلو الاختبار التاني انشغّل أول كان رح يضل "مسموح" هون.
        using (var scope = CreateScope())
        {
            await TestDataBuilder.SetSettingAsync(scope, PosPolicyKeys.AllowCrossMethodRefund, false);
            await TestDataBuilder.SetSettingAsync(scope, PosPolicyKeys.HighValueReturnThreshold, 0m);
        }

        var admin = await CreateAuthenticatedClientAsync();
        var (milkId, milkUnit, riceId, riceUnit) = await SeedAsync(admin);
        var cashier = await CashierAsync();
        var cash = TestDataBuilder.CashPaymentMethodId;

        // بيع: حليب 5 × 1.000 + رز 2 × 2.500 = 10.000 كاش
        var (saleId, invoiceNumber, lines) = await SellAsync(cashier, cash, 10.000m, (milkId, milkUnit, 5m), (riceId, riceUnit, 2m));
        var milkLine = lines.Single(l => l.ProductId == milkId).SaleInvoiceItemId;
        var riceLine = lines.Single(l => l.ProductId == riceId).SaleInvoiceItemId;
        Assert.Equal(15m, await StockAsync(admin, milkId));
        Assert.Equal(8m, await StockAsync(admin, riceId));

        // --- إرجاع 1: حليب 2 (غيّر رأيه) - جزئي ---
        var firstRequestId = Guid.NewGuid();
        var first = await OkAsync(await cashier.PostAsJsonAsync("/api/v1/returns",
            ReturnBody(saleId, firstRequestId, "CustomerChangedMind", cash, 2.000m, (milkLine, 2m)), StringEnums), "إرجاع 1");
        Assert.Equal(2.000m, first.GetProperty("totalAmount").GetDecimal());
        Assert.Equal("PartiallyReturned", first.GetProperty("originalInvoiceNewStatus").GetString());
        Assert.Equal(17m, await StockAsync(admin, milkId));

        // --- نفس الإرجاع انبعت مرة تانية (الرد ضاع) - ما بيتكرر ---
        var replay = await OkAsync(await cashier.PostAsJsonAsync("/api/v1/returns",
            ReturnBody(saleId, firstRequestId, "CustomerChangedMind", cash, 2.000m, (milkLine, 2m)), StringEnums), "إعادة إرسال");
        Assert.True(replay.GetProperty("wasReplay").GetBoolean());
        Assert.Equal(first.GetProperty("returnInvoiceId").GetGuid(), replay.GetProperty("returnInvoiceId").GetGuid());
        Assert.Equal(17m, await StockAsync(admin, milkId));

        // --- مرفوض: أكتر من الباقي (باقي 3، طلب 4) ---
        var tooMany = await cashier.PostAsJsonAsync("/api/v1/returns",
            ReturnBody(saleId, Guid.NewGuid(), "Defective", cash, 4.000m, (milkLine, 4m)), StringEnums);
        Assert.Equal(HttpStatusCode.UnprocessableEntity, tooMany.StatusCode);
        Assert.Contains("Return.ExceedsSoldQuantity", await ErrorCodeAsync(tooMany));

        // --- مرفوض: استرجاع أكتر من قيمة المرتجع (حبة بـ1.000، استرجاع 2.000) ---
        var overRefund = await cashier.PostAsJsonAsync("/api/v1/returns",
            ReturnBody(saleId, Guid.NewGuid(), "Defective", cash, 2.000m, (milkLine, 1m)), StringEnums);
        Assert.Equal(HttpStatusCode.UnprocessableEntity, overRefund.StatusCode);
        Assert.Contains("Return.RefundExceedsReturnTotal", await ErrorCodeAsync(overRefund));

        // --- مرفوض: استرجاع بفيزا لبيع كاش (مقفول افتراضيًا) ---
        var crossMethod = await cashier.PostAsJsonAsync("/api/v1/returns",
            ReturnBody(saleId, Guid.NewGuid(), "Defective", TestDataBuilder.VisaPaymentMethodId, 1.000m, (milkLine, 1m)), StringEnums);
        Assert.Equal(HttpStatusCode.Forbidden, crossMethod.StatusCode);
        Assert.Contains("Return.CrossMethodRefundDenied", await ErrorCodeAsync(crossMethod));
        Assert.Equal(17m, await StockAsync(admin, milkId)); // ولا رفض غيّر المخزون

        // --- إرجاع 2: الباقي كله (حليب 3 + رز 2 = 8.000، تالف) - كامل ---
        var second = await OkAsync(await cashier.PostAsJsonAsync("/api/v1/returns",
            ReturnBody(saleId, Guid.NewGuid(), "Defective", cash, 8.000m, (milkLine, 3m), (riceLine, 2m)), StringEnums), "إرجاع 2");
        Assert.Equal("FullyReturned", second.GetProperty("originalInvoiceNewStatus").GetString());
        Assert.Equal(20m, await StockAsync(admin, milkId));
        Assert.Equal(10m, await StockAsync(admin, riceId));

        // --- مرفوض: إرجاع من فاتورة مرتجعة بالكامل ---
        var afterFull = await cashier.PostAsJsonAsync("/api/v1/returns",
            ReturnBody(saleId, Guid.NewGuid(), "Defective", cash, 1.000m, (milkLine, 1m)), StringEnums);
        Assert.Equal(HttpStatusCode.UnprocessableEntity, afterFull.StatusCode);
        Assert.Contains("Return.InvoiceNotReturnable", await ErrorCodeAsync(afterFull));

        // --- مرفوض: إرجاع من فاتورة ملغاة ---
        var (voidedId, _, voidedLines) = await SellAsync(cashier, cash, 1.000m, (milkId, milkUnit, 1m));
        await OkAsync(await cashier.PostAsJsonAsync($"/api/v1/sales/{voidedId}/void", new { reason = "CashierError", notes = (string?)null }, StringEnums), "إلغاء");
        var onVoided = await cashier.PostAsJsonAsync("/api/v1/returns",
            ReturnBody(voidedId, Guid.NewGuid(), "Defective", cash, 1.000m, (voidedLines[0].SaleInvoiceItemId, 1m)), StringEnums);
        Assert.Equal(HttpStatusCode.UnprocessableEntity, onVoided.StatusCode);
        Assert.Contains("Return.InvoiceNotReturnable", await ErrorCodeAsync(onVoided));
        Assert.Equal(20m, await StockAsync(admin, milkId));

        // ================= الأثر على كل مكان =================
        var from = Uri.EscapeDataString(DateTime.UtcNow.AddHours(-1).ToString("o"));
        var to = Uri.EscapeDataString(DateTime.UtcNow.AddHours(1).ToString("o"));

        // صفحة المبيعات: الفاتورة "مرتجعة بالكامل" (4) والمرتجع 10.000
        var sales = JsonDocument.Parse(await admin.GetStringAsync($"/api/v1/sales?branchId={Fixture.TestBranchId}&pageSize=50")).RootElement
            .GetProperty("items").EnumerateArray().ToList();
        var fullyReturned = sales.Single(s => s.GetProperty("id").GetGuid() == saleId);
        Assert.Equal(4, fullyReturned.GetProperty("statusCode").GetInt32());
        Assert.Equal(10.000m, fullyReturned.GetProperty("totalReturnedAmount").GetDecimal());

        // ملخص المبيعات: 10.000 مبيعات - 10.000 مرتجعات = صفر (الملغاة مش محسوبة)
        var summary = JsonDocument.Parse(await admin.GetStringAsync($"/api/v1/reports/sales/summary?branchId={Fixture.TestBranchId}&fromUtc={from}&toUtc={to}")).RootElement.GetProperty("period");
        Assert.Equal(10.000m, summary.GetProperty("totalSales").GetDecimal());
        Assert.Equal(10.000m, summary.GetProperty("totalReturnedAmount").GetDecimal());
        Assert.Equal(0m, summary.GetProperty("netRevenue").GetDecimal());

        // كشف الربح: كل شي رجع = إيراد صفر وتكلفة صفر (مش خسارة وهمية بتكلفة بضاعة رجعت للرف)
        var profit = JsonDocument.Parse(await admin.GetStringAsync(
            $"/api/v1/finance/profit-statement?branchId={Fixture.TestBranchId}&year={DateTime.UtcNow.Year}&month={DateTime.UtcNow.Month}")).RootElement;
        Assert.Equal(0m, profit.GetProperty("netRevenue").GetDecimal());
        Assert.Equal(0m, profit.GetProperty("costOfGoodsSold").GetDecimal());
        Assert.Equal(0m, profit.GetProperty("grossProfit").GetDecimal());

        // التقفيل: 10.000 + 1.000 (الملغاة) - 1.000 (رجعت بالإلغاء) - 2.000 - 8.000 (استرجاع) = صفر
        var closing = await OkAsync(await cashier.PostAsJsonAsync("/api/v1/cash-closings", new
        {
            branchId = Fixture.TestBranchId, businessDate = DateOnly.FromDateTime(DateTime.UtcNow), countedCash = 0m,
            countedDetails = Array.Empty<object>()
        }), "تقفيل");
        Assert.Equal(0m, closing.GetProperty("expectedCash").GetDecimal());
        Assert.Equal(0m, closing.GetProperty("variance").GetDecimal());

        // التقارير: إرجاعين فعليين بس (الإعادة والمرفوضة ما انحسبت)
        var recent = JsonDocument.Parse(await admin.GetStringAsync($"/api/v1/reports/returns/recent?branchId={Fixture.TestBranchId}&fromUtc={from}&toUtc={to}")).RootElement
            .GetProperty("items").EnumerateArray().ToList();
        Assert.Equal(2, recent.Count);
        Assert.Contains(recent, r => r.GetProperty("reason").GetString() == "CustomerChangedMind" && r.GetProperty("totalRefundedAmount").GetDecimal() == 2.000m);
        Assert.Contains(recent, r => r.GetProperty("reason").GetString() == "Defective" && r.GetProperty("totalRefundedAmount").GetDecimal() == 8.000m);

        var frequency = JsonDocument.Parse(await admin.GetStringAsync($"/api/v1/reports/returns/frequency-by-product?branchId={Fixture.TestBranchId}&fromUtc={from}&toUtc={to}")).RootElement
            .GetProperty("items").EnumerateArray().ToList();
        var milkFrequency = frequency.Single(f => f.GetProperty("productId").GetGuid() == milkId);
        Assert.Equal(2, milkFrequency.GetProperty("returnCount").GetInt32());
        Assert.Equal(5m, milkFrequency.GetProperty("totalQuantityReturned").GetDecimal());

        // المراجعات: الإرجاعين بانتظار مراجعة، ولما تنراجع بتختفي
        var reviews = JsonDocument.Parse(await admin.GetStringAsync("/api/v1/reviews")).RootElement.GetProperty("items").EnumerateArray()
            .Where(i => i.GetProperty("type").GetString() == "Return").ToList();
        Assert.Equal(2, reviews.Count);
        await OkAsync(await admin.PostAsync($"/api/v1/returns/{first.GetProperty("returnInvoiceId").GetGuid()}/mark-reviewed", null), "مراجعة");
        var reviewsAfter = JsonDocument.Parse(await admin.GetStringAsync("/api/v1/reviews")).RootElement.GetProperty("items").EnumerateArray()
            .Count(i => i.GetProperty("type").GetString() == "Return");
        Assert.Equal(1, reviewsAfter);

        // التنبيهات: تنبيه لكل إرجاع فعلي (2)، مش للإعادة ولا للمرفوض
        var alerts = JsonDocument.Parse(await admin.GetStringAsync("/api/v1/notifications?pageSize=100")).RootElement.GetProperty("items").EnumerateArray()
            .Where(n => n.GetProperty("title").GetString()!.StartsWith("إرجاع")).ToList();
        Assert.Equal(2, alerts.Count);
        Assert.All(alerts, a => Assert.Contains(invoiceNumber, a.GetProperty("message").GetString()));
    }

    [Fact]
    public async Task إرجاع_بطريقة_دفع_مختلفة_أو_فوق_حد_القيمة_بينقبل_بس_بتنبيه_خطير()
    {
        var admin = await CreateAuthenticatedClientAsync();
        var (milkId, milkUnit, _, _) = await SeedAsync(admin);
        using (var scope = CreateScope())
        {
            await TestDataBuilder.SetSettingAsync(scope, PosPolicyKeys.AllowCrossMethodRefund, true);
            await TestDataBuilder.SetSettingAsync(scope, PosPolicyKeys.HighValueReturnThreshold, 5m);
        }
        var cashier = await CashierAsync();

        // بيع كاش، استرجاع فيزا (مسموح بالإعدادات، بس لازم ينتبهله)
        var (crossSale, _, crossLines) = await SellAsync(cashier, TestDataBuilder.CashPaymentMethodId, 2.000m, (milkId, milkUnit, 2m));
        await OkAsync(await cashier.PostAsJsonAsync("/api/v1/returns",
            ReturnBody(crossSale, Guid.NewGuid(), "Defective", TestDataBuilder.VisaPaymentMethodId, 1.000m, (crossLines[0].SaleInvoiceItemId, 1m)), StringEnums), "استرجاع بفيزا");

        // إرجاع قيمته 6.000 فوق الحد 5.000
        var (bigSale, _, bigLines) = await SellAsync(cashier, TestDataBuilder.CashPaymentMethodId, 6.000m, (milkId, milkUnit, 6m));
        var big = await OkAsync(await cashier.PostAsJsonAsync("/api/v1/returns",
            ReturnBody(bigSale, Guid.NewGuid(), "WrongItem", TestDataBuilder.CashPaymentMethodId, 6.000m, (bigLines[0].SaleInvoiceItemId, 6m)), StringEnums), "إرجاع كبير");
        Assert.NotEmpty(big.GetProperty("reviewFlags").EnumerateArray());

        var critical = JsonDocument.Parse(await admin.GetStringAsync("/api/v1/notifications?pageSize=100&minSeverity=Critical")).RootElement
            .GetProperty("items").EnumerateArray().Where(n => n.GetProperty("title").GetString()!.StartsWith("إرجاع")).ToList();
        Assert.Equal(2, critical.Count);
        Assert.Contains(critical, n => n.GetProperty("message").GetString()!.Contains("طريقة دفع غير طريقة البيع الأصلية"));
        Assert.Contains(critical, n => n.GetProperty("message").GetString()!.Contains(
            "فوق حد المراجعة " + 5m.ToString("0.000", CultureInfo.InvariantCulture)));
    }
}
