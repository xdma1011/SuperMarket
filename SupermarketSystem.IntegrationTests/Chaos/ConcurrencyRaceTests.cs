using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using SupermarketSystem.IntegrationTests.Common;
using Xunit;
using Xunit.Abstractions;

namespace SupermarketSystem.IntegrationTests.Chaos;

/// <summary>
/// سباقات تزامن حقيقية (16/10/2026) - كاشيرين/تطبيق/مزامنة خلفية بيضربوا نفس السجل بنفس اللحظة. كله HTTP حقيقي، طلبات متوازية من بوابة بداية وحدة
/// (<c>Task.WhenAll</c>)، وبعدين فحص الثوابت على <b>القاعدة نفسها</b>: ولا فاتورة مكررة، ولا مخزون ضايع، ولا دفعة/إرجاع زايد عن اللي مسموح،
/// ولا 5xx (deadlock/تعارض مش مترجم). كان ما في ولا اختبار تزامن بالمشروع قبل هالملف.
/// </summary>
[Collection(DatabaseCollection.Name)]
public sealed class ConcurrencyRaceTests : IntegrationTestBase
{
    private readonly ITestOutputHelper _output;
    public ConcurrencyRaceTests(DatabaseFixture fixture, ITestOutputHelper output) : base(fixture) { _output = output; }

    private const decimal Price = 2.250m;

    private async Task<(HttpClient Cashier, Guid ProductId, Guid UnitId, Guid CustomerId)> SetUpAsync(decimal stock)
    {
        Guid productId, unitId, customerId;
        using (var scope = CreateScope())
        {
            await TestDataBuilder.ActAsAdminAsync(scope, Fixture);
            var db = CreateDbContext(scope);
            await TestDataBuilder.EnsureDocumentSequencesProvisionedAsync(db, Fixture.TestBranchId);
            var (product, unit) = await TestDataBuilder.CreateActiveProductAsync(db, "منتج السباق");
            await TestDataBuilder.CreateProductBranchAsync(db, product.Id, Fixture.TestBranchId, sellingPrice: Price);
            await TestDataBuilder.SetStockAsync(db, product.Id, Fixture.TestBranchId, stock);
            customerId = (await TestDataBuilder.CreateCustomerAsync(db, "زبون السباق", "0791234567")).Id;
            productId = product.Id;
            unitId = unit.Id;
        }

        var (_, name) = await UsersTestDataHelper.CreateUserWithRoleReturningUsernameAsync(
            Fixture.Factory.Services, Fixture.TestBranchId, UsersTestDataHelper.CashierRoleId, "race.cashier");
        var cashier = await LoginHelper.LoginAsAsync(Fixture, name, UsersTestDataHelper.DefaultPassword, appType: "Cashier");
        return (cashier, productId, unitId, customerId);
    }

    private object SaleBody(Guid productId, Guid unitId, decimal quantity, decimal paid, Guid? clientRequestId = null, Guid? customerId = null, bool credit = false) => new
    {
        branchId = Fixture.TestBranchId, clientRequestId = clientRequestId ?? Guid.NewGuid(), customerId, allowCreditSale = credit, invoiceLevelDiscountAmount = 0m,
        items = new[] { new { productId, productUnitId = unitId, quantity, manualDiscountAmount = 0m, productBatchId = (Guid?)null, catalogVersion = (long?)null, promotionId = (Guid?)null } },
        payments = paid > 0
            ? new[] { new { paymentMethodId = TestDataBuilder.CashPaymentMethodId, amount = paid, externalReference = (string?)null, clientRequestId = Guid.NewGuid() } }
            : [],
        preparedOrderId = (Guid?)null, customerPhone = customerId is null ? null : "0791234567"
    };

    /// <summary>يطلق كل المهام بنفس اللحظة (بوابة) ويرجع النتائج.</summary>
    private static async Task<T[]> RaceAsync<T>(int count, Func<int, Task<T>> action)
    {
        var gate = new TaskCompletionSource();
        var tasks = Enumerable.Range(0, count).Select(i => Task.Run(async () => { await gate.Task; return await action(i); })).ToArray();
        await Task.Delay(100);
        gate.SetResult();
        return await Task.WhenAll(tasks);
    }

    private async Task<decimal> StockAsync(Guid productId)
    {
        using var scope = CreateScope();
        var db = CreateDbContext(scope);
        return await db.Stocks.IgnoreQueryFilters().AsNoTracking().Where(s => s.ProductId == productId && s.BranchId == Fixture.TestBranchId).SumAsync(s => s.QuantityOnHand);
    }

    /// <summary>بعد كل سباق: القاعدة لازم تضل متسقة مع نفسها (مدقّق الثوابت). المخزون المبذور مباشرة (بلا حركة) بيتعدّ افتتاحي.</summary>
    private async Task AssertInvariantsAsync(Guid productId, decimal openingStock)
    {
        using var scope = CreateScope();
        var violations = await InvariantAuditor.AuditAsync(CreateDbContext(scope), new Dictionary<Guid, decimal> { [productId] = openingStock });
        Assert.True(violations.Count == 0, "خرق ثوابت بعد السباق:\n" + string.Join('\n', violations));
    }

    private static void AssertNo5xx(IEnumerable<HttpResponseMessage> responses, string what) =>
        Assert.True(responses.All(r => (int)r.StatusCode < 500),
            $"{what}: ردود 5xx تحت التزامن: {string.Join(',', responses.Where(r => (int)r.StatusCode >= 500).Select(r => (int)r.StatusCode))}");

    [Fact]
    public async Task بيع_متزامن_بنفس_ClientRequestId_بينسجّل_مرة_وحدة_والباقي_إعادة()
    {
        var (cashier, productId, unitId, _) = await SetUpAsync(stock: 100m);
        var requestId = Guid.NewGuid();
        var body = SaleBody(productId, unitId, 2m, 2m * Price, requestId);

        var responses = await RaceAsync(12, _ => cashier.PostAsJsonAsync("/api/v1/sales", body));
        AssertNo5xx(responses, "بيع بنفس المعرّف");
        var ok = responses.Where(r => r.IsSuccessStatusCode).ToList();
        Assert.True(ok.Count >= 1);

        var ids = new HashSet<Guid>();
        foreach (var r in ok) ids.Add(JsonDocument.Parse(await r.Content.ReadAsStringAsync()).RootElement.GetProperty("saleInvoiceId").GetGuid());
        Assert.Single(ids);

        using var scope = CreateScope();
        var db = CreateDbContext(scope);
        Assert.Equal(1, await db.SaleInvoices.IgnoreQueryFilters().CountAsync(i => i.ClientRequestId == requestId));
        Assert.Equal(98m, await StockAsync(productId));
        Assert.Equal(2m * Price, await db.SaleInvoicePayments.IgnoreQueryFilters().SumAsync(p => p.Amount));
        await AssertInvariantsAsync(productId, 100m);
    }

    [Fact]
    public async Task بيوع_متزامنة_مختلفة_أرقام_فواتير_فريدة_ومخزون_ودفعات_بدقة()
    {
        const int n = 24;
        var (cashier, productId, unitId, _) = await SetUpAsync(stock: 100m);

        var responses = await RaceAsync(n, _ => cashier.PostAsJsonAsync("/api/v1/sales", SaleBody(productId, unitId, 1m, Price)));
        AssertNo5xx(responses, "بيوع متزامنة");
        var failed = responses.Where(r => !r.IsSuccessStatusCode).ToList();
        Assert.True(failed.Count == 0, "بيوع سليمة انرفضت: " + string.Join(" | ", failed.Select(r => $"{(int)r.StatusCode} {r.Content.ReadAsStringAsync().Result}")));

        var numbers = new List<string>();
        foreach (var r in responses) numbers.Add(JsonDocument.Parse(await r.Content.ReadAsStringAsync()).RootElement.GetProperty("invoiceNumber").GetString()!);
        Assert.Equal(n, numbers.Distinct().Count());

        Assert.Equal(100m - n, await StockAsync(productId));
        using var scope = CreateScope();
        var db = CreateDbContext(scope);
        Assert.Equal(n, await db.SaleInvoices.IgnoreQueryFilters().CountAsync());
        Assert.Equal(n * Price, await db.SaleInvoicePayments.IgnoreQueryFilters().SumAsync(p => p.Amount));
        // كل فاتورة مدفوعها = إجماليها بالضبط (العداد المحروس ما ضيّع ولا زوّد).
        Assert.Equal(0, await db.SaleInvoices.IgnoreQueryFilters().CountAsync(i => i.TotalPaidAmount != i.TotalAmount));
        await AssertInvariantsAsync(productId, 100m);
    }

    [Fact]
    public async Task بيوع_متزامنة_على_آخر_كمية_المخزون_بينقص_بدقة_ويتعلّم_السالب_ولا_تضيع_ولا_تتكرر_كمية()
    {
        const int n = 16;
        var (cashier, productId, unitId, _) = await SetUpAsync(stock: 5m);

        var responses = await RaceAsync(n, _ => cashier.PostAsJsonAsync("/api/v1/sales", SaleBody(productId, unitId, 1m, Price)));
        AssertNo5xx(responses, "بيع على آخر كمية");
        var successes = responses.Count(r => r.IsSuccessStatusCode);
        // AllowNegativeStock افتراضيًا true (§1.6: نسمح ونعلّم) -> كل البيوع لازم تنجح، والمخزون ينقص بعددها بالضبط (-11).
        Assert.Equal(n, successes);
        Assert.Equal(5m - n, await StockAsync(productId));
        await AssertInvariantsAsync(productId, 5m);
    }

    [Fact]
    public async Task دفعات_متزامنة_على_فاتورة_دين_ما_بتتجاوز_إجمالي_الفاتورة()
    {
        var (cashier, productId, unitId, customerId) = await SetUpAsync(stock: 100m);
        var sale = await cashier.PostAsJsonAsync("/api/v1/sales", SaleBody(productId, unitId, 2m, 0m, customerId: customerId, credit: true));
        Assert.True(sale.IsSuccessStatusCode, await sale.Content.ReadAsStringAsync());
        var saleId = JsonDocument.Parse(await sale.Content.ReadAsStringAsync()).RootElement.GetProperty("saleInvoiceId").GetGuid();
        var total = 2m * Price; // 4.500

        var responses = await RaceAsync(10, _ => cashier.PostAsJsonAsync($"/api/v1/sales/{saleId}/payments", new
        {
            paymentMethodId = TestDataBuilder.CashPaymentMethodId, amount = 3.000m, externalReference = (string?)null, clientRequestId = Guid.NewGuid()
        }));
        AssertNo5xx(responses, "دفعات متزامنة");

        using var scope = CreateScope();
        var db = CreateDbContext(scope);
        var invoice = await db.SaleInvoices.IgnoreQueryFilters().AsNoTracking().SingleAsync(i => i.Id == saleId);
        var paidRows = await db.SaleInvoicePayments.IgnoreQueryFilters().Where(p => p.SaleInvoiceId == saleId).SumAsync(p => p.Amount);
        Assert.True(invoice.TotalPaidAmount <= total, $"دفع زايد: {invoice.TotalPaidAmount} من {total}");
        Assert.Equal(invoice.TotalPaidAmount, paidRows);
        Assert.Equal(1, responses.Count(r => r.IsSuccessStatusCode)); // 3.000 مرة وحدة بس (الثانية 3.000 > المتبقي 1.500)
        await AssertInvariantsAsync(productId, 100m);
    }

    [Fact]
    public async Task إرجاعات_متزامنة_لنفس_السطر_ما_بترجع_أكتر_من_المباع_والمخزون_بيرجع_بدقة()
    {
        var (cashier, productId, unitId, _) = await SetUpAsync(stock: 100m);
        var sale = await cashier.PostAsJsonAsync("/api/v1/sales", SaleBody(productId, unitId, 3m, 3m * Price));
        Assert.True(sale.IsSuccessStatusCode, await sale.Content.ReadAsStringAsync());
        var saleId = JsonDocument.Parse(await sale.Content.ReadAsStringAsync()).RootElement.GetProperty("saleInvoiceId").GetGuid();
        var detail = JsonDocument.Parse(await cashier.GetStringAsync($"/api/v1/sales/{saleId}")).RootElement;
        var lineId = detail.GetProperty("items")[0].GetProperty("saleInvoiceItemId").GetGuid();

        var responses = await RaceAsync(8, _ => cashier.PostAsJsonAsync("/api/v1/returns", new
        {
            originalSaleInvoiceId = saleId, clientRequestId = Guid.NewGuid(), reason = "CustomerChangedMind", notes = (string?)null,
            items = new[] { new { saleInvoiceItemId = lineId, quantity = 1m } },
            refunds = new[] { new { paymentMethodId = TestDataBuilder.CashPaymentMethodId, amount = Price, externalReference = (string?)null, clientRequestId = Guid.NewGuid() } }
        }));
        AssertNo5xx(responses, "إرجاعات متزامنة");

        // التعارض المتزامن على نفس الفاتورة = 409 (الحارس الذري رفض بدل ما يضيّع/يكرر) والعميل بيعيد المحاولة - مقبول؛ المرفوض لازم 409/422 مش 5xx.
        var successes = responses.Count(r => r.IsSuccessStatusCode);
        Assert.InRange(successes, 1, 3);
        Assert.All(responses.Where(r => !r.IsSuccessStatusCode), r => Assert.True(r.StatusCode is HttpStatusCode.Conflict or HttpStatusCode.UnprocessableEntity, $"رفض غير متوقع {(int)r.StatusCode}"));

        // إعادة المحاولة المتتالية (زي ما الكاشير بيعمل) لازم توصل لـ3 بالضبط، والرابعة مرفوضة.
        async Task<HttpResponseMessage> ReturnOneAsync() => await cashier.PostAsJsonAsync("/api/v1/returns", new
        {
            originalSaleInvoiceId = saleId, clientRequestId = Guid.NewGuid(), reason = "CustomerChangedMind", notes = (string?)null,
            items = new[] { new { saleInvoiceItemId = lineId, quantity = 1m } },
            refunds = new[] { new { paymentMethodId = TestDataBuilder.CashPaymentMethodId, amount = Price, externalReference = (string?)null, clientRequestId = Guid.NewGuid() } }
        });
        for (var i = successes; i < 3; i++) Assert.True((await ReturnOneAsync()).IsSuccessStatusCode);
        Assert.Equal(HttpStatusCode.UnprocessableEntity, (await ReturnOneAsync()).StatusCode);

        using var scope = CreateScope();
        var db = CreateDbContext(scope);
        var item = await db.SaleInvoiceItems.IgnoreQueryFilters().AsNoTracking().SingleAsync(i => i.Id == lineId);
        Assert.Equal(3m, item.QuantityReturned);
        var invoice = await db.SaleInvoices.IgnoreQueryFilters().AsNoTracking().SingleAsync(i => i.Id == saleId);
        Assert.Equal(3m * Price, invoice.TotalReturnedAmount);
        Assert.Equal(3, await db.ReturnInvoices.IgnoreQueryFilters().CountAsync());
        Assert.Equal(100m, await StockAsync(productId)); // 100 - 3 مباع + 3 مرتجع
        await AssertInvariantsAsync(productId, 100m);
    }

    [Fact]
    public async Task إلغاء_متزامن_لنفس_الفاتورة_بينجح_مرة_وحدة_والمخزون_بيرجع_مرة_وحدة()
    {
        var (cashier, productId, unitId, _) = await SetUpAsync(stock: 100m);
        var sale = await cashier.PostAsJsonAsync("/api/v1/sales", SaleBody(productId, unitId, 4m, 4m * Price));
        var saleId = JsonDocument.Parse(await sale.Content.ReadAsStringAsync()).RootElement.GetProperty("saleInvoiceId").GetGuid();
        Assert.Equal(96m, await StockAsync(productId));

        var responses = await RaceAsync(6, _ => cashier.PostAsJsonAsync($"/api/v1/sales/{saleId}/void", new { reason = "CashierError", notes = (string?)null }));
        AssertNo5xx(responses, "إلغاء متزامن");
        Assert.Equal(1, responses.Count(r => r.IsSuccessStatusCode));
        Assert.Equal(100m, await StockAsync(productId));
        await AssertInvariantsAsync(productId, 100m);
    }

    [Fact]
    public async Task باركود_بنفس_القيمة_يتضاف_بنفس_اللحظة_لصنفين_واحد_بس_بينجح_والباقي_409()
    {
        var admin = await CreateAuthenticatedClientAsync();
        Guid p1, u1, p2, u2;
        using (var scope = CreateScope())
        {
            var db = CreateDbContext(scope);
            if (!await db.SystemSettings.AnyAsync(s => s.Key == "Catalog.Version"))
            {
                db.SystemSettings.Add(new SupermarketSystem.Domain.Settings.SystemSetting("Catalog.Version", "1", null));
                await db.SaveChangesAsync();
            }

            var (a, au) = await TestDataBuilder.CreateActiveProductAsync(db, "صنف باركود 1");
            var (b, bu) = await TestDataBuilder.CreateActiveProductAsync(db, "صنف باركود 2");
            (p1, u1, p2, u2) = (a.Id, au.Id, b.Id, bu.Id);
        }

        var responses = await RaceAsync(10, i => admin.PostAsJsonAsync(
            i % 2 == 0 ? $"/api/v1/products/{p1}/units/{u1}/barcodes" : $"/api/v1/products/{p2}/units/{u2}/barcodes",
            new { barcodeValue = "6291009999990" }));
        AssertNo5xx(responses, "باركود متزامن");
        Assert.Equal(1, responses.Count(r => r.IsSuccessStatusCode));
        Assert.All(responses.Where(r => !r.IsSuccessStatusCode), r => Assert.Equal(HttpStatusCode.Conflict, r.StatusCode));

        using var check = CreateScope();
        var cdb = CreateDbContext(check);
        Assert.Equal(1, await cdb.ProductBarcodes.IgnoreQueryFilters().CountAsync(b => b.BarcodeValue == "6291009999990"));
    }

    [Fact]
    public async Task فتح_درج_بنفس_ClientRequestId_متزامن_بينسجّل_مرة_وحدة()
    {
        var (cashier, _, _, _) = await SetUpAsync(stock: 1m);
        var requestId = Guid.NewGuid();
        var responses = await RaceAsync(10, _ => cashier.PostAsJsonAsync("/api/v1/cash-drawer/open-events",
            new { branchId = Fixture.TestBranchId, reason = "فكة", clientRequestId = requestId, occurredAtUtc = (DateTime?)null }));
        AssertNo5xx(responses, "فتح درج متزامن");
        Assert.All(responses, r => Assert.True(r.IsSuccessStatusCode, $"{(int)r.StatusCode}"));
        using var scope = CreateScope();
        var db = CreateDbContext(scope);
        Assert.Equal(1, await db.DrawerOpenEvents.IgnoreQueryFilters().CountAsync(e => e.ClientRequestId == requestId));
    }

    [Fact]
    public async Task فواتير_شراء_متزامنة_بتكاليف_مختلفة_المتوسط_المرجّح_بيطلع_صحيح_والمخزون_مجموعها()
    {
        const int n = 8;
        var admin = await CreateAuthenticatedClientAsync();
        Guid productId, unitId, supplierId;
        using (var scope = CreateScope())
        {
            await TestDataBuilder.ActAsAdminAsync(scope, Fixture);
            var db = CreateDbContext(scope);
            await TestDataBuilder.EnsureDocumentSequencesProvisionedAsync(db, Fixture.TestBranchId);
            var (product, unit) = await TestDataBuilder.CreateActiveProductAsync(db, "صنف شراء متزامن");
            await TestDataBuilder.CreateProductBranchAsync(db, product.Id, Fixture.TestBranchId, sellingPrice: 5.000m);
            supplierId = (await TestDataBuilder.CreateSupplierAsync(db, "مورد السباق")).Id;
            (productId, unitId) = (product.Id, unit.Id);
        }

        Task<HttpResponseMessage> Purchase(int i) => admin.PostAsJsonAsync("/api/v1/purchase-invoices", new
        {
            branchId = Fixture.TestBranchId, supplierId, supplierInvoiceReference = $"R-{i}",
            items = new[] { new { productId, productUnitId = unitId, quantity = 10m, unitCost = 1.000m + 0.100m * i, existingProductBatchId = (Guid?)null, newBatchNumber = (string?)null, newBatchExpiryDate = (DateOnly?)null } }
        });

        // أول شراء لصنف جديد بينشئ صف Stock؛ لو اتسابقوا عليه الفهرس الفريد بيرفض (409 بالتصميم، راجع StockOperations) - فنبدأ بشراء وحيد ونسابق بعده.
        Assert.True((await Purchase(0)).IsSuccessStatusCode);
        // تعديل الرصيد المتتبَّع (Stock.Increase) محروس بـRowVersion: التعارض المتزامن = 409 "أعد المحاولة" (مقصود، ما بيضيّع شي)؛ العميل بيعيد.
        // نفس الفاتورة (نفس المرجع) لو اتكرر إرسالها بعد 409 ما بتنكرر لأنها اترفضت كاملة -> الثابت: بعد الإعادة، المخزون والتكلفة مضبوطين.
        var conflicts = 0;
        var responses = await RaceAsync(n - 1, async i =>
        {
            for (var attempt = 0; attempt < 40; attempt++)
            {
                var r = await Purchase(i + 1);
                if (r.StatusCode != HttpStatusCode.Conflict) return r;
                Interlocked.Increment(ref conflicts);
                await Task.Delay(20 * (attempt + 1) + Random.Shared.Next(30));
            }

            return new HttpResponseMessage(HttpStatusCode.RequestTimeout);
        });
        _output.WriteLine($"تعارضات 409 قبل نجاح الإعادة: {conflicts}");
        AssertNo5xx(responses, "شراء متزامن");
        Assert.True(responses.All(r => r.IsSuccessStatusCode), string.Join(" | ", responses.Where(r => !r.IsSuccessStatusCode).Select(r => $"{(int)r.StatusCode}")));
        Assert.Equal(n * 10m, await StockAsync(productId));

        // بيعة بعدهم: تكلفة الوحدة المجمَّدة = المتوسط المرجّح لكل المشتريات (الكميات متساوية -> متوسط التكاليف 1.350) بغض النظر عن ترتيب وصولها.
        var (_, name) = await UsersTestDataHelper.CreateUserWithRoleReturningUsernameAsync(Fixture.Factory.Services, Fixture.TestBranchId, UsersTestDataHelper.CashierRoleId, "race.cost");
        var cashier = await LoginHelper.LoginAsAsync(Fixture, name, UsersTestDataHelper.DefaultPassword, "Cashier");
        var sale = await cashier.PostAsJsonAsync("/api/v1/sales", SaleBody(productId, unitId, 1m, 5.000m));
        Assert.True(sale.IsSuccessStatusCode, await sale.Content.ReadAsStringAsync());
        using var check = CreateScope();
        var cdb = CreateDbContext(check);
        var cost = await cdb.SaleInvoiceItems.IgnoreQueryFilters().Where(i => i.ProductId == productId).Select(i => i.UnitCostSnapshot).SingleAsync();
        Assert.NotNull(cost);
        Assert.InRange(cost!.Value, 1.349m, 1.351m);
        await AssertInvariantsAsync(productId, 0m);
    }

    [Fact]
    public async Task أول_شراء_لصنف_جديد_متزامن_بيرفض_الزايد_بـ409_بلا_صف_مخزون_مكرر_ولا_5xx()
    {
        var admin = await CreateAuthenticatedClientAsync();
        Guid productId, unitId, supplierId;
        using (var scope = CreateScope())
        {
            await TestDataBuilder.ActAsAdminAsync(scope, Fixture);
            var db = CreateDbContext(scope);
            await TestDataBuilder.EnsureDocumentSequencesProvisionedAsync(db, Fixture.TestBranchId);
            var (product, unit) = await TestDataBuilder.CreateActiveProductAsync(db, "صنف أول شراء");
            await TestDataBuilder.CreateProductBranchAsync(db, product.Id, Fixture.TestBranchId, sellingPrice: 5.000m);
            supplierId = (await TestDataBuilder.CreateSupplierAsync(db, "مورد أول شراء")).Id;
            (productId, unitId) = (product.Id, unit.Id);
        }

        var responses = await RaceAsync(6, i => admin.PostAsJsonAsync("/api/v1/purchase-invoices", new
        {
            branchId = Fixture.TestBranchId, supplierId, supplierInvoiceReference = $"F-{i}",
            items = new[] { new { productId, productUnitId = unitId, quantity = 10m, unitCost = 1.000m, existingProductBatchId = (Guid?)null, newBatchNumber = (string?)null, newBatchExpiryDate = (DateOnly?)null } }
        }));
        AssertNo5xx(responses, "أول شراء متزامن");
        var ok = responses.Count(r => r.IsSuccessStatusCode);
        Assert.True(ok >= 1);
        Assert.All(responses.Where(r => !r.IsSuccessStatusCode), r => Assert.Equal(HttpStatusCode.Conflict, r.StatusCode));

        using var check = CreateScope();
        var cdb = CreateDbContext(check);
        Assert.Equal(1, await cdb.Stocks.IgnoreQueryFilters().CountAsync(s => s.ProductId == productId && s.BranchId == Fixture.TestBranchId));
        Assert.Equal(ok * 10m, await StockAsync(productId)); // المخزون = مجموع الفواتير الناجحة بالضبط، ولا فاتورة مرفوضة سابت أثر
        Assert.Equal(ok, await cdb.PurchaseInvoices.IgnoreQueryFilters().CountAsync(p => p.SupplierId == supplierId));
        await AssertInvariantsAsync(productId, 0m);
    }

    /// <summary>
    /// deadlock حقيقي انمسك بعاصفة 8 كاشيرات (22/10/2026): بيعة (س ثم ص) وبيعة (ص ثم س) بنفس اللحظة كانتا تقفلان صفّي المخزون بترتيب معاكس
    /// فيتقفلوا ببعض، والضحية بتنهار بـ500 بعد 6 إعادات أوتوماتيكية. الإصلاح: ترتيب قفل ثابت (صنف ← دفعة) بالبيع والإلغاء والإرجاع.
    /// </summary>
    [Fact]
    public async Task بيوع_متزامنة_بسلات_أصنافها_بترتيب_معاكس_ما_بتتقفل_ببعض_deadlock()
    {
        const int n = 40;
        var (cashier, p1, u1, _) = await SetUpAsync(stock: 500m);
        Guid p2, u2;
        using (var scope = CreateScope())
        {
            var db = CreateDbContext(scope);
            var (product, unit) = await TestDataBuilder.CreateActiveProductAsync(db, "صنف السباق الثاني");
            await TestDataBuilder.CreateProductBranchAsync(db, product.Id, Fixture.TestBranchId, sellingPrice: Price);
            await TestDataBuilder.SetStockAsync(db, product.Id, Fixture.TestBranchId, 500m);
            (p2, u2) = (product.Id, unit.Id);
        }

        object Basket(bool reversed) => new
        {
            branchId = Fixture.TestBranchId, clientRequestId = Guid.NewGuid(), customerId = (Guid?)null, allowCreditSale = false, invoiceLevelDiscountAmount = 0m,
            items = (reversed ? new[] { (p2, u2), (p1, u1) } : new[] { (p1, u1), (p2, u2) })
                .Select(x => new { productId = x.Item1, productUnitId = x.Item2, quantity = 1m, manualDiscountAmount = 0m, productBatchId = (Guid?)null, catalogVersion = (long?)null, promotionId = (Guid?)null }).ToArray(),
            payments = new[] { new { paymentMethodId = TestDataBuilder.CashPaymentMethodId, amount = Price * 2, externalReference = (string?)null, clientRequestId = Guid.NewGuid() } },
            preparedOrderId = (Guid?)null, customerPhone = (string?)null
        };

        var watch = System.Diagnostics.Stopwatch.StartNew();
        var responses = await RaceAsync(n, i => cashier.PostAsJsonAsync("/api/v1/sales", Basket(i % 2 == 1)));
        watch.Stop();
        _output.WriteLine($"زمن {n} بيعة متزامنة بترتيب معاكس: {watch.Elapsed.TotalSeconds:0.0}s");
        // بلا ترتيب القفل كان بياخد 31-34 ثانية (deadlock + إعادات أوتوماتيكية بتغطي الفشل)؛ معه 3-4 ثواني. الحد بيحرس الإصلاح حتى لو نجحت الإعادات.
        Assert.True(watch.Elapsed < TimeSpan.FromSeconds(20), $"البيوع المتزامنة بطيئة ({watch.Elapsed.TotalSeconds:0.0}s) - deadlock على المخزون؟");
        AssertNo5xx(responses, "بيوع بترتيب أصناف معاكس");
        Assert.All(responses, r => Assert.True(r.IsSuccessStatusCode, $"{(int)r.StatusCode}"));
        Assert.Equal(500m - n, await StockAsync(p1));
        Assert.Equal(500m - n, await StockAsync(p2));
    }
}
