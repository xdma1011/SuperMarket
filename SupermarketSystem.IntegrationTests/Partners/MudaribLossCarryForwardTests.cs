using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using SupermarketSystem.Application.Partners;
using SupermarketSystem.IntegrationTests.Common;
using Xunit;

namespace SupermarketSystem.IntegrationTests.Partners;

/// <summary>
/// ترحيل خسارة المضارب (27/10/2026، "المضارب ما يربح لحاله وأنا أتحمّل الخسارة كاملة - بتترحّل طبعًا"): نسبته بتنحسب من الربح بعد تغطية خسارة
/// الشهور اللي قبل. أموال ناس - فالاختبارات بتغطي: الحاسبة النقية (أمثلة + خصائص رياضية على آلاف التسلسلات العشوائية)، وسلسلة كشوف حقيقية بالـAPI
/// (خسارة ثم ربح أقل/أكبر/متساوي)، ورفض الشهر الناقص، وتعليم الكشوف اللي بعد إعادة إصدار قديم، ومضارب بدأ بنص الطريق، وموقوف ثم مفعّل.
/// </summary>
public sealed class MudaribLossCarryForwardCalculatorTests
{
    [Theory]
    // netProfit, carriedIn, percent  ->  profitAfterCarry, share, carriedOut
    [InlineData(-10, 0, 20, 0, 0, 10)]          // شهر خسارة: نصيب صفر والخسارة بتترحّل
    [InlineData(-5, 10, 20, 0, 0, 15)]          // خسارة ثانية بتتراكم
    [InlineData(8, 15, 20, 0, 0, 7)]            // ربح أقل من المرحَّل: نصيب صفر والباقي بيضل
    [InlineData(15, 15, 20, 0, 0, 0)]           // ربح يساوي المرحَّل بالضبط
    [InlineData(30, 10, 20, 20, 4.000, 0)]      // ربح أكبر: النسبة من الباقي بعد التغطية
    [InlineData(0, 7, 20, 0, 0, 7)]             // شهر صفري: ما بيغيّر المرحَّل
    [InlineData(45, 0, 20, 45, 9.000, 0)]       // بلا مرحَّل = السلوك العادي
    [InlineData(100, 0, 100, 100, 100.000, 0)]  // نسبة 100%
    public void أمثلة_محسوبة_بالإيد(double net, double carriedIn, double percent, double after, double share, double carriedOut)
    {
        var result = MudaribLossCarryForward.Calculate((decimal)net, (decimal)carriedIn, (decimal)percent);
        Assert.Equal((decimal)carriedIn, result.CarriedIn);
        Assert.Equal((decimal)after, result.ProfitAfterCarry);
        Assert.Equal((decimal)share, result.Share);
        Assert.Equal((decimal)carriedOut, result.CarriedOut);
    }

    [Fact]
    public void تقريب_النصيب_لأقرب_فلس_بعيدًا_عن_الصفر()
    {
        // (7.777 - 0) × 33.33% = 2.59206... → 2.592
        Assert.Equal(2.592m, MudaribLossCarryForward.Calculate(7.777m, 0m, 33.33m).Share);
        // نص فلس بالضبط بيقرّب لفوق: 0.005 × 10% = 0.0005 → 0.001
        Assert.Equal(0.001m, MudaribLossCarryForward.Calculate(0.005m, 0m, 10m).Share);
    }

    [Fact]
    public void مرحَّل_سالب_بالغلط_بيتعامل_كصفر_ومبلغ_المرحَّل_ما_بيصير_سالب_أبدًا()
    {
        var result = MudaribLossCarryForward.Calculate(10m, -50m, 20m);
        Assert.Equal(0m, result.CarriedIn);
        Assert.Equal(2.000m, result.Share);
        Assert.Equal(0m, result.CarriedOut);
    }

    /// <summary>
    /// خصائص رياضية لازم تصح لأي تسلسل شهور (5000 تسلسل عشوائي، بذرة ثابتة عشان تنعاد):
    /// (1) المرحَّل ما بيصير سالب. (2) C_t = max(0, C_{t-1} − N_t). (3) الربح الخاضع = max(0, N_t − C_{t-1}).
    /// (4) مجموع الأرباح الخاضعة = مجموع الصافي + المرحَّل الأخير (يعني المضارب ما بياخد نسبة من ربح اتغطّت فيه خسارة).
    /// (5) نصيبه الكلي ≤ نسبته × مجموع الأرباح الموجبة، ودايمًا ≥ 0.
    /// </summary>
    [Fact]
    public void خصائص_رياضية_على_تسلسلات_عشوائية()
    {
        var rng = new Random(20261027);
        for (var run = 0; run < 5000; run++)
        {
            var percent = rng.Next(1, 101);
            var months = rng.Next(1, 25);
            var carry = 0m;
            var sumNet = 0m;
            var sumTaxable = 0m;
            var sumPositive = 0m;
            var sumShare = 0m;
            for (var m = 0; m < months; m++)
            {
                var net = Math.Round((decimal)(rng.NextDouble() * 400 - 200), 3);
                var result = MudaribLossCarryForward.Calculate(net, carry, percent);

                Assert.True(result.CarriedOut >= 0m);
                Assert.Equal(Math.Max(0m, carry - net), result.CarriedOut);
                Assert.Equal(Math.Max(0m, net - carry), result.ProfitAfterCarry);
                Assert.True(result.Share >= 0m);

                carry = result.CarriedOut;
                sumNet += net;
                sumTaxable += result.ProfitAfterCarry;
                sumPositive += Math.Max(net, 0m);
                sumShare += result.Share;
            }

            Assert.Equal(sumNet + carry, sumTaxable);
            Assert.True(sumShare <= Math.Round(sumPositive * percent / 100m, 3) + 0.001m * months, $"تجاوز النسبة بالتسلسل {run}");
        }
    }
}

[Collection(DatabaseCollection.Name)]
public sealed class MudaribLossCarryForwardHttpTests : IntegrationTestBase
{
    public MudaribLossCarryForwardHttpTests(DatabaseFixture fixture) : base(fixture) { }

    private static readonly JsonSerializerOptions StringEnums = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        Converters = { new System.Text.Json.Serialization.JsonStringEnumConverter() }
    };

    private static async Task<JsonElement> JsonAsync(HttpResponseMessage response, string what)
    {
        var body = await response.Content.ReadAsStringAsync();
        Assert.True(response.IsSuccessStatusCode, $"{what}: {(int)response.StatusCode} {body}");
        return string.IsNullOrWhiteSpace(body) ? default : JsonDocument.Parse(body).RootElement;
    }

    private sealed record Setup(HttpClient Admin, Guid MilkId, Guid MilkUnit, Guid Ahmad, Guid Sami, Guid Mudarib);

    /// <summary>ثلاث شهور ماضية متتالية (M1 الأقدم). أقرب 3 شهور منتهية. نص الشهر عشان توقيت المحل ما يزحزح الحدود.</summary>
    private static DateTime MonthMiddle(int monthsAgo)
    {
        var now = DateTime.UtcNow;
        return new DateTime(now.Year, now.Month, 1, 12, 0, 0, DateTimeKind.Utc).AddMonths(-monthsAgo).AddDays(13);
    }

    private async Task<Setup> SetUpAsync(bool withMudarib = true)
    {
        var branchId = Fixture.TestBranchId;
        using (var scope = CreateScope())
        {
            await TestDataBuilder.EnsureDocumentSequencesProvisionedAsync(CreateDbContext(scope), branchId);
        }

        var admin = await CreateAuthenticatedClientAsync();
        var category = await JsonAsync(await admin.PostAsJsonAsync("/api/v1/product-categories", new { name = "ألبان", parentCategoryId = (Guid?)null }), "تصنيف");
        var created = await JsonAsync(await admin.PostAsJsonAsync("/api/v1/products", new
        {
            name = "حليب", description = (string?)null, categoryId = category.GetProperty("categoryId").GetGuid(), isBatchTracked = false,
            suggestedRetailPrice = (decimal?)null, expectedShelfLifeDays = (int?)null,
            units = new[] { new { unitName = "حبة", conversionFactorToBase = 1m, isBaseUnit = true } },
            barcodes = Array.Empty<object>()
        }), "منتج");
        var milkId = created.GetProperty("productId").GetGuid();
        await JsonAsync(await admin.PostAsJsonAsync($"/api/v1/products/{milkId}/branches",
            new { branchId, sellingPrice = 1.250m, minimumStock = 0m, maximumStock = (decimal?)null }), "ربط");
        var milkUnit = (await JsonAsync(await admin.GetAsync($"/api/v1/products/{milkId}/units"), "وحدات")).EnumerateArray().Single().GetProperty("id").GetGuid();
        var supplier = await JsonAsync(await admin.PostAsJsonAsync("/api/v1/suppliers", new
        {
            name = "مورد", contactName = (string?)null, phone = "0790000001", email = (string?)null,
            street = (string?)null, city = "عمّان", postalCode = (string?)null, country = "الأردن"
        }), "مورد");
        await JsonAsync(await admin.PostAsJsonAsync("/api/v1/purchase-invoices", new
        {
            branchId, supplierId = supplier.GetProperty("supplierId").GetGuid(), supplierInvoiceReference = "P-1",
            items = new[] { new { productId = milkId, productUnitId = milkUnit, quantity = 2000m, unitCost = 0.800m, existingProductBatchId = (Guid?)null, newBatchNumber = (string?)null, newBatchExpiryDate = (DateOnly?)null } },
            imageReferences = (string[]?)null, dueDate = (DateOnly?)null
        }), "شراء");

        async Task<Guid> PartnerAsync(string name, string type, decimal? percent) => (await JsonAsync(await admin.PostAsJsonAsync("/api/v1/partners", new
        {
            branchId, fullName = name, type, userId = (Guid?)null, speculativeProfitPercent = percent, notes = (string?)null
        }, StringEnums), $"شريك {name}")).GetProperty("partnerId").GetGuid();

        var ahmad = await PartnerAsync("أحمد", "Capital", null);
        var sami = await PartnerAsync("سامي", "Capital", null);
        var mudarib = withMudarib ? await PartnerAsync("خالد المضارب", "Speculative", 20m) : Guid.Empty;
        foreach (var (p, amount) in new[] { (ahmad, 3000m), (sami, 1000m) })
        {
            await JsonAsync(await admin.PostAsJsonAsync("/api/v1/finance/capital-transactions", new
            {
                branchId, type = "Deposit", amount, occurredAtUtc = MonthMiddle(6), notes = "رأس مال", partnerId = p
            }, StringEnums), "رأس مال");
        }

        return new Setup(admin, milkId, milkUnit, ahmad, sami, mudarib);
    }

    /// <summary>بيعة بتتأرّخ بشهر معيّن: صافي ربحها = كمية × (1.250 − 0.800) = كمية × 0.450. (التكلفة 0.800 من فاتورة الشراء.)</summary>
    private async Task SellInMonthAsync(Setup s, int monthsAgo, int quantity)
    {
        var sale = await JsonAsync(await s.Admin.PostAsJsonAsync("/api/v1/sales", new
        {
            branchId = Fixture.TestBranchId, clientRequestId = Guid.NewGuid(), customerId = (Guid?)null, invoiceLevelDiscountAmount = 0m,
            items = new[] { new { productId = s.MilkId, productUnitId = s.MilkUnit, quantity = (decimal)quantity, manualDiscountAmount = 0m, productBatchId = (Guid?)null } },
            payments = new[] { new { paymentMethodId = TestDataBuilder.CashPaymentMethodId, amount = 1.250m * quantity, externalReference = (string?)null, clientRequestId = Guid.NewGuid() } }
        }), "بيع");
        var saleId = sale.GetProperty("saleInvoiceId").GetGuid();
        var at = MonthMiddle(monthsAgo);
        using var scope = CreateScope();
        await CreateDbContext(scope).SaleInvoices.IgnoreQueryFilters().Where(i => i.Id == saleId)
            .ExecuteUpdateAsync(x => x.SetProperty(i => i.CreatedAtUtc, at));
    }

    private async Task ExpenseInMonthAsync(Setup s, int monthsAgo, decimal amount)
    {
        var at = MonthMiddle(monthsAgo);
        await JsonAsync(await s.Admin.PostAsJsonAsync("/api/v1/finance/expenses", new
        {
            branchId = Fixture.TestBranchId, category = 2, amount, paymentDateUtc = at.ToString("yyyy-MM-dd"),
            periodYear = at.Year, periodMonth = at.Month, notes = "مصروف الشهر"
        }), "مصروف");
    }

    private Task<HttpResponseMessage> GenerateAsync(Setup s, int monthsAgo)
    {
        var at = MonthMiddle(monthsAgo);
        return s.Admin.PostAsJsonAsync("/api/v1/partners/statements", new { branchId = Fixture.TestBranchId, year = at.Year, month = at.Month });
    }

    private static JsonElement Line(JsonElement statement, Guid partnerId) =>
        statement.GetProperty("lines").EnumerateArray().Single(l => l.GetProperty("partnerId").GetGuid() == partnerId);

    private static decimal D(JsonElement e, string name) => e.GetProperty(name).GetDecimal();

    [Fact]
    public async Task خسارة_ثم_ربح_أكبر_المضارب_ياخد_نسبته_من_الربح_بعد_تغطية_الخسارة_ورأس_المال_ياخد_الباقي()
    {
        var s = await SetUpAsync();
        await ExpenseInMonthAsync(s, 3, 10m);            // M1: صافي -10.000
        await SellInMonthAsync(s, 2, 100);               // M2: صافي +45.000

        var m1 = await JsonAsync(await GenerateAsync(s, 3), "كشف M1");
        Assert.Equal(-10.000m, D(m1, "netProfit"));
        Assert.Equal(0m, D(Line(m1, s.Mudarib), "shareAmount"));
        Assert.Equal(0m, D(Line(m1, s.Mudarib), "lossCarriedIn"));
        Assert.Equal(10.000m, D(Line(m1, s.Mudarib), "lossCarriedOut"));
        // رأس المال بيتحمل الخسارة كاملة بالشهر الأول: أحمد 75%، سامي 25%.
        Assert.Equal(-7.500m, D(Line(m1, s.Ahmad), "shareAmount"));
        Assert.Equal(-2.500m, D(Line(m1, s.Sami), "shareAmount"));

        var m2 = await JsonAsync(await GenerateAsync(s, 2), "كشف M2");
        Assert.Equal(45.000m, D(m2, "netProfit"));
        var mudarib = Line(m2, s.Mudarib);
        Assert.Equal(10.000m, D(mudarib, "lossCarriedIn"));
        Assert.Equal(0m, D(mudarib, "lossCarriedOut"));
        Assert.Equal(7.000m, D(mudarib, "shareAmount"));          // 20% × (45 − 10)، بدل 9.000 بلا ترحيل
        // الباقي 38 بين الرأسماليين 75/25 (28.5 و9.5) والمجموع = الصافي بالضبط.
        Assert.Equal(28.500m, D(Line(m2, s.Ahmad), "shareAmount"));
        Assert.Equal(9.500m, D(Line(m2, s.Sami), "shareAmount"));
        Assert.Equal(45.000m, m2.GetProperty("lines").EnumerateArray().Sum(l => D(l, "shareAmount")) + D(m2, "unallocatedAmount"));

        // الأرصدة التراكمية: الاثنين مع بعض صافي الشهرين = 35 (وليس 45 − 0 كما بدون ترحيل)، والمضارب 7 فقط.
        var partners = (await JsonAsync(await s.Admin.GetAsync($"/api/v1/partners?branchId={Fixture.TestBranchId}"), "الشركاء"))
            .EnumerateArray().ToDictionary(p => p.GetProperty("id").GetGuid());
        Assert.Equal(7.000m, D(partners[s.Mudarib], "currentBalance"));
        Assert.Equal(35.000m, D(partners[s.Mudarib], "currentBalance") + D(partners[s.Ahmad], "currentBalance") + D(partners[s.Sami], "currentBalance"));
    }

    [Fact]
    public async Task خسارتين_ثم_ربح_أقل_من_المرحَّل_المضارب_صفر_والباقي_بيترحّل_للشهر_الجاي()
    {
        var s = await SetUpAsync();
        await ExpenseInMonthAsync(s, 3, 10m);   // M1: -10
        await ExpenseInMonthAsync(s, 2, 5m);    // M2: -5
        await SellInMonthAsync(s, 1, 20);       // M3: +9

        await JsonAsync(await GenerateAsync(s, 3), "M1");
        var m2 = await JsonAsync(await GenerateAsync(s, 2), "M2");
        Assert.Equal(10.000m, D(Line(m2, s.Mudarib), "lossCarriedIn"));
        Assert.Equal(15.000m, D(Line(m2, s.Mudarib), "lossCarriedOut"));

        var m3 = await JsonAsync(await GenerateAsync(s, 1), "M3");
        Assert.Equal(9.000m, D(m3, "netProfit"));
        var mudarib = Line(m3, s.Mudarib);
        Assert.Equal(15.000m, D(mudarib, "lossCarriedIn"));
        Assert.Equal(0m, D(mudarib, "shareAmount"));
        Assert.Equal(6.000m, D(mudarib, "lossCarriedOut"));
        // الرأسماليين بياخدوا الربح كامل (9) لأنه ما راح للمضارب شي.
        Assert.Equal(9.000m, D(Line(m3, s.Ahmad), "shareAmount") + D(Line(m3, s.Sami), "shareAmount"));
    }

    [Fact]
    public async Task شهر_ناقص_بالسلسلة_بيرفض_الإصدار_برسالة_وبعد_إصداره_بالترتيب_بيمشي()
    {
        var s = await SetUpAsync();
        await ExpenseInMonthAsync(s, 3, 10m);
        await SellInMonthAsync(s, 1, 50);

        await JsonAsync(await GenerateAsync(s, 3), "M1");
        var skipped = await GenerateAsync(s, 1);                 // M3 بدون M2
        Assert.Equal(HttpStatusCode.UnprocessableEntity, skipped.StatusCode);
        var body = await skipped.Content.ReadAsStringAsync();
        Assert.Contains("PartnerStatement.PreviousMonthMissing", body);
        Assert.Contains($"{MonthMiddle(2).Month}/{MonthMiddle(2).Year}", body);

        using (var scope = CreateScope())
        {
            Assert.Equal(1, await CreateDbContext(scope).PartnerMonthlyStatements.IgnoreQueryFilters().CountAsync()); // ما انكتب ولا شي
        }

        await JsonAsync(await GenerateAsync(s, 2), "M2 (صفري)");
        var m3 = await JsonAsync(await GenerateAsync(s, 1), "M3");
        Assert.Equal(10.000m, D(Line(m3, s.Mudarib), "lossCarriedIn"));
        Assert.Equal(22.500m, D(m3, "netProfit"));
        Assert.Equal(2.500m, D(Line(m3, s.Mudarib), "shareAmount"));    // 20% × (22.5 − 10)
    }

    [Fact]
    public async Task إعادة_إصدار_شهر_قديم_بتغيّر_المرحَّل_بتعلّم_الكشوف_اللي_بعده_قديمة_وإعادة_إصدارها_بالترتيب_بتصلّحها()
    {
        var s = await SetUpAsync();
        await ExpenseInMonthAsync(s, 3, 10m);
        await SellInMonthAsync(s, 2, 100);   // +45
        await SellInMonthAsync(s, 1, 100);   // +45

        await JsonAsync(await GenerateAsync(s, 3), "M1");
        await JsonAsync(await GenerateAsync(s, 2), "M2");
        var m3Before = await JsonAsync(await GenerateAsync(s, 1), "M3");
        Assert.Equal(9.000m, D(Line(m3Before, s.Mudarib), "shareAmount"));    // المرحَّل اتغطى بـM2 فـM3 عادي 20% × 45

        // مصروف جديد على M1 (خسارة أكبر: -30) + إعادة إصدار M1 → المرحَّل بيتغيّر → M2 وM3 قديمين.
        await ExpenseInMonthAsync(s, 3, 20m);
        var m1 = await JsonAsync(await GenerateAsync(s, 3), "إعادة M1");
        Assert.Equal(30.000m, D(Line(m1, s.Mudarib), "lossCarriedOut"));

        var list = (await JsonAsync(await s.Admin.GetAsync($"/api/v1/partners/statements?branchId={Fixture.TestBranchId}"), "الكشوف")).EnumerateArray()
            .ToDictionary(x => (x.GetProperty("year").GetInt32(), x.GetProperty("month").GetInt32()));
        var m2Key = (MonthMiddle(2).Year, MonthMiddle(2).Month);
        var m3Key = (MonthMiddle(1).Year, MonthMiddle(1).Month);
        Assert.NotEqual(JsonValueKind.Null, list[m2Key].GetProperty("staleSinceUtc").ValueKind);
        Assert.NotEqual(JsonValueKind.Null, list[m3Key].GetProperty("staleSinceUtc").ValueKind);
        Assert.Contains("الخسارة المرحّلة", list[m2Key].GetProperty("staleReason").GetString());

        // إعادة إصدارهم بالترتيب: M2 ياخد 30 مرحَّل (45 − 30 = 15 → 3.000)، وM3 بلا مرحَّل (20% × 45 = 9.000).
        var m2 = await JsonAsync(await GenerateAsync(s, 2), "إعادة M2");
        Assert.Equal(30.000m, D(Line(m2, s.Mudarib), "lossCarriedIn"));
        Assert.Equal(3.000m, D(Line(m2, s.Mudarib), "shareAmount"));
        Assert.Equal(0m, D(Line(m2, s.Mudarib), "lossCarriedOut"));
        var m3 = await JsonAsync(await GenerateAsync(s, 1), "إعادة M3");
        Assert.Equal(9.000m, D(Line(m3, s.Mudarib), "shareAmount"));
        var after = (await JsonAsync(await s.Admin.GetAsync($"/api/v1/partners/statements?branchId={Fixture.TestBranchId}"), "الكشوف"))
            .EnumerateArray().ToDictionary(x => (x.GetProperty("year").GetInt32(), x.GetProperty("month").GetInt32()));
        Assert.Equal(JsonValueKind.Null, after[m2Key].GetProperty("staleSinceUtc").ValueKind);
        Assert.Equal(JsonValueKind.Null, after[m3Key].GetProperty("staleSinceUtc").ValueKind);
    }

    [Fact]
    public async Task إعادة_إصدار_كشف_بنفس_الأرقام_ما_بتعلّم_شي_قديم()
    {
        var s = await SetUpAsync();
        await ExpenseInMonthAsync(s, 3, 10m);
        await SellInMonthAsync(s, 2, 100);
        await JsonAsync(await GenerateAsync(s, 3), "M1");
        await JsonAsync(await GenerateAsync(s, 2), "M2");
        await JsonAsync(await GenerateAsync(s, 3), "إعادة M1 بدون تغيير");

        var list = (await JsonAsync(await s.Admin.GetAsync($"/api/v1/partners/statements?branchId={Fixture.TestBranchId}"), "الكشوف")).EnumerateArray();
        Assert.All(list, x => Assert.Equal(JsonValueKind.Null, x.GetProperty("staleSinceUtc").ValueKind));
    }

    [Fact]
    public async Task مضارب_انضاف_بعد_خسارة_ما_بيتحمّلها_والترحيل_بيبدأ_من_أول_كشف_يشارك_فيه()
    {
        var s = await SetUpAsync(withMudarib: false);
        await ExpenseInMonthAsync(s, 3, 10m);   // M1 خسارة قبل ما ينضاف المضارب
        await SellInMonthAsync(s, 2, 100);      // M2 +45
        var m1 = await JsonAsync(await GenerateAsync(s, 3), "M1 بلا مضارب");
        Assert.DoesNotContain(m1.GetProperty("lines").EnumerateArray(), l => l.GetProperty("typeCode").GetInt32() == 2);

        var mudarib = (await JsonAsync(await s.Admin.PostAsJsonAsync("/api/v1/partners", new
        {
            branchId = Fixture.TestBranchId, fullName = "خالد المضارب", type = "Speculative", userId = (Guid?)null, speculativeProfitPercent = 20m, notes = (string?)null
        }, StringEnums), "مضارب")).GetProperty("partnerId").GetGuid();

        var m2 = await JsonAsync(await GenerateAsync(s, 2), "M2");
        Assert.Equal(0m, D(Line(m2, mudarib), "lossCarriedIn"));
        Assert.Equal(9.000m, D(Line(m2, mudarib), "shareAmount"));      // 20% × 45 كاملة: خسارة M1 ما كان شريك فيها
    }

    [Fact]
    public async Task مضارب_موقوف_بشهر_بيحافظ_على_مرحَّله_ولا_بيتغطى_من_ربح_ما_شارك_فيه()
    {
        var s = await SetUpAsync();
        await ExpenseInMonthAsync(s, 3, 10m);   // M1: -10 → مرحَّل 10
        await SellInMonthAsync(s, 2, 100);      // M2: +45 (المضارب موقوف)
        await SellInMonthAsync(s, 1, 100);      // M3: +45 (المضارب رجع)

        await JsonAsync(await GenerateAsync(s, 3), "M1");
        await JsonAsync(await s.Admin.PostAsJsonAsync($"/api/v1/partners/{s.Mudarib}/active", new { isActive = false }), "إيقاف");
        var m2 = await JsonAsync(await GenerateAsync(s, 2), "M2");
        Assert.DoesNotContain(m2.GetProperty("lines").EnumerateArray(), l => l.GetProperty("partnerId").GetGuid() == s.Mudarib);

        await JsonAsync(await s.Admin.PostAsJsonAsync($"/api/v1/partners/{s.Mudarib}/active", new { isActive = true }), "تفعيل");
        var m3 = await JsonAsync(await GenerateAsync(s, 1), "M3");
        var line = Line(m3, s.Mudarib);
        Assert.Equal(10.000m, D(line, "lossCarriedIn"));                // ما اتغطّى من ربح M2 (ما شارك فيه)
        Assert.Equal(7.000m, D(line, "shareAmount"));                   // 20% × (45 − 10)
    }

    [Fact]
    public async Task مجموع_أنصبة_كل_الشركاء_دايمًا_يساوي_صافي_الكشف_بكل_حالات_الترحيل()
    {
        var s = await SetUpAsync();
        await ExpenseInMonthAsync(s, 3, 12.345m);
        await SellInMonthAsync(s, 2, 37);
        await SellInMonthAsync(s, 1, 91);
        foreach (var monthsAgo in new[] { 3, 2, 1 })
        {
            var statement = await JsonAsync(await GenerateAsync(s, monthsAgo), $"كشف قبل {monthsAgo}");
            var total = statement.GetProperty("lines").EnumerateArray().Sum(l => D(l, "shareAmount")) + D(statement, "unallocatedAmount");
            Assert.Equal(D(statement, "netProfit"), total);
        }
    }
}
