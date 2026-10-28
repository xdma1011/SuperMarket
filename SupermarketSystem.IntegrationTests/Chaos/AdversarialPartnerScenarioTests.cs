using System.Net.Http.Json;
using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using SupermarketSystem.Application.Common.Time;
using SupermarketSystem.Application.Inventory.ApproveStocktake;
using SupermarketSystem.Application.Inventory.CompleteStocktake;
using SupermarketSystem.Application.Inventory.CreateStocktake;
using SupermarketSystem.Application.Inventory.RecordStocktakeCount;
using SupermarketSystem.Domain.Notifications;
using SupermarketSystem.Domain.Partners;
using SupermarketSystem.IntegrationTests.Common;
using Xunit;

namespace SupermarketSystem.IntegrationTests.Chaos;

/// <summary>
/// اختبارات هجومية (27/10/2026، "اسأل المجلس كيف تطلع مشاكل بالسستم - خطأ أو تحايل أو خطأ بين الشركاء"): كل اختبار بيتصرف كشخص بدو
/// يخرّب أو يغشّ، وبيتحقق من **السلوك المطلوب** (مش الحالي). اللي بينكسر = ثغرة حقيقية مكتشفة (بيتعلّم `Skip` بسبب واضح لحد ما تنصلح)،
/// والباقي حراسة دايمة. (كل الثغرات الأربع المكتشفة انصلحت 27/10/2026 - الاختبارات كلها حراسة.) الأموال أموال ناس: كل كشف لازم مجموع أنصبته + غير الموزَّع = صافيه بالضبط، وسلسلة ترحيل المضارب متصلة.
/// </summary>
[Collection(DatabaseCollection.Name)]
public sealed class AdversarialPartnerScenarioTests : IntegrationTestBase
{
    public AdversarialPartnerScenarioTests(DatabaseFixture fixture) : base(fixture) { }

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

    private static decimal D(JsonElement e, string name) => e.GetProperty(name).GetDecimal();

    private sealed record Setup(HttpClient Admin, Guid MilkId, Guid MilkUnit, Guid[] Capital, Guid Mudarib);

    /// <summary>نص الشهر قبل N شهور (عشان توقيت المحل ما يزحزح الحدود).</summary>
    private static DateTime MonthMiddle(int monthsAgo)
    {
        var now = DateTime.UtcNow;
        return new DateTime(now.Year, now.Month, 1, 12, 0, 0, DateTimeKind.Utc).AddMonths(-monthsAgo).AddDays(13);
    }

    private static DateTime MonthStart(int monthsAgo)
    {
        var now = DateTime.UtcNow;
        return new DateTime(now.Year, now.Month, 1, 12, 0, 0, DateTimeKind.Utc).AddMonths(-monthsAgo);
    }

    /// <summary>حليب كلفته 0.800 وبيعه 1.250 (ربح 0.450 للحبة)، شركاء رأس مال بالمبالغ المعطاة (مودعة قبل 6 شهور)، ومضارب 20% اختياري.</summary>
    private async Task<Setup> SetUpAsync(decimal[] capitals, bool withMudarib = false)
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
            items = new[] { new { productId = milkId, productUnitId = milkUnit, quantity = 5000m, unitCost = 0.800m, existingProductBatchId = (Guid?)null, newBatchNumber = (string?)null, newBatchExpiryDate = (DateOnly?)null } },
            imageReferences = (string[]?)null, dueDate = (DateOnly?)null
        }), "شراء");

        async Task<Guid> PartnerAsync(string name, string type, decimal? percent) => (await JsonAsync(await admin.PostAsJsonAsync("/api/v1/partners", new
        {
            branchId, fullName = name, type, userId = (Guid?)null, speculativeProfitPercent = percent, notes = (string?)null
        }, StringEnums), $"شريك {name}")).GetProperty("partnerId").GetGuid();

        var capital = new List<Guid>();
        for (var i = 0; i < capitals.Length; i++)
        {
            var id = await PartnerAsync($"شريك {i + 1}", "Capital", null);
            await DepositAsync(admin, id, capitals[i], MonthMiddle(6));
            capital.Add(id);
        }

        var mudarib = withMudarib ? await PartnerAsync("المضارب", "Speculative", 20m) : Guid.Empty;
        return new Setup(admin, milkId, milkUnit, capital.ToArray(), mudarib);
    }

    private async Task DepositAsync(HttpClient admin, Guid partnerId, decimal amount, DateTime at, string type = "Deposit") =>
        await JsonAsync(await admin.PostAsJsonAsync("/api/v1/finance/capital-transactions", new
        {
            branchId = Fixture.TestBranchId, type, amount, occurredAtUtc = at, notes = "رأس مال", partnerId
        }, StringEnums), $"حركة رأس مال {type}");

    /// <summary>بيعة بتتأرّخ بشهر معيّن (تحديث CreatedAtUtc بعد الحفظ): صافي ربحها = كمية × 0.450.</summary>
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

    private async Task<HttpResponseMessage> ExpenseRawAsync(Setup s, int periodMonthsAgo, decimal amount)
    {
        var at = MonthMiddle(periodMonthsAgo);
        return await s.Admin.PostAsJsonAsync("/api/v1/finance/expenses", new
        {
            branchId = Fixture.TestBranchId, category = 2, amount, paymentDateUtc = at.ToString("yyyy-MM-dd"),
            periodYear = at.Year, periodMonth = at.Month, notes = "مصروف"
        });
    }

    private async Task ExpenseInMonthAsync(Setup s, int monthsAgo, decimal amount) =>
        await JsonAsync(await ExpenseRawAsync(s, monthsAgo, amount), "مصروف");

    private Task<HttpResponseMessage> GenerateAsync(Setup s, int monthsAgo)
    {
        var at = MonthMiddle(monthsAgo);
        return s.Admin.PostAsJsonAsync("/api/v1/partners/statements", new { branchId = Fixture.TestBranchId, year = at.Year, month = at.Month });
    }

    private static JsonElement Line(JsonElement statement, Guid partnerId) =>
        statement.GetProperty("lines").EnumerateArray().Single(l => l.GetProperty("partnerId").GetGuid() == partnerId);

    private async Task<Dictionary<(int, int), JsonElement>> StatementListAsync(Setup s) =>
        (await JsonAsync(await s.Admin.GetAsync($"/api/v1/partners/statements?branchId={Fixture.TestBranchId}"), "الكشوف"))
            .EnumerateArray().ToDictionary(x => (x.GetProperty("year").GetInt32(), x.GetProperty("month").GetInt32()));

    private static bool IsStale(JsonElement listItem) => listItem.GetProperty("staleSinceUtc").ValueKind != JsonValueKind.Null;

    private async Task<int> NotificationCountAsync(Setup s)
    {
        var page = await JsonAsync(await s.Admin.GetAsync("/api/v1/notifications?pageSize=200"), "التنبيهات");
        return page.TryGetProperty("items", out var items) ? items.GetArrayLength() : page.GetArrayLength();
    }

    private static void AssertSharesSumToNet(JsonElement statement, string what)
    {
        var total = statement.GetProperty("lines").EnumerateArray().Sum(l => D(l, "shareAmount")) + D(statement, "unallocatedAmount");
        Assert.True(D(statement, "netProfit") == total, $"{what}: مجموع الأنصبة {total} ≠ الصافي {D(statement, "netProfit")}");
    }

    // ===================================================================== هجمات

    /// <summary>
    /// [#6] بيعة وصلت متأخرة وحسب وقتها بتنتمي لفترة كشف نازل أصلًا: الكشف لازم ينعلّم "قديم" (وإلا الأنصبة بتضل على رقم ناقص).
    /// بنحاكي كشف شهر "منتهي" بإدخال سجله مباشرة بالقاعدة (الشهر الحالي ما بيصدر بالـAPI)، ثم بيعة بوقت داخل نفس الفترة وأقدم من وقت الإصدار.
    /// </summary>
    [Fact]
    public async Task بيعة_متأخرة_بوقت_داخل_فترة_كشف_نازل_بتعلّم_الكشف_قديم()
    {
        var s = await SetUpAsync(new[] { 1000m, 1000m });
        var now = DateTime.UtcNow;
        int year, month;
        using (var scope = CreateScope())
        {
            var db = CreateDbContext(scope);
            var businessTime = await BusinessTime.LoadAsync(db, CancellationToken.None);
            (year, month) = businessTime.LocalMonth(now.AddMinutes(-30));
            var statement = new PartnerMonthlyStatement(Fixture.TestBranchId, year, month);
            statement.SetContent(0m, 0m, now.AddMinutes(-10), isAutomatic: false, Array.Empty<StatementLineInput>());
            db.PartnerMonthlyStatements.Add(statement);
            await db.SaveChangesAsync();
        }

        // الكاشير بيبعت بيعة ختمها قبل 30 دقيقة (قبل إصدار الكشف) ووصلت هلق.
        await JsonAsync(await s.Admin.PostAsJsonAsync("/api/v1/sales", new
        {
            branchId = Fixture.TestBranchId, clientRequestId = Guid.NewGuid(), customerId = (Guid?)null, invoiceLevelDiscountAmount = 0m,
            items = new[] { new { productId = s.MilkId, productUnitId = s.MilkUnit, quantity = 10m, manualDiscountAmount = 0m, productBatchId = (Guid?)null } },
            payments = new[] { new { paymentMethodId = TestDataBuilder.CashPaymentMethodId, amount = 12.5m, externalReference = (string?)null, clientRequestId = Guid.NewGuid() } },
            occurredAtUtc = now.AddMinutes(-30)
        }), "بيعة متأخرة");

        Assert.True(IsStale((await StatementListAsync(s))[(year, month)]), "الكشف لازم ينعلّم قديم بعد بيعة متأخرة بفترته");
    }

    /// <summary>
    /// [#1] تلميع رأس المال: شريك بيودع مبلغ ضخم بآخر يوم بالشهر وبيسحبه أول يوم بالشهر اللي بعده. نسبته بتنحسب برأس المال آخر الشهر،
    /// فبياخد حصة الشريك الثاني. المطلوب: حصة الشريك الأمين ما تنقص بسبب إيداع ساعات.
    /// </summary>
    [Fact]
    public async Task إيداع_آخر_يوم_وسحبه_أول_يوم_ما_بيسرق_حصة_الشريك_الثاني()
    {
        var s = await SetUpAsync(new[] { 3000m, 1000m });
        await SellInMonthAsync(s, 3, 100);   // M1 صافي +45
        var honestShareOfPartner2 = Math.Round(45m * 1000m / 4000m, 3);   // 11.250

        var nextMonthStart = MonthStart(2);
        await DepositAsync(s.Admin, s.Capital[0], 30000m, nextMonthStart.AddDays(-1));          // آخر يوم بـM1
        await DepositAsync(s.Admin, s.Capital[0], 30000m, nextMonthStart.AddDays(1), "Withdrawal"); // أول يوم بـM2

        var m1 = await JsonAsync(await GenerateAsync(s, 3), "كشف M1");
        AssertSharesSumToNet(m1, "M1");
        // يوم من ~30 بمبلغ ضخم بيحسب بحجمه الزمني (رأس مال مرجَّح بالزمن): ~92% من العادلة، مش 12% زي لقطة آخر الشهر القديمة.
        var share = D(Line(m1, s.Capital[1]), "shareAmount");
        Assert.True(share >= honestShareOfPartner2 * 0.85m,
            $"حصة الشريك الثاني {share} نزلت عن 85% من العادلة {honestShareOfPartner2} بسبب إيداع ليوم واحد");
    }

    /// <summary>[#2] حركة رأس مال بتاريخ رجعي داخل شهر نزل كشفه لازم تعلّم الكشف قديم (ما ينسحب على أنصبة قديمة بصمت).</summary>
    [Fact]
    public async Task حركة_رأس_مال_رجعية_بشهر_نزل_كشفه_بتعلّمه_قديم()
    {
        var s = await SetUpAsync(new[] { 3000m, 1000m });
        await SellInMonthAsync(s, 2, 100);
        await JsonAsync(await GenerateAsync(s, 2), "كشف");
        var key = (MonthMiddle(2).Year, MonthMiddle(2).Month);
        Assert.False(IsStale((await StatementListAsync(s))[key]));

        await DepositAsync(s.Admin, s.Capital[1], 9000m, MonthMiddle(2));

        Assert.True(IsStale((await StatementListAsync(s))[key]), "إيداع رجعي بفترة كشف نازل لازم يعلّمه قديم");
    }

    /// <summary>[#7] مصروف على شهر نزل كشفه بيعلّمه قديم (ما بيتبدّل الربح بصمت).</summary>
    [Fact]
    public async Task مصروف_على_شهر_نزل_كشفه_بيعلّمه_قديم()
    {
        var s = await SetUpAsync(new[] { 1000m, 1000m });
        await SellInMonthAsync(s, 2, 100);
        await JsonAsync(await GenerateAsync(s, 2), "كشف");
        var key = (MonthMiddle(2).Year, MonthMiddle(2).Month);
        var before = await NotificationCountAsync(s);

        await ExpenseInMonthAsync(s, 2, 20m);

        Assert.True(IsStale((await StatementListAsync(s))[key]));
        // فحص تحكّم: عدّاد التنبيهات بالاختبارات الهجومية الثانية شغّال فعلًا (تنبيه "كشف صار قديم" لازم يزيده).
        Assert.True(await NotificationCountAsync(s) > before, "عدّاد التنبيهات ما زاد رغم تعليم الكشف قديم");
    }

    /// <summary>[#7] ترحيل مصروف لفترة بعيدة جدًا عن تاريخ الإدخال (تهريب ربح بين الشهور) لازم يطلّع تنبيه لصاحب المحل.</summary>
    [Fact]
    public async Task مصروف_بفترة_بعيدة_عن_تاريخ_إدخاله_بيطلّع_تنبيه()
    {
        var s = await SetUpAsync(new[] { 1000m, 1000m });
        var before = await NotificationCountAsync(s);

        await ExpenseInMonthAsync(s, 9, 500m);   // مصروف اليوم محسوب على شهر قبل 9 شهور

        Assert.True(await NotificationCountAsync(s) > before, "مصروف بفترة بعيدة بدون ولا تنبيه");
    }

    /// <summary>[#8] فائض جرد ضخم (بيزيد الربح) لازم يتعلّم/يطلّع تنبيه مثل النقص (النقص بيطلّع تنبيه خطير، الزيادة لا).</summary>
    [Fact]
    public async Task فائض_جرد_ضخم_بيطلّع_تنبيه_زي_النقص()
    {
        var s = await SetUpAsync(new[] { 1000m, 1000m });
        using var scope = CreateScope();
        await TestDataBuilder.ActAsAdminAsync(scope, Fixture);
        var before = await NotificationCountAsync(s);

        var created = await scope.ServiceProvider.GetRequiredService<CreateStocktakeHandler>().HandleAsync(
            new CreateStocktakeCommand(Fixture.TestBranchId, IncludeAllProductsAtBranch: true, ProductIds: null), CancellationToken.None);
        Assert.True(created.IsSuccess);
        var itemId = await CreateDbContext(scope).StocktakeItems.AsNoTracking()
            .Where(i => i.StocktakeId == created.Value.StocktakeId).Select(i => i.Id).FirstAsync();
        await scope.ServiceProvider.GetRequiredService<RecordStocktakeCountHandler>().HandleAsync(
            new RecordStocktakeCountCommand(created.Value.StocktakeId, itemId, CountedQuantity: 9000m), CancellationToken.None);
        await scope.ServiceProvider.GetRequiredService<CompleteStocktakeHandler>().HandleAsync(
            new CompleteStocktakeCommand(created.Value.StocktakeId), CancellationToken.None);
        var approved = await scope.ServiceProvider.GetRequiredService<ApproveStocktakeHandler>().HandleAsync(
            new ApproveStocktakeCommand(created.Value.StocktakeId), CancellationToken.None);
        Assert.True(approved.IsSuccess);

        Assert.True(await NotificationCountAsync(s) > before, "فائض جرد ضخم بدون ولا تنبيه");
    }

    /// <summary>[#13] تقريب: شركاء بنسب مش قابلة للقسمة مع مبالغ فلسية — مجموع الأنصبة + غير الموزَّع لازم يساوي الصافي بالضبط، بكل شهر.</summary>
    [Fact]
    public async Task تقريب_أنصبة_ثلاثة_شركاء_متساويين_بمبالغ_فلسية_ما_بيضيّع_ولا_بيزيد_فلس()
    {
        var s = await SetUpAsync(new[] { 1000m, 1000m, 1000m });
        await ExpenseInMonthAsync(s, 3, 100.000m);     // -100.000
        await ExpenseInMonthAsync(s, 2, 0.001m);       // -0.001
        await SellInMonthAsync(s, 1, 1);               // +0.450
        await ExpenseInMonthAsync(s, 1, 0.449m);       // => +0.001

        foreach (var monthsAgo in new[] { 3, 2, 1 })
        {
            var statement = await JsonAsync(await GenerateAsync(s, monthsAgo), $"كشف قبل {monthsAgo}");
            AssertSharesSumToNet(statement, $"قبل {monthsAgo} شهور");
        }
    }

    /// <summary>[#13] رؤوس أموال "مزعجة" (7/11/13) وأرباح بأعداد أولية: الثابت المحاسبي صامد.</summary>
    [Theory]
    [InlineData(7, 11, 13, 37)]
    [InlineData(1, 1, 1, 7)]
    [InlineData(333, 333, 334, 91)]
    public async Task رؤوس_أموال_مزعجة_الثابت_المحاسبي_صامد(int c1, int c2, int c3, int quantity)
    {
        var s = await SetUpAsync(new[] { (decimal)c1, c2, c3 });
        await SellInMonthAsync(s, 2, quantity);
        await ExpenseInMonthAsync(s, 2, 0.007m);
        var statement = await JsonAsync(await GenerateAsync(s, 2), "كشف");
        AssertSharesSumToNet(statement, "رؤوس أموال مزعجة");
    }

    /// <summary>
    /// [#12] سلسلة شهور عشوائية بمضارب، مع تعديلات رجعية (مصاريف جديدة بشهور سابقة) وإعادة إصدار بالترتيب: الثوابت — المجموع = الصافي،
    /// المرحَّل ≥ 0، المرحَّل الداخل لكل شهر = الخارج من اللي قبله، وبعد إعادة الإصدار الكاملة ما يضل ولا كشف قديم.
    /// </summary>
    [Theory]
    [InlineData(11)]
    [InlineData(22)]
    [InlineData(33)]
    public async Task سلسلة_شهور_عشوائية_مع_تعديلات_رجعية_الثوابت_صامدة(int seed)
    {
        var rng = new Random(seed);
        var s = await SetUpAsync(new[] { 3000m, 1000m, 2000m }, withMudarib: true);
        var months = new[] { 4, 3, 2, 1 };
        foreach (var monthsAgo in months)
        {
            if (rng.Next(3) > 0)
            {
                await SellInMonthAsync(s, monthsAgo, rng.Next(1, 80));
            }

            if (rng.Next(2) == 0)
            {
                await ExpenseInMonthAsync(s, monthsAgo, Math.Round((decimal)(rng.NextDouble() * 40), 3));
            }
        }

        async Task GenerateAllAsync(int fromMonthsAgo)
        {
            JsonElement? previous = null;
            foreach (var monthsAgo in months.Where(m => m <= fromMonthsAgo))
            {
                var statement = await JsonAsync(await GenerateAsync(s, monthsAgo), $"كشف قبل {monthsAgo}");
                AssertSharesSumToNet(statement, $"seed {seed}، قبل {monthsAgo}");
                var mudarib = Line(statement, s.Mudarib);
                Assert.True(D(mudarib, "lossCarriedOut") >= 0m, "مرحَّل سالب");
                if (previous is { } prev && monthsAgo != fromMonthsAgo)
                {
                    Assert.Equal(D(Line(prev, s.Mudarib), "lossCarriedOut"), D(mudarib, "lossCarriedIn"));
                }

                previous = statement;
            }
        }

        await GenerateAllAsync(4);

        for (var round = 0; round < 3; round++)
        {
            var retro = months[rng.Next(months.Length)];
            await ExpenseInMonthAsync(s, retro, Math.Round((decimal)(rng.NextDouble() * 60 + 1), 3));
            if (rng.Next(2) == 0)
            {
                await SellInMonthAsync(s, months[rng.Next(months.Length)], rng.Next(1, 40));
            }

            await GenerateAllAsync(4);
            var list = await StatementListAsync(s);
            Assert.All(list.Values, item => Assert.False(IsStale(item), $"seed {seed}: كشف ضل قديم بعد إعادة إصدار الكل بالترتيب"));
        }
    }

    /// <summary>متوسط رأس المال المرجَّح بالزمن بمثال بالإيد (10 أيام): افتتاح 200، +1000 بالبداية، +1000 باليوم 5، −500 باليوم 8 → 1600.</summary>
    [Fact]
    public async Task متوسط_رأس_المال_المرجَّح_بالزمن_بمثال_محسوب_بالإيد()
    {
        var s = await SetUpAsync(new[] { 1m });
        var start = new DateTime(2026, 3, 1, 0, 0, 0, DateTimeKind.Utc);
        var end = start.AddDays(10);
        var partner = s.Capital[0];
        await DepositAsync(s.Admin, partner, 200m, start.AddDays(-20));         // افتتاح 200 (إيداع التجهيز بتاريخ أحدث من الفترة فما بيدخل)
        await DepositAsync(s.Admin, partner, 1000m, start);
        await DepositAsync(s.Admin, partner, 1000m, start.AddDays(5));
        await DepositAsync(s.Admin, partner, 500m, start.AddDays(8), "Withdrawal");
        await DepositAsync(s.Admin, partner, 99999m, end.AddHours(1));          // بعد نهاية الفترة: ما بيدخل

        using var scope = CreateScope();
        var result = await SupermarketSystem.Application.Partners.PartnerLedger.LoadTimeWeightedCapitalAsync(
            CreateDbContext(scope), new[] { partner }, start, end, CancellationToken.None);

        Assert.Equal(1600m, result[partner]);
    }

    private async Task ApproveStocktakeWithCountAsync(IServiceScope scope, decimal counted)
    {
        var created = await scope.ServiceProvider.GetRequiredService<CreateStocktakeHandler>().HandleAsync(
            new CreateStocktakeCommand(Fixture.TestBranchId, IncludeAllProductsAtBranch: true, ProductIds: null), CancellationToken.None);
        Assert.True(created.IsSuccess, created.Error?.Message);
        var itemId = await CreateDbContext(scope).StocktakeItems.AsNoTracking()
            .Where(i => i.StocktakeId == created.Value.StocktakeId).Select(i => i.Id).FirstAsync();
        await scope.ServiceProvider.GetRequiredService<RecordStocktakeCountHandler>().HandleAsync(
            new RecordStocktakeCountCommand(created.Value.StocktakeId, itemId, CountedQuantity: counted), CancellationToken.None);
        await scope.ServiceProvider.GetRequiredService<CompleteStocktakeHandler>().HandleAsync(
            new CompleteStocktakeCommand(created.Value.StocktakeId), CancellationToken.None);
        var approved = await scope.ServiceProvider.GetRequiredService<ApproveStocktakeHandler>().HandleAsync(
            new ApproveStocktakeCommand(created.Value.StocktakeId), CancellationToken.None);
        Assert.True(approved.IsSuccess, approved.Error?.Message);
    }

    /// <summary>فائض الجرد: صغير (≈ 8 د.أ بالتكلفة) = تنبيه "مهم"؛ كبير (≈ 3200 د.أ، فوق حد 100) = "خطير". النقص ما تغيّر (خطير دايمًا).</summary>
    [Fact]
    public async Task فائض_الجرد_بيتصعّد_حسب_قيمته_صغير_مهم_وكبير_خطير()
    {
        var s = await SetUpAsync(new[] { 1000m, 1000m });
        using var scope = CreateScope();
        await TestDataBuilder.ActAsAdminAsync(scope, Fixture);

        await ApproveStocktakeWithCountAsync(scope, 5010m);     // مخزون النظام 5000 → +10 × 0.8 = 8
        await ApproveStocktakeWithCountAsync(scope, 9010m);     // بعدها 5010 → +4000 × 0.8 = 3200

        var alerts = await CreateDbContext(scope).Notifications.IgnoreQueryFilters().AsNoTracking()
            .Where(n => n.Title.StartsWith("فائض بالجرد")).OrderBy(n => n.CreatedAtUtc).ToListAsync();
        Assert.Equal(new[] { NotificationSeverity.Warning, NotificationSeverity.Critical }, alerts.Select(a => a.Severity).Distinct().Order().ToArray());
    }

    /// <summary>مصروف بالشهر الحالي أو اللي قبله = عادي بلا تنبيه "فترة بعيدة"؛ بالمستقبل = تنبيه.</summary>
    [Fact]
    public async Task مصروف_بفترة_قريبة_بلا_تنبيه_وبالمستقبل_بتنبيه()
    {
        var s = await SetUpAsync(new[] { 1000m, 1000m });
        using var scope = CreateScope();
        var db = CreateDbContext(scope);

        async Task<int> FarPeriodAlertsAsync() =>
            await db.Notifications.IgnoreQueryFilters().AsNoTracking().CountAsync(n => n.Title.StartsWith("مصروف بفترة بعيدة"));

        await ExpenseInMonthAsync(s, 0, 10m);   // الشهر الحالي
        await ExpenseInMonthAsync(s, 1, 10m);   // الشهر اللي قبله
        Assert.Equal(0, await FarPeriodAlertsAsync());

        var future = DateTime.UtcNow.AddMonths(3);
        await JsonAsync(await s.Admin.PostAsJsonAsync("/api/v1/finance/expenses", new
        {
            branchId = Fixture.TestBranchId, category = 2, amount = 10m, paymentDateUtc = DateTime.UtcNow.ToString("yyyy-MM-dd"),
            periodYear = future.Year, periodMonth = future.Month, notes = "إيجار مقدّم"
        }), "مصروف مستقبلي");
        var afterFuture = await FarPeriodAlertsAsync();   // التنبيه بينسجّل لكل مستلم، فبنقارن بالزيادة مش بعدد ثابت
        Assert.True(afterFuture > 0, "مصروف بفترة مستقبلية بلا تنبيه");

        await ExpenseInMonthAsync(s, 5, 10m);   // قبل 5 شهور
        Assert.True(await FarPeriodAlertsAsync() > afterFuture, "مصروف قبل 5 شهور بلا تنبيه");
    }

    /// <summary>بيعة عادية (بوقت الوصول) أو بوقت قريب ما بتعلّم ولا بتنبّه؛ وإعادة إرسال نفس البيعة المتأخرة ما بتكرر التنبيه.</summary>
    [Fact]
    public async Task البيعة_المتأخرة_ما_بتكرر_التعليم_وبيعة_بلا_كشف_ما_بتعلّم_شي()
    {
        var s = await SetUpAsync(new[] { 1000m, 1000m });
        var now = DateTime.UtcNow;
        var requestId = Guid.NewGuid();

        object Sale(DateTime? at) => new
        {
            branchId = Fixture.TestBranchId, clientRequestId = requestId, customerId = (Guid?)null, invoiceLevelDiscountAmount = 0m,
            items = new[] { new { productId = s.MilkId, productUnitId = s.MilkUnit, quantity = 1m, manualDiscountAmount = 0m, productBatchId = (Guid?)null } },
            payments = new[] { new { paymentMethodId = TestDataBuilder.CashPaymentMethodId, amount = 1.25m, externalReference = (string?)null, clientRequestId = Guid.NewGuid() } },
            occurredAtUtc = at
        };

        // بلا كشف أصلًا: بيعة متأخرة ما بتعمل ولا تنبيه.
        await JsonAsync(await s.Admin.PostAsJsonAsync("/api/v1/sales", Sale(now.AddHours(-3))), "بيعة متأخرة بلا كشف");
        using var scope = CreateScope();
        var db = CreateDbContext(scope);
        Assert.Equal(0, await db.Notifications.IgnoreQueryFilters().AsNoTracking().CountAsync(n => n.Title.Contains("صار قديم")));
    }

    // ===================================================================== الدفعة الثانية (28/10/2026): #3، #5، #10، #11

    private Task<HttpResponseMessage> WithdrawAsync(Setup s, Guid partnerId, decimal amount) =>
        s.Admin.PostAsJsonAsync("/api/v1/partners/withdrawals", new
        {
            partnerId, amount, source = "Drawer", notes = (string?)null, occurredAtUtc = (DateTime?)null, clientRequestId = Guid.NewGuid()
        }, StringEnums);

    private async Task<Dictionary<Guid, JsonElement>> WithdrawalsAsync(Setup s) =>
        (await JsonAsync(await s.Admin.GetAsync($"/api/v1/partners/withdrawals?branchId={Fixture.TestBranchId}"), "السحوبات"))
            .EnumerateArray().ToDictionary(w => w.GetProperty("id").GetGuid());

    /// <summary>
    /// [#3] سحب أكبر من رصيد الشريك: ما بينمنع (§1.6)، بس بيتعلّم NeedsReview برصيده قبل السحب + تنبيه "خطير"، وصاحب المحل بيعلّمه مراجَع.
    /// سحب ضمن الرصيد = عادي ("مهم") وما إله مراجعة.
    /// </summary>
    [Fact]
    public async Task سحب_فوق_رصيد_الشريك_بيمر_بس_بيتعلّم_للمراجعة_وتنبيه_خطير_وبتنراجع()
    {
        var s = await SetUpAsync(new[] { 1000m, 1000m });
        await SellInMonthAsync(s, 2, 100);                       // صافي 45 → 22.5 لكل شريك
        await JsonAsync(await GenerateAsync(s, 2), "كشف");

        var within = await JsonAsync(await WithdrawAsync(s, s.Capital[0], 10m), "سحب ضمن الرصيد");
        var over = await JsonAsync(await WithdrawAsync(s, s.Capital[0], 20m), "سحب فوق الرصيد");
        Assert.Equal(-7.5m, D(over, "newBalance"));

        var list = await WithdrawalsAsync(s);
        var withinRow = list[within.GetProperty("withdrawalId").GetGuid()];
        var overRow = list[over.GetProperty("withdrawalId").GetGuid()];
        Assert.False(withinRow.GetProperty("needsReview").GetBoolean());
        Assert.Equal(22.5m, D(withinRow, "balanceBefore"));
        Assert.True(overRow.GetProperty("needsReview").GetBoolean());
        Assert.Equal(12.5m, D(overRow, "balanceBefore"));

        using (var scope = CreateScope())
        {
            var alerts = await CreateDbContext(scope).Notifications.IgnoreQueryFilters().AsNoTracking()
                .Where(n => n.Title.StartsWith("سحب شريك")).ToListAsync();
            Assert.Contains(alerts, n => n.Title.StartsWith("سحب شريك فوق رصيده") && n.Severity == NotificationSeverity.Critical);
            Assert.Contains(alerts, n => n.Title.StartsWith("سحب شريك — ") && n.Severity == NotificationSeverity.Warning);
        }

        var overId = over.GetProperty("withdrawalId").GetGuid();
        var withinId = within.GetProperty("withdrawalId").GetGuid();
        Assert.Equal(System.Net.HttpStatusCode.UnprocessableEntity,
            (await s.Admin.PostAsync($"/api/v1/partners/withdrawals/{withinId}/mark-reviewed", null)).StatusCode);
        Assert.Equal(System.Net.HttpStatusCode.NoContent,
            (await s.Admin.PostAsync($"/api/v1/partners/withdrawals/{overId}/mark-reviewed", null)).StatusCode);
        Assert.Equal(System.Net.HttpStatusCode.NoContent,
            (await s.Admin.PostAsync($"/api/v1/partners/withdrawals/{overId}/mark-reviewed", null)).StatusCode);   // مرة تانية: بلا تغيير
        var reviewed = (await WithdrawalsAsync(s))[overId];
        Assert.NotEqual(JsonValueKind.Null, reviewed.GetProperty("reviewedAtUtc").ValueKind);
        Assert.False(string.IsNullOrEmpty(reviewed.GetProperty("reviewedByName").GetString()));
        Assert.Equal(System.Net.HttpStatusCode.NotFound,
            (await s.Admin.PostAsync($"/api/v1/partners/withdrawals/{Guid.NewGuid()}/mark-reviewed", null)).StatusCode);
    }

    /// <summary>
    /// [#3] 6 سحوبات متزامنة × 5 على رصيد 22.5: قفل الشريك بيخلّيهم يقرأوا الرصيد واحد ورا الثاني، فبالضبط اللي بعد ما نزل الرصيد تحت 5
    /// بيتعلّموا (اثنين: الرصيد 2.5 و−2.5). بلا قفل كانوا كلهم ممكن يقرأوا 22.5 وما يتعلّم ولا واحد.
    /// </summary>
    [Fact]
    public async Task سحوبات_متزامنة_ما_بتتهرّب_من_علامة_المراجعة()
    {
        var s = await SetUpAsync(new[] { 1000m, 1000m });
        await SellInMonthAsync(s, 2, 100);
        await JsonAsync(await GenerateAsync(s, 2), "كشف");

        var gate = new TaskCompletionSource();
        var tasks = Enumerable.Range(0, 6).Select(async _ =>
        {
            await gate.Task;
            return await WithdrawAsync(s, s.Capital[0], 5m);
        }).ToList();
        gate.SetResult();
        var responses = await Task.WhenAll(tasks);
        Assert.All(responses, r => Assert.True(r.IsSuccessStatusCode, $"{(int)r.StatusCode}"));

        var rows = (await WithdrawalsAsync(s)).Values.ToList();
        Assert.Equal(6, rows.Count);
        Assert.Equal(2, rows.Count(r => r.GetProperty("needsReview").GetBoolean()));
        Assert.Equal(new[] { -2.5m, 2.5m, 7.5m, 12.5m, 17.5m, 22.5m }, rows.Select(r => D(r, "balanceBefore")).Order().ToArray());
    }

    /// <summary>[#5] شريك انعرّف بعد نهاية شهر الكشف وأخد نصيب فيه = تنبيه بالاسم؛ شركاء موجودين قبل نهاية الشهر = بلا تنبيه.</summary>
    [Fact]
    public async Task شريك_انضاف_بعد_نهاية_الشهر_وأخد_نصيب_بكشفه_بيطلّع_تنبيه()
    {
        var s = await SetUpAsync(new[] { 1000m, 1000m });
        await SellInMonthAsync(s, 2, 100);
        using var scope = CreateScope();
        var db = CreateDbContext(scope);
        // الشركاء "قدام" - انعرّفوا قبل الشهر.
        await db.Partners.IgnoreQueryFilters().ExecuteUpdateAsync(x => x.SetProperty(p => p.CreatedAtUtc, MonthMiddle(8)));

        await JsonAsync(await GenerateAsync(s, 2), "كشف بلا جدد");
        Task<int> NewcomerAlertsAsync() => db.Notifications.IgnoreQueryFilters().AsNoTracking().CountAsync(n => n.Title.Contains("شريك انضاف بعد نهاية الشهر"));
        Assert.Equal(0, await NewcomerAlertsAsync());

        // مضارب جديد هلق + إعادة إصدار نفس الشهر القديم → بياخد 20% منه.
        var mudarib = (await JsonAsync(await s.Admin.PostAsJsonAsync("/api/v1/partners", new
        {
            branchId = Fixture.TestBranchId, fullName = "مضارب جديد", type = "Speculative", userId = (Guid?)null, speculativeProfitPercent = 20m, notes = (string?)null
        }, StringEnums), "مضارب")).GetProperty("partnerId").GetGuid();
        var statement = await JsonAsync(await GenerateAsync(s, 2), "إعادة إصدار");
        Assert.Equal(9.000m, D(Line(statement, mudarib), "shareAmount"));

        Assert.True(await NewcomerAlertsAsync() > 0, "مضارب جديد أخد من شهر قديم بلا تنبيه");
        var message = await db.Notifications.IgnoreQueryFilters().AsNoTracking()
            .Where(n => n.Title.Contains("شريك انضاف بعد نهاية الشهر")).Select(n => n.Message).FirstAsync();
        Assert.Contains("مضارب جديد", message);
        Assert.Contains("9.000", message);
    }

    /// <summary>[#10] تعليم تنبيه مقروء (واحد أو الكل) بيحفظ مين علّمه، وبيرجع بالقائمة (القراءة مشتركة بين الإدارة).</summary>
    [Fact]
    public async Task تعليم_التنبيه_مقروء_بيحفظ_مين_علّمه()
    {
        var s = await SetUpAsync(new[] { 1000m, 1000m });
        await WithdrawAsync(s, s.Capital[0], 1m);   // بيطلّع تنبيه
        await WithdrawAsync(s, s.Capital[1], 1m);

        var page = await JsonAsync(await s.Admin.GetAsync("/api/v1/notifications?pageSize=50&unreadOnly=true"), "تنبيهات");
        var first = page.GetProperty("items").EnumerateArray().First();
        Assert.Equal(JsonValueKind.Null, first.GetProperty("readByName").ValueKind);

        Assert.True((await s.Admin.PostAsync($"/api/v1/notifications/{first.GetProperty("id").GetGuid()}/read", null)).IsSuccessStatusCode);
        Assert.True((await s.Admin.PostAsJsonAsync("/api/v1/notifications/read-all", new { maxSeverity = (string?)null })).IsSuccessStatusCode);

        using var scope = CreateScope();
        var rows = await CreateDbContext(scope).Notifications.IgnoreQueryFilters().AsNoTracking()
            .Where(n => n.Channel == NotificationChannel.InApp).ToListAsync();
        Assert.NotEmpty(rows);
        Assert.All(rows, n => Assert.Equal(Fixture.AdminUserId, n.ReadByUserId));

        var all = await JsonAsync(await s.Admin.GetAsync("/api/v1/notifications?pageSize=50"), "بعد القراءة");
        Assert.All(all.GetProperty("items").EnumerateArray(), n => Assert.False(string.IsNullOrEmpty(n.GetProperty("readByName").GetString())));
    }

    /// <summary>
    /// [#11] تغيير توقيت المحل بيحرّك حدود الشهور، فالكشوف النازلة بتتعلّم "قديمة". نفس التوقيت (حفظ بلا تغيير) = ما بيعلّم شي.
    /// </summary>
    [Fact]
    public async Task تغيير_توقيت_المحل_بيعلّم_الكشوف_قديمة_والحفظ_بنفس_القيمة_لا()
    {
        var s = await SetUpAsync(new[] { 1000m, 1000m });
        await SellInMonthAsync(s, 2, 100);
        await JsonAsync(await GenerateAsync(s, 2), "كشف");
        var key = (MonthMiddle(2).Year, MonthMiddle(2).Month);
        try
        {
            await JsonAsync(await s.Admin.PutAsJsonAsync("/api/v1/system/time-settings", new { timeZoneId = "Asia/Amman", fixedUtcOffsetMinutes = (int?)null }), "نفس التوقيت");
            Assert.False(IsStale((await StatementListAsync(s))[key]), "حفظ بنفس التوقيت علّم الكشف");

            await JsonAsync(await s.Admin.PutAsJsonAsync("/api/v1/system/time-settings", new { timeZoneId = "Asia/Amman", fixedUtcOffsetMinutes = (int?)0 }), "فرق يدوي صفر");
            var item = (await StatementListAsync(s))[key];
            Assert.True(IsStale(item), "تغيير التوقيت ما علّم الكشف قديم");
            Assert.Contains("توقيت المحل", item.GetProperty("staleReason").GetString());
        }
        finally
        {
            await s.Admin.PutAsJsonAsync("/api/v1/system/time-settings", new { timeZoneId = "Asia/Amman", fixedUtcOffsetMinutes = (int?)null });
        }
    }
}
