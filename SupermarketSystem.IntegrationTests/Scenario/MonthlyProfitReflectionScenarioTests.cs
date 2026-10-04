using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using Microsoft.Data.SqlClient;
using Microsoft.EntityFrameworkCore;
using SupermarketSystem.IntegrationTests.Common;
using Xunit;
using Xunit.Abstractions;

namespace SupermarketSystem.IntegrationTests.Scenario;

/// <summary>
/// "هل كل شي بيعكس صح؟" (طلب صاحب المشروع 29/9/2026): شهر كامل بالمحل عبر HTTP حقيقي بأرقام محسوبة مسبقًا
/// باليد، وكل تقرير لازم يطلع نفس الرقم - المبيعات، هامش كل منتج، كشف الربح الشهري، المخزون، قيمة رأس المال،
/// ديون الزبائن والموردين، تقفيل الصندوق، وتوزيع الربح على الشركاء.
///
/// الأصناف (السعر بالحبة):
///   أرز  1.500 (حبة + كرتونة ×10) - شراء 4 كراتين × 8.000 (= 0.800 للحبة) + 10 حبات × 1.100  → تكلفة الحبة 43/50 = 0.860
///   حليب 1.250 - شراء 30 × 0.800 - عرض "3 بـ3.000"
///   لبن  2.000 (دفعات) - دفعة L-1: 20 × 1.200
///   خبز  0.500 - شراء 20 × 0.300 - للتلف والضيافة والجرد
///   شيبس 0.250 - رصيد 10 بلا أي فاتورة شراء (تكلفة غير معروفة)
///
/// المبيعات (غير الملغاة):
///   S1 أرز 5 حبات + كرتونة = 22.500 كاش (تكلفة 15 × 0.860 = 12.900)
///   S2 حليب 3 بالعرض = 3.000 فيزا (2.400)       S3 لبن 4 من L-1 = 8.000 كاش (4.800)
///   S4 شيبس 4 = 1.000 كاش (بلا تكلفة)           S5 حليب 2 بالدين = 2.500، دفع 1.000 + لاحقًا 0.500 (1.600)
///   S6 خبز 2 = 1.000 كاش ← ملغاة                S7 أرز 2 = 3.000 كاش، إرجاع حبة 1.500 (تكلفة صافية 0.860)
///   AC1 سحب بالتكلفة: حليب 1 = 0.800 "اخصمها مني"   AC2 خبز 1 = 0.300 "بدفع حقها" كاش
///   → المبيعات 41.100، المرتجع 1.500، الصافي 39.600، التكلفة 23.660، الإجمالي 15.940
/// تلف خبز 2 (0.600 خسارة) + خبز 1 مستبدَل (بلا خسارة)، ضيافة خبز 1 (0.300 تكلفة - صارت تنحسب 29/9/2026)،
/// جرد: حليب ناقص 1 (0.800)، خبز زايد 1 (0.300)، مصاريف 5.000 + 1.200
///   → صافي الربح = 15.940 − 6.200 + 0.300 − 0.800 − 0.600 − 0.300 = 8.340
/// </summary>
[Collection(DatabaseCollection.Name)]
public sealed class MonthlyProfitReflectionScenarioTests : IntegrationTestBase
{
    private readonly ITestOutputHelper _output;
    private readonly List<string> _failures = new();
    private int _checks;

    public MonthlyProfitReflectionScenarioTests(DatabaseFixture fixture, ITestOutputHelper output) : base(fixture)
    {
        _output = output;
    }

    // ================= أدوات =================

    private void Check(string what, decimal expected, decimal actual)
    {
        _checks++;
        var ok = expected == actual;
        _output.WriteLine($"{(ok ? "✔" : "✘")} {what}: متوقع {expected:0.000#} ← فعلي {actual:0.000#}");
        if (!ok) _failures.Add($"{what}: متوقع {expected:0.000#}، فعلي {actual:0.000#}");
    }

    private void CheckTrue(string what, bool ok, string? detail = null)
    {
        _checks++;
        _output.WriteLine($"{(ok ? "✔" : "✘")} {what}{(detail is null ? "" : $" ({detail})")}");
        if (!ok) _failures.Add($"{what}{(detail is null ? "" : $" ({detail})")}");
    }

    private static async Task<JsonElement> OkAsync(HttpResponseMessage response, string what)
    {
        var body = await response.Content.ReadAsStringAsync();
        Assert.True(response.IsSuccessStatusCode, $"{what}: {(int)response.StatusCode} {body}");
        return string.IsNullOrWhiteSpace(body) ? default : JsonDocument.Parse(body).RootElement;
    }

    private static Task<JsonElement> PostAsync(HttpClient client, string url, object body, string what) =>
        client.PostAsJsonAsync(url, body).ContinueWith(t => OkAsync(t.Result, what)).Unwrap();

    private static async Task<JsonElement> GetAsync(HttpClient client, string url, string what) =>
        await OkAsync(await client.GetAsync(url), what);

    private static string Iso(DateTime value) => Uri.EscapeDataString(value.ToString("o"));

    private static decimal D(JsonElement e, string name) => e.GetProperty(name).GetDecimal();

    private sealed record Product(Guid Id, Guid BaseUnit, Guid? Carton);

    private async Task<Product> CreateProductAsync(HttpClient admin, Guid categoryId, string name, decimal price, bool batchTracked = false, bool withCarton = false)
    {
        var units = new List<object> { new { unitName = "حبة", conversionFactorToBase = 1m, isBaseUnit = true } };
        if (withCarton) units.Add(new { unitName = "كرتونة", conversionFactorToBase = 10m, isBaseUnit = false });
        var created = await PostAsync(admin, "/api/v1/products", new
        {
            name, description = (string?)null, categoryId, isBatchTracked = batchTracked,
            suggestedRetailPrice = (decimal?)null, expectedShelfLifeDays = (int?)null,
            units, barcodes = Array.Empty<object>()
        }, $"منتج {name}");
        var id = created.GetProperty("productId").GetGuid();
        await PostAsync(admin, $"/api/v1/products/{id}/branches",
            new { branchId = Fixture.TestBranchId, sellingPrice = price, minimumStock = 0m, maximumStock = (decimal?)null }, $"ربط {name}");
        var list = (await GetAsync(admin, $"/api/v1/products/{id}/units", "وحدات")).EnumerateArray().ToList();
        var baseUnit = list.Single(u => u.GetProperty("isBaseUnit").GetBoolean()).GetProperty("id").GetGuid();
        Guid? carton = withCarton ? list.Single(u => !u.GetProperty("isBaseUnit").GetBoolean()).GetProperty("id").GetGuid() : null;
        return new Product(id, baseUnit, carton);
    }

    private static object Line(Guid productId, Guid unitId, decimal quantity, Guid? batchId = null) =>
        new { productId, productUnitId = unitId, quantity, manualDiscountAmount = 0m, productBatchId = batchId };

    private static object Pay(Guid methodId, decimal amount, string? reference = null) =>
        new { paymentMethodId = methodId, amount, externalReference = reference, clientRequestId = Guid.NewGuid() };

    private Task<JsonElement> SellAsync(HttpClient client, string what, object[] items, object[] payments, Guid? customerId = null, bool allowCredit = false) =>
        PostAsync(client, "/api/v1/sales", new
        {
            branchId = Fixture.TestBranchId, clientRequestId = Guid.NewGuid(), customerId, invoiceLevelDiscountAmount = 0m,
            items, payments, allowCreditSale = allowCredit
        }, what);

    // ================= السيناريو =================

    [Fact]
    public async Task شهر_كامل_بالمحل_كل_تقرير_بيعكس_نفس_الأرقام_والربح_بيتوزع_صح_عالشركاء()
    {
        var branchId = Fixture.TestBranchId;
        var cash = TestDataBuilder.CashPaymentMethodId;
        var visa = TestDataBuilder.VisaPaymentMethodId;
        using (var scope = CreateScope())
        {
            await TestDataBuilder.EnsureDocumentSequencesProvisionedAsync(CreateDbContext(scope), branchId);
        }

        var admin = await CreateAuthenticatedClientAsync();
        var (cashierId, cashierName) = await UsersTestDataHelper.CreateUserWithRoleReturningUsernameAsync(
            Fixture.Factory.Services, branchId, UsersTestDataHelper.CashierRoleId, "month.cashier");
        var cashier = await LoginHelper.LoginAsAsync(Fixture, cashierName, UsersTestDataHelper.DefaultPassword, appType: "Cashier");

        // ---------- الكتالوج ----------
        var categoryId = (await PostAsync(admin, "/api/v1/product-categories", new { name = "بقالة الشهر", parentCategoryId = (Guid?)null }, "تصنيف"))
            .GetProperty("categoryId").GetGuid();
        var rice = await CreateProductAsync(admin, categoryId, "أرز الشهر", 1.500m, withCarton: true);
        var milk = await CreateProductAsync(admin, categoryId, "حليب الشهر", 1.250m);
        var laban = await CreateProductAsync(admin, categoryId, "لبن الشهر", 2.000m, batchTracked: true);
        var bread = await CreateProductAsync(admin, categoryId, "خبز الشهر", 0.500m);
        var chips = await CreateProductAsync(admin, categoryId, "شيبس الشهر", 0.250m);
        using (var scope = CreateScope())
        {
            var db = CreateDbContext(scope);
            await db.Products.IgnoreQueryFilters().Where(p => p.Id == bread.Id)
                .ExecuteUpdateAsync(s => s.SetProperty(p => p.IsComplimentaryAllowed, true));
            // شيبس: رصيد بلا فاتورة شراء (بضاعة قديمة مثلًا) - تكلفته غير معروفة. (مش بيع بالسالب: إعداد
            // AllowNegativeStock ممكن يكون مطفي من اختبار تاني، والإعدادات مش بتتصفّر بين الاختبارات.)
            await TestDataBuilder.SetStockAsync(db, chips.Id, branchId, 10m);
        }

        await PostAsync(admin, $"/api/v1/products/{milk.Id}/promotions", new
        {
            title = "3 حليب بـ3", bundleQuantity = 3, bundlePrice = 3.000m, maxQuantityPerInvoice = (decimal?)null,
            startAtUtc = DateTime.UtcNow.AddHours(-1), endAtUtc = DateTime.UtcNow.AddDays(60), branchIds = (Guid[]?)null
        }, "عرض الحليب");

        // ---------- المشتريات ----------
        object Supplier(string name) => new
        {
            name, contactName = (string?)null, phone = "0790000001", email = (string?)null,
            street = (string?)null, city = "عمّان", postalCode = (string?)null, country = "الأردن"
        };
        var supplierA = (await PostAsync(admin, "/api/v1/suppliers", Supplier("مورد الشهر أ"), "مورد أ")).GetProperty("supplierId").GetGuid();
        var supplierB = (await PostAsync(admin, "/api/v1/suppliers", Supplier("مورد الشهر ب"), "مورد ب")).GetProperty("supplierId").GetGuid();
        object PItem(Guid productId, Guid unitId, decimal qty, decimal cost, string? batch = null) => new
        {
            productId, productUnitId = unitId, quantity = qty, unitCost = cost, existingProductBatchId = (Guid?)null,
            newBatchNumber = batch, newBatchExpiryDate = batch is null ? (DateOnly?)null : DateOnly.FromDateTime(DateTime.UtcNow.AddDays(60))
        };
        var purchaseA = await PostAsync(admin, "/api/v1/purchase-invoices", new
        {
            branchId, supplierId = supplierA, supplierInvoiceReference = "A-1",
            items = new[] { PItem(rice.Id, rice.Carton!.Value, 4m, 8.000m), PItem(milk.Id, milk.BaseUnit, 30m, 0.800m), PItem(bread.Id, bread.BaseUnit, 20m, 0.300m) },
            imageReferences = (string[]?)null, dueDate = (DateOnly?)null
        }, "شراء أ");
        await PostAsync(admin, "/api/v1/purchase-invoices", new
        {
            branchId, supplierId = supplierB, supplierInvoiceReference = "B-1",
            items = new[] { PItem(rice.Id, rice.BaseUnit, 10m, 1.100m), PItem(laban.Id, laban.BaseUnit, 20m, 1.200m, "L-1") },
            imageReferences = (string[]?)null, dueDate = (DateOnly?)null
        }, "شراء ب");
        await PostAsync(admin, $"/api/v1/purchase-invoices/{purchaseA.GetProperty("purchaseInvoiceId").GetGuid()}/payments", new
        {
            paymentMethodId = cash, amount = 20.000m, externalReference = (string?)null, clientRequestId = Guid.NewGuid()
        }, "دفعة للمورد أ (كاش من الدرج)");

        Guid labanBatch;
        using (var scope = CreateScope())
        {
            labanBatch = await CreateDbContext(scope).ProductBatches.IgnoreQueryFilters()
                .Where(b => b.ProductId == laban.Id).Select(b => b.Id).SingleAsync();
        }

        // ---------- المبيعات ----------
        var s1 = await SellAsync(cashier, "S1 أرز حبات + كرتونة",
            new[] { Line(rice.Id, rice.BaseUnit, 5m), Line(rice.Id, rice.Carton.Value, 1m) }, new[] { Pay(cash, 22.500m) });
        await SellAsync(cashier, "S2 حليب بالعرض", new[] { Line(milk.Id, milk.BaseUnit, 3m) }, new[] { Pay(visa, 3.000m, "V-1") });
        await SellAsync(cashier, "S3 لبن من دفعة", new[] { Line(laban.Id, laban.BaseUnit, 4m, labanBatch) }, new[] { Pay(cash, 8.000m) });
        await SellAsync(cashier, "S4 شيبس بلا تكلفة", new[] { Line(chips.Id, chips.BaseUnit, 4m) }, new[] { Pay(cash, 1.000m) });

        Guid customerId;
        using (var scope = CreateScope())
        {
            var db = CreateDbContext(scope);
            var customer = new SupermarketSystem.Domain.Customers.Customer("زبون الدفتر", "0791112233", null);
            db.Customers.Add(customer);
            await db.SaveChangesAsync();
            customerId = customer.Id;
        }
        var s5 = await SellAsync(cashier, "S5 حليب بالدين", new[] { Line(milk.Id, milk.BaseUnit, 2m) }, new[] { Pay(cash, 1.000m) }, customerId, allowCredit: true);
        await PostAsync(cashier, $"/api/v1/sales/{s5.GetProperty("saleInvoiceId").GetGuid()}/payments", new
        {
            paymentMethodId = cash, amount = 0.500m, externalReference = (string?)null, clientRequestId = Guid.NewGuid()
        }, "S5 تسديد لاحق");

        var s6 = await SellAsync(cashier, "S6 خبز (رح تنلغى)", new[] { Line(bread.Id, bread.BaseUnit, 2m) }, new[] { Pay(cash, 1.000m) });
        await PostAsync(cashier, $"/api/v1/sales/{s6.GetProperty("saleInvoiceId").GetGuid()}/void", new { reason = "CashierError", notes = "غلط" }, "إلغاء S6");

        var s7 = await SellAsync(cashier, "S7 أرز 2", new[] { Line(rice.Id, rice.BaseUnit, 2m) }, new[] { Pay(cash, 3.000m) });
        var s7Id = s7.GetProperty("saleInvoiceId").GetGuid();
        var s7Detail = await GetAsync(cashier, $"/api/v1/sales/{s7Id}", "تفاصيل S7");
        await PostAsync(cashier, "/api/v1/returns", new
        {
            originalSaleInvoiceId = s7Id, clientRequestId = Guid.NewGuid(), reason = "Defective", notes = (string?)null,
            items = new[] { new { saleInvoiceItemId = s7Detail.GetProperty("items")[0].GetProperty("saleInvoiceItemId").GetGuid(), quantity = 1m } },
            refunds = new[] { Pay(cash, 1.500m) }
        }, "إرجاع حبة من S7");

        // سحب بالتكلفة (حساب الأدمن = الشريك أحمد لاحقًا)
        var ac1 = await PostAsync(admin, "/api/v1/sales/at-cost", new
        {
            branchId, clientRequestId = Guid.NewGuid(), items = new[] { new { productId = milk.Id, productUnitId = milk.BaseUnit, quantity = 1m, productBatchId = (Guid?)null } },
            deductFromShare = true, paymentMethodId = (Guid?)null
        }, "AC1 حليب بالتكلفة - اخصمها مني");
        Check("AC1 سعر الحليب بالتكلفة", 0.800m, D(ac1, "totalAmount"));
        var ac2 = await PostAsync(admin, "/api/v1/sales/at-cost", new
        {
            branchId, clientRequestId = Guid.NewGuid(), items = new[] { new { productId = bread.Id, productUnitId = bread.BaseUnit, quantity = 1m, productBatchId = (Guid?)null } },
            deductFromShare = false, paymentMethodId = cash
        }, "AC2 خبز بالتكلفة - بدفع حقها");
        Check("AC2 سعر الخبز بالتكلفة", 0.300m, D(ac2, "totalAmount"));

        // ---------- تلف، ضيافة، مصاريف ----------
        object Issue(Guid productId, Guid unitId, decimal qty, string reason, bool replaced) => new
        {
            productId, productUnitId = unitId, branchId, quantity = qty, reason, notes = (string?)null, isReplacedBySupplier = replaced
        };
        await PostAsync(admin, "/api/v1/inventory/waste-issues", Issue(bread.Id, bread.BaseUnit, 2m, "Broken", false), "تلف خبز 2");
        await PostAsync(admin, "/api/v1/inventory/waste-issues", Issue(bread.Id, bread.BaseUnit, 1m, "Expired", true), "تلف خبز 1 مستبدَل");
        await PostAsync(admin, "/api/v1/inventory/complimentary-issues",
            new { productId = bread.Id, productUnitId = bread.BaseUnit, branchId, quantity = 1m, reason = "ضيافة" }, "ضيافة خبز 1");

        var now = DateTime.UtcNow;
        async Task ExpenseAsync(int category, decimal amount, DateTime period, string note) =>
            await PostAsync(admin, "/api/v1/finance/expenses", new
            {
                branchId, category, amount, paymentDateUtc = period.ToString("yyyy-MM-dd"), periodYear = period.Year, periodMonth = period.Month, notes = note
            }, note);
        await ExpenseAsync(1, 5.000m, now, "إيجار");
        await ExpenseAsync(2, 1.200m, now, "كهربا");

        // ---------- المخزون قبل الجرد ----------
        async Task<decimal> StockAsync(Guid productId)
        {
            var page = await GetAsync(admin, $"/api/v1/inventory/current-stock?pageSize=200&branchId={branchId}", "المخزون الحالي");
            return page.GetProperty("items").EnumerateArray()
                .Where(i => i.GetProperty("productId").GetGuid() == productId).Sum(i => D(i, "quantityOnHand"));
        }
        Check("مخزون الأرز (50 − 15 − 2 + 1 مرتجع)", 34m, await StockAsync(rice.Id));
        Check("مخزون الحليب (30 − 3 − 2 − 1)", 24m, await StockAsync(milk.Id));
        Check("مخزون اللبن (20 − 4)", 16m, await StockAsync(laban.Id));
        Check("مخزون الخبز (20، الملغاة رجعت، − 2 − 1 تلف − 1 ضيافة − 1 بالتكلفة)", 15m, await StockAsync(bread.Id));
        Check("مخزون الشيبس (10 بلا فاتورة − 4)", 6m, await StockAsync(chips.Id));

        // ---------- جرد جزئي: حليب ناقص 1، خبز زايد 1 ----------
        var stocktake = await PostAsync(admin, "/api/v1/stocktakes",
            new { branchId, includeAllProductsAtBranch = false, productIds = new[] { milk.Id, bread.Id } }, "جرد جزئي");
        var stocktakeId = stocktake.GetProperty("stocktakeId").GetGuid();
        foreach (var item in (await GetAsync(admin, $"/api/v1/stocktakes/{stocktakeId}", "تفاصيل الجرد")).GetProperty("items").EnumerateArray())
        {
            var productId = item.GetProperty("productId").GetGuid();
            var expected = D(item, "expectedQuantity");
            var counted = productId == milk.Id ? expected - 1m : expected + 1m;
            await PostAsync(admin, $"/api/v1/stocktakes/{stocktakeId}/items/{item.GetProperty("stocktakeItemId").GetGuid()}/count",
                new { countedQuantity = counted }, "عد");
        }
        await PostAsync(admin, $"/api/v1/stocktakes/{stocktakeId}/complete", new { }, "إنهاء الجرد");
        await PostAsync(admin, $"/api/v1/stocktakes/{stocktakeId}/approve", new { }, "اعتماد الجرد");
        Check("مخزون الحليب بعد الجرد", 23m, await StockAsync(milk.Id));
        Check("مخزون الخبز بعد الجرد", 16m, await StockAsync(bread.Id));

        // ================= التقارير (الشهر الحالي) =================
        await VerifyMonthAsync(admin, now.Year, now.Month, "الشهر الحالي");

        var range = $"branchId={branchId}&fromUtc={Iso(now.AddHours(-2))}&toUtc={Iso(now.AddHours(2))}";
        var summary = (await GetAsync(admin, $"/api/v1/reports/sales/summary?{range}", "ملخّص المبيعات")).GetProperty("period");
        Check("ملخّص المبيعات: عدد الفواتير (بلا الملغاة)", 8m, summary.GetProperty("invoiceCount").GetInt32());
        Check("ملخّص المبيعات: الإجمالي", 41.100m, D(summary, "totalSales"));
        Check("ملخّص المبيعات: المرتجع", 1.500m, D(summary, "totalReturnedAmount"));
        Check("ملخّص المبيعات: الصافي = صافي كشف الربح", 39.600m, D(summary, "netRevenue"));
        Check("ملخّص المبيعات: الخصومات اليدوية", 0m, D(summary, "totalDiscounts"));
        Check("ملخّص المبيعات: توفير العروض (3 حليب: 3.750 − 3.000)", 0.750m, D(summary, "totalPromotionDiscounts"));

        var margin = await GetAsync(admin, $"/api/v1/reports/product-margin?pageSize=50&{range}", "هامش كل منتج");
        Check("هامش المنتجات: إجمالي الإيراد", 39.600m, D(margin, "totalNetRevenue"));
        Check("هامش المنتجات: إجمالي التكلفة", 23.660m, D(margin, "totalCost"));
        Check("هامش المنتجات: إجمالي الهامش = إجمالي كشف الربح", 15.940m, D(margin, "totalMargin"));
        var marginRows = margin.GetProperty("items").GetProperty("items").EnumerateArray().ToDictionary(r => r.GetProperty("productId").GetGuid());
        void MarginRow(string name, Product p, decimal qty, decimal revenue, decimal cost, int excluded = 0)
        {
            var row = marginRows[p.Id];
            Check($"هامش {name}: الكمية المباعة (بالحبة)", qty, D(row, "quantitySold"));
            Check($"هامش {name}: الإيراد", revenue, D(row, "netRevenue"));
            Check($"هامش {name}: التكلفة", cost, D(row, "cost"));
            Check($"هامش {name}: الهامش", revenue - cost, D(row, "margin"));
            Check($"هامش {name}: أسطر بلا تكلفة", excluded, row.GetProperty("linesExcludedNoCostHistory").GetInt32());
        }
        MarginRow("الأرز", rice, 16m, 24.000m, 13.760m);
        MarginRow("الحليب", milk, 6m, 6.300m, 4.800m);
        MarginRow("اللبن", laban, 4m, 8.000m, 4.800m);
        MarginRow("الخبز", bread, 1m, 0.300m, 0.300m);
        MarginRow("الشيبس", chips, 4m, 1.000m, 0m, excluded: 1);

        var capital = await GetAsync(admin, $"/api/v1/reports/inventory/capital-value?pageSize=50&branchId={branchId}", "قيمة رأس المال بالمخزون");
        var capitalRows = capital.GetProperty("items").GetProperty("items").EnumerateArray().ToDictionary(r => r.GetProperty("productId").GetGuid());
        Check("رأس المال: متوسط تكلفة حبة الأرز", 0.860m, Math.Round(D(capitalRows[rice.Id], "weightedAverageCost"), 3));
        Check("رأس المال: قيمة الأرز (34 × 0.860)", 29.240m, Math.Round(D(capitalRows[rice.Id], "totalValue"), 3));
        Check("رأس المال: قيمة الحليب (23 × 0.800)", 18.400m, Math.Round(D(capitalRows[milk.Id], "totalValue"), 3));
        Check("رأس المال: قيمة اللبن (16 × 1.200)", 19.200m, Math.Round(D(capitalRows[laban.Id], "totalValue"), 3));
        Check("رأس المال: قيمة الخبز (16 × 0.300)", 4.800m, Math.Round(D(capitalRows[bread.Id], "totalValue"), 3));
        Check("رأس المال: الإجمالي", 71.640m, Math.Round(D(capital, "totalCapitalValue"), 3));

        var customerDebts = await GetAsync(admin, "/api/v1/sales/customer-debts", "ديون الزبائن");
        var debtor = customerDebts.GetProperty("customers").EnumerateArray().Single(c => c.GetProperty("customerId").GetGuid() == customerId);
        Check("دين زبون الدفتر (2.500 − 1.000 − 0.500)", 1.000m, D(debtor, "remainingDebt"));

        var supplierDebts = await GetAsync(admin, "/api/v1/purchase-invoices/supplier-debts", "ديون الموردين");
        var suppliers = supplierDebts.GetProperty("suppliers").EnumerateArray().ToDictionary(s => s.GetProperty("supplierId").GetGuid());
        Check("دين المورد أ (62 − 20)", 42.000m, D(suppliers[supplierA], "remainingDebt"));
        Check("دين المورد ب", 35.000m, D(suppliers[supplierB], "remainingDebt"));

        var pending = (await GetAsync(admin, $"/api/v1/stocktakes/returned-pending?branchId={branchId}", "مرتجعات بانتظار جرد")).EnumerateArray().ToList();
        CheckTrue("الأرز المرتجع بقائمة بانتظار جرد", pending.Any(p => p.GetProperty("productId").GetGuid() == rice.Id));

        // ---------- تقفيل الصندوق ----------
        // كاش: 22.5 + 8 + 1 + 1 + 0.5 + (1 − 1 ملغاة) + 3 − 1.5 إرجاع + 0.3 − 20 للمورد = 14.800
        var closing = await PostAsync(admin, "/api/v1/cash-closings", new
        {
            branchId, businessDate = DateOnly.FromDateTime(now), countedCash = 14.500m, countedDetails = Array.Empty<object>()
        }, "تقفيل 1");
        Check("التقفيل: الكاش المتوقع", 14.800m, D(closing, "expectedCash"));
        Check("التقفيل: الفرق (عجز)", -0.300m, D(closing, "variance"));
        var visaDetail = closing.GetProperty("details").EnumerateArray().FirstOrDefault(d => d.GetProperty("paymentMethodId").GetGuid() == visa);
        Check("التقفيل: الفيزا المتوقعة", 3.000m, visaDetail.ValueKind == JsonValueKind.Undefined ? 0m : D(visaDetail, "expectedAmount"));

        // ================= نقل الشهر للماضي (الترتيب محفوظ، فالتكلفة ما بتتغيّر) =================
        await ShiftBusinessDataOneMonthBackAsync();
        var previous = new DateTime(now.Year, now.Month, 1, 0, 0, 0, DateTimeKind.Utc).AddMonths(-1);
        await VerifyMonthAsync(admin, previous.Year, previous.Month, "الشهر الماضي (بعد النقل)");
        var emptyNow = await GetAsync(admin, $"/api/v1/finance/profit-statement?branchId={branchId}&year={now.Year}&month={now.Month}", "كشف الشهر الحالي الفاضي");
        Check("الشهر الحالي صار فاضي بعد النقل", 0m, D(emptyNow, "netProfit"));

        // ================= الشركاء =================
        var (samiUserId, _) = await UsersTestDataHelper.CreateUserWithRoleReturningUsernameAsync(
            Fixture.Factory.Services, branchId, UsersTestDataHelper.CashierRoleId, "month.sami");
        async Task<Guid> PartnerAsync(string name, string type, Guid? userId, decimal? percent) =>
            (await PostAsync(admin, "/api/v1/partners", new { branchId, fullName = name, type, userId, speculativeProfitPercent = percent, notes = (string?)null }, $"شريك {name}"))
            .GetProperty("partnerId").GetGuid();
        var ahmad = await PartnerAsync("أحمد", "Capital", Fixture.AdminUserId, null);
        var sami = await PartnerAsync("سامي", "Capital", samiUserId, null);
        var mudarib = await PartnerAsync("خالد المضارب", "Speculative", null, 25m);
        async Task CapitalAsync(Guid partnerId, decimal amount) => await PostAsync(admin, "/api/v1/finance/capital-transactions", new
        {
            branchId, type = "Deposit", amount, occurredAtUtc = previous.AddDays(2), notes = "رأس مال", partnerId
        }, "رأس مال");
        await CapitalAsync(ahmad, 3000m);
        await CapitalAsync(sami, 1000m);

        var statement = await PostAsync(admin, "/api/v1/partners/statements", new { branchId, year = previous.Year, month = previous.Month }, "كشف الشركاء");
        Check("كشف الشركاء: صافي الربح = كشف الربح الشهري", 8.340m, D(statement, "netProfit"));
        var lines = statement.GetProperty("lines").EnumerateArray().ToDictionary(l => l.GetProperty("partnerId").GetGuid());
        Check("المضارب 25% من 8.340", 2.085m, D(lines[mudarib], "shareAmount"));
        Check("أحمد 75% من الباقي 6.255 (4.69125)", 4.691m, D(lines[ahmad], "shareAmount"));
        Check("سامي 25% من الباقي (1.56375)", 1.564m, D(lines[sami], "shareAmount"));
        Check("مجموع الأنصبة = صافي الربح", 8.340m, lines.Values.Sum(l => D(l, "shareAmount")) + D(statement, "unallocatedAmount"));

        async Task<Dictionary<Guid, JsonElement>> PartnersAsync() =>
            (await GetAsync(admin, $"/api/v1/partners?branchId={branchId}", "الشركاء")).EnumerateArray().ToDictionary(p => p.GetProperty("id").GetGuid());
        Check("رصيد أحمد = نصيبه − الحليب اللي أخده بالتكلفة", 3.891m, D((await PartnersAsync())[ahmad], "currentBalance"));

        await PostAsync(admin, "/api/v1/partners/withdrawals", new
        {
            partnerId = ahmad, amount = 1.000m, source = "Drawer", notes = (string?)null, occurredAtUtc = (DateTime?)null, clientRequestId = Guid.NewGuid()
        }, "أحمد بيسحب 1 من الصندوق");
        await PostAsync(admin, "/api/v1/partners/withdrawals", new
        {
            partnerId = sami, amount = 2.000m, source = "OwnerPocket", notes = (string?)null, occurredAtUtc = (DateTime?)null, clientRequestId = Guid.NewGuid()
        }, "سامي بياخد 2 من جيب صاحب المحل");
        var partners = await PartnersAsync();
        Check("رصيد أحمد بعد السحب", 2.891m, D(partners[ahmad], "currentBalance"));
        Check("رصيد سامي (سلفة)", -0.436m, D(partners[sami], "currentBalance"));
        Check("رصيد المضارب", 2.085m, D(partners[mudarib], "currentBalance"));
        var receivables = (await GetAsync(admin, $"/api/v1/partners/owner-receivables?branchId={branchId}", "مستحق لصاحب المحل")).EnumerateArray().ToList();
        Check("المستحق لصاحب المحل (دفع لسامي من جيبه)", 2.000m,
            receivables.Where(r => r.GetProperty("ownerUserId").GetGuid() == Fixture.AdminUserId).Sum(r => D(r, "balance")));

        var currentStatement = await admin.PostAsJsonAsync("/api/v1/partners/statements", new { branchId, year = now.Year, month = now.Month });
        CheckTrue("كشف الشهر الحالي مرفوض لأنه ما خلص", currentStatement.StatusCode == HttpStatusCode.UnprocessableEntity, ((int)currentStatement.StatusCode).ToString());

        // ================= الشهر الحالي: بيعة جديدة + تقفيل تاني =================
        await SellAsync(cashier, "S8 حليب (الشهر الحالي)", new[] { Line(milk.Id, milk.BaseUnit, 1m) }, new[] { Pay(cash, 1.250m) });
        var current = await GetAsync(admin, $"/api/v1/finance/profit-statement?branchId={branchId}&year={now.Year}&month={now.Month}", "كشف الشهر الحالي");
        Check("الشهر الحالي: المبيعات", 1.250m, D(current, "totalSales"));
        Check("الشهر الحالي: التكلفة", 0.800m, D(current, "costOfGoodsSold"));
        Check("الشهر الحالي: صافي الربح (السحوبات مش مصروف)", 0.450m, D(current, "netProfit"));
        var previousAgain = await GetAsync(admin, $"/api/v1/finance/profit-statement?branchId={branchId}&year={previous.Year}&month={previous.Month}", "كشف الشهر الماضي");
        Check("الشهر الماضي ما تأثر بالبيعة الجديدة ولا بالسحوبات", 8.340m, D(previousAgain, "netProfit"));

        var closing2 = await PostAsync(admin, "/api/v1/cash-closings", new
        {
            branchId, businessDate = DateOnly.FromDateTime(DateTime.UtcNow), countedCash = 0.250m, countedDetails = Array.Empty<object>()
        }, "تقفيل 2");
        Check("التقفيل 2: المتوقع = 1.250 بيع − 1.000 سحب أحمد (سحب سامي من الجيب ما بيأثر)", 0.250m, D(closing2, "expectedCash"));
        Check("التقفيل 2: بلا فرق", 0m, D(closing2, "variance"));
        Check("مخزون الحليب بعد S8", 22m, await StockAsync(milk.Id));

        _output.WriteLine($"\n{_checks - _failures.Count}/{_checks} فحص صح");
        Assert.True(_failures.Count == 0, $"{_failures.Count} من {_checks} فحص غلط:\n" + string.Join("\n", _failures));
    }

    /// <summary>
    /// نفس جذر الخطأ من جهات تانية: دفعة مشتراة بالكرتونة، إنذار "سعر أعلى من المعتاد" الكاذب لما تشتري بالكرتونة
    /// بعد الحبة، مقارنة الموردين، والسحب بالتكلفة لمنتج انشرى بالكرتونة.
    /// </summary>
    [Fact]
    public async Task الشراء_بالكرتونة_بيحسب_تكلفة_الحبة_بالدفعة_والإنذار_والمقارنة_والسحب_بالتكلفة()
    {
        var branchId = Fixture.TestBranchId;
        using (var scope = CreateScope())
        {
            await TestDataBuilder.EnsureDocumentSequencesProvisionedAsync(CreateDbContext(scope), branchId);
        }

        var admin = await CreateAuthenticatedClientAsync();
        var categoryId = (await PostAsync(admin, "/api/v1/product-categories", new { name = "كراتين", parentCategoryId = (Guid?)null }, "تصنيف"))
            .GetProperty("categoryId").GetGuid();
        var juice = await CreateProductAsync(admin, categoryId, "عصير كراتين", 1.000m, batchTracked: true, withCarton: true);
        var water = await CreateProductAsync(admin, categoryId, "مي كراتين", 0.300m, withCarton: true);
        var supplier = (await PostAsync(admin, "/api/v1/suppliers", new
        {
            name = "مورد الكراتين", contactName = (string?)null, phone = "0790000002", email = (string?)null,
            street = (string?)null, city = (string?)null, postalCode = (string?)null, country = (string?)null
        }, "مورد")).GetProperty("supplierId").GetGuid();
        async Task PurchaseAsync(Guid productId, Guid unitId, decimal qty, decimal cost, string? batch = null) =>
            await PostAsync(admin, "/api/v1/purchase-invoices", new
            {
                branchId, supplierId = supplier, supplierInvoiceReference = (string?)null,
                items = new[] { new { productId, productUnitId = unitId, quantity = qty, unitCost = cost, existingProductBatchId = (Guid?)null,
                    newBatchNumber = batch, newBatchExpiryDate = batch is null ? (DateOnly?)null : DateOnly.FromDateTime(DateTime.UtcNow.AddDays(30)) } },
                imageReferences = (string[]?)null, dueDate = (DateOnly?)null
            }, "شراء");

        // دفعة عصير: كرتونتين × 6.000 (= 0.600 للحبة)
        await PurchaseAsync(juice.Id, juice.Carton!.Value, 2m, 6.000m, "J-1");
        decimal batchCost;
        Guid batchId;
        using (var scope = CreateScope())
        {
            var batch = await CreateDbContext(scope).ProductBatches.IgnoreQueryFilters().SingleAsync(b => b.ProductId == juice.Id);
            (batchCost, batchId) = (batch.UnitCost, batch.Id);
        }
        Check("تكلفة دفعة مشتراة بالكرتونة = تكلفة الحبة", 0.600m, batchCost);

        await SellAsync(admin, "بيع 3 عصير من الدفعة", new[] { Line(juice.Id, juice.BaseUnit, 3m, batchId) },
            new[] { Pay(TestDataBuilder.CashPaymentMethodId, 3.000m) });
        var now = DateTime.UtcNow;
        var margin = await GetAsync(admin, $"/api/v1/reports/product-margin?branchId={branchId}&fromUtc={Iso(now.AddHours(-1))}&toUtc={Iso(now.AddHours(1))}", "هامش");
        var juiceRow = margin.GetProperty("items").GetProperty("items").EnumerateArray().Single(r => r.GetProperty("productId").GetGuid() == juice.Id);
        Check("تكلفة 3 عصير من الدفعة (3 × 0.600)", 1.800m, D(juiceRow, "cost"));

        // مي: 24 حبة × 0.250، بعدين كرتونة (×10) بـ2.400 (= 0.240 للحبة - أرخص) - ما لازم تطلع "أعلى من المعتاد"
        await PurchaseAsync(water.Id, water.BaseUnit, 24m, 0.250m);
        await PurchaseAsync(water.Id, water.Carton!.Value, 1m, 2.400m);
        var alerts = (await GetAsync(admin, "/api/v1/notifications?pageSize=100", "تنبيهات")).GetProperty("items").EnumerateArray()
            .Select(n => n.GetProperty("title").GetString()!).ToList();
        CheckTrue("كرتونة أرخص للحبة ما بتطلع \"سعر شراء أعلى من المعتاد\"", !alerts.Any(t => t.StartsWith("سعر شراء أعلى من المعتاد")),
            string.Join(" | ", alerts));
        // وكرتونة أغلى فعلًا للحبة (0.400) لازم تنبّه
        await PurchaseAsync(water.Id, water.Carton.Value, 1m, 4.000m);
        alerts = (await GetAsync(admin, "/api/v1/notifications?pageSize=100", "تنبيهات")).GetProperty("items").EnumerateArray()
            .Select(n => n.GetProperty("title").GetString()!).ToList();
        CheckTrue("كرتونة أغلى للحبة بتنبّه", alerts.Any(t => t.StartsWith("سعر شراء أعلى من المعتاد")));

        var comparison = (await GetAsync(admin, $"/api/v1/reports/suppliers/price-comparison?productId={water.Id}&pageSize=10", "مقارنة الموردين"))
            .GetProperty("items").EnumerateArray().ToList();
        Check("مقارنة الموردين: تكلفة الحبة من فاتورة الكرتونة الرخيصة", 0.240m,
            comparison.Where(c => D(c, "unitCost") == 2.400m).Select(c => D(c, "baseUnitCost")).Single());
        CheckTrue("مقارنة الموردين: اسم وحدة الشراء", comparison.Any(c => c.GetProperty("unitName").GetString() == "كرتونة"));

        // سحب بالتكلفة: متوسط الحبة = (6 + 2.4 + 4) / (24 + 10 + 10) = 12.4 / 44 = 0.2818... → حبة 0.282، كرتونة 2.818
        var quote = await PostAsync(admin, "/api/v1/sales/at-cost/quote", new
        {
            branchId, items = new[] { new { productId = water.Id, productUnitId = water.Carton.Value, quantity = 1m, productBatchId = (Guid?)null } }
        }, "عرض سحب بالتكلفة");
        _output.WriteLine($"ℹ عرض السحب بالتكلفة: {quote}");
        Check("سحب كرتونة مي بالتكلفة (0.28182 × 10)", 2.818m, D(quote, "total"));

        _output.WriteLine($"\n{_checks - _failures.Count}/{_checks} فحص صح");
        Assert.True(_failures.Count == 0, $"{_failures.Count} من {_checks} فحص غلط:\n" + string.Join("\n", _failures));
    }

    /// <summary>
    /// قيمة رصيد صنف بدفعات = كل دفعة بتكلفتها (4/10/2026، انمسك بالمحاكاة العشوائية): دفعة رخيصة خلصت بالبيع ودفعة غالية
    /// باقية - الباقي بينقيّم بتكلفة الغالية، مش بمتوسط كل الدفعات اللي انشرت عبر التاريخ.
    /// </summary>
    [Fact]
    public async Task قيمة_رصيد_صنف_بدفعات_بتكلفة_كل_دفعة_مش_متوسط_الدفعات_اللي_خلصت()
    {
        var branchId = Fixture.TestBranchId;
        using (var scope = CreateScope())
        {
            await TestDataBuilder.EnsureDocumentSequencesProvisionedAsync(CreateDbContext(scope), branchId);
        }

        var admin = await CreateAuthenticatedClientAsync();
        var categoryId = (await PostAsync(admin, "/api/v1/product-categories", new { name = "دفعات", parentCategoryId = (Guid?)null }, "تصنيف"))
            .GetProperty("categoryId").GetGuid();
        var yogurt = await CreateProductAsync(admin, categoryId, "لبنة دفعات", 3.000m, batchTracked: true);
        var supplier = (await PostAsync(admin, "/api/v1/suppliers", new
        {
            name = "مورد الدفعات", contactName = (string?)null, phone = "0790000003", email = (string?)null,
            street = (string?)null, city = (string?)null, postalCode = (string?)null, country = (string?)null
        }, "مورد")).GetProperty("supplierId").GetGuid();
        foreach (var (batch, cost) in new[] { ("Y-1", 1.000m), ("Y-2", 2.000m) })
        {
            await PostAsync(admin, "/api/v1/purchase-invoices", new
            {
                branchId, supplierId = supplier, supplierInvoiceReference = (string?)null,
                items = new[] { new { productId = yogurt.Id, productUnitId = yogurt.BaseUnit, quantity = 10m, unitCost = cost, existingProductBatchId = (Guid?)null,
                    newBatchNumber = batch, newBatchExpiryDate = DateOnly.FromDateTime(DateTime.UtcNow.AddDays(30)) } },
                imageReferences = (string[]?)null, dueDate = (DateOnly?)null
            }, $"شراء {batch}");
        }

        Guid cheapBatch;
        using (var scope = CreateScope())
        {
            cheapBatch = await CreateDbContext(scope).ProductBatches.IgnoreQueryFilters()
                .Where(b => b.ProductId == yogurt.Id && b.BatchNumber == "Y-1").Select(b => b.Id).SingleAsync();
        }

        await SellAsync(admin, "بيع الدفعة الرخيصة كلها", new[] { Line(yogurt.Id, yogurt.BaseUnit, 10m, cheapBatch) },
            new[] { Pay(TestDataBuilder.CashPaymentMethodId, 30.000m) });

        var capital = await GetAsync(admin, $"/api/v1/reports/inventory/capital-value?pageSize=50&branchId={branchId}", "قيمة رأس المال");
        var value = capital.GetProperty("items").GetProperty("items").EnumerateArray()
            .Where(r => r.GetProperty("productId").GetGuid() == yogurt.Id).Sum(r => D(r, "totalValue"));
        Check("قيمة الباقي = 10 من الدفعة الغالية × 2.000 (مش متوسط 1.500)", 20.000m, value);
        Check("إجمالي قيمة رأس المال", 20.000m, D(capital, "totalCapitalValue"));

        _output.WriteLine($"\n{_checks - _failures.Count}/{_checks} فحص صح");
        Assert.True(_failures.Count == 0, $"{_failures.Count} من {_checks} فحص غلط:\n" + string.Join("\n", _failures));
    }

    /// <summary>كشف الربح الشهري للشهر المطلوب لازم يطلع نفس الأرقام المحسوبة باليد.</summary>
    private async Task VerifyMonthAsync(HttpClient admin, int year, int month, string label)
    {
        var p = await GetAsync(admin, $"/api/v1/finance/profit-statement?branchId={Fixture.TestBranchId}&year={year}&month={month}", $"كشف الربح - {label}");
        Check($"[{label}] المبيعات", 41.100m, D(p, "totalSales"));
        Check($"[{label}] المرتجع", 1.500m, D(p, "totalReturnedAmount"));
        Check($"[{label}] صافي الإيراد", 39.600m, D(p, "netRevenue"));
        Check($"[{label}] تكلفة البضاعة المباعة", 23.660m, D(p, "costOfGoodsSold"));
        Check($"[{label}] أسطر بلا تكلفة (الشيبس)", 1m, p.GetProperty("itemsExcludedNoCostHistory").GetInt32());
        Check($"[{label}] الربح الإجمالي", 15.940m, D(p, "grossProfit"));
        Check($"[{label}] المصاريف", 6.200m, D(p, "totalExpenses"));
        Check($"[{label}] فائض الجرد", 0.300m, D(p, "stocktakeSurplusValue"));
        Check($"[{label}] نقص الجرد", 0.800m, D(p, "stocktakeShortageValue"));
        Check($"[{label}] خسارة التلف (المستبدَل مش خسارة)", 0.600m, D(p, "wasteLossValue"));
        Check($"[{label}] تكلفة الضيافة (خبز 1 × 0.300)", 0.300m, D(p, "complimentaryCostValue"));
        Check($"[{label}] صافي الربح", 8.340m, D(p, "netProfit"));
    }

    /// <summary>
    /// بيرجّع كل تواريخ البيانات التجارية شهر لورا (بيع، شراء، دفعات، درج، حركات مخزون، جرد، مصاريف...) - الترتيب بينها
    /// محفوظ، فالتكلفة ووقت كل عملية نسبة لغيرها ما بيتغيّروا. الهوية والجلسات والإعدادات وسجل التدقيق (Temporal) برّا.
    /// </summary>
    private async Task ShiftBusinessDataOneMonthBackAsync()
    {
        var excluded = new HashSet<string>(StringComparer.OrdinalIgnoreCase)
        {
            "Users", "UserSessions", "UserLoginLogs", "UserBranches", "Roles", "Permissions", "RolePermissions",
            "SystemSettings", "Branches", "BranchDocumentSequences", "PaymentMethods", "UnitsOfMeasure", "__EFMigrationsHistory",
            "CustomerOtpCodes", "CustomerDeviceTokens"
        };

        string connectionString;
        using (var scope = CreateScope())
        {
            connectionString = CreateDbContext(scope).Database.GetConnectionString()!;
        }

        await using var connection = new SqlConnection(connectionString);
        await connection.OpenAsync();
        var columns = new List<(string Table, string Column)>();
        await using (var cmd = new SqlCommand(@"
            SELECT t.name, c.name FROM sys.columns c
            JOIN sys.tables t ON t.object_id = c.object_id
            JOIN sys.types ty ON ty.user_type_id = c.user_type_id
            WHERE ty.name IN ('datetime2','datetime','date') AND t.temporal_type = 0 AND c.is_computed = 0
              AND c.generated_always_type = 0 AND t.name NOT LIKE '%History'", connection))
        await using (var reader = await cmd.ExecuteReaderAsync())
        {
            while (await reader.ReadAsync()) columns.Add((reader.GetString(0), reader.GetString(1)));
        }

        foreach (var group in columns.Where(c => !excluded.Contains(c.Table)).GroupBy(c => c.Table))
        {
            var sets = string.Join(", ", group.Select(c => $"[{c.Column}] = DATEADD(month, -1, [{c.Column}])"));
            await using var update = new SqlCommand($"UPDATE [{group.Key}] SET {sets}", connection);
            await update.ExecuteNonQueryAsync();
        }

        await using var expenses = new SqlCommand(@"
            UPDATE Expenses SET PeriodYear = YEAR(DATEADD(month, -1, DATEFROMPARTS(PeriodYear, PeriodMonth, 1))),
                                PeriodMonth = MONTH(DATEADD(month, -1, DATEFROMPARTS(PeriodYear, PeriodMonth, 1)))", connection);
        await expenses.ExecuteNonQueryAsync();
    }
}
