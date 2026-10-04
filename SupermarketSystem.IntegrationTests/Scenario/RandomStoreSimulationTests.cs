using System.Net;
using System.Net.Http.Json;
using System.Text;
using System.Text.Json;
using Microsoft.Data.SqlClient;
using Microsoft.EntityFrameworkCore;
using SupermarketSystem.Application.Common.Interfaces;
using SupermarketSystem.Application.Common.Policies;
using SupermarketSystem.Application.Common.Promotions;
using SupermarketSystem.IntegrationTests.Common;
using Xunit;
using Xunit.Abstractions;

namespace SupermarketSystem.IntegrationTests.Scenario;

/// <summary>
/// محاكاة عشوائية لمحل كامل (4/10/2026، طلب صاحب المشروع: "عشوائي جدًا - يشتري كأنه زبون، يجيب بضاعة كأنه مدير، يصرف
/// مصاريف، الشريك يسحب أقل وأكتر من نسبته، توزيع أرباح الشهر الماضي، وبالآخر يشوف هل النتيجة متوقعة").
///
/// الفكرة: "حَكَم" بالذاكرة (Oracle) بيحسب كل شي لحاله - المخزون، متوسط التكلفة، المبيعات والمرتجع، الصندوق، الديون،
/// المصاريف، التلف والضيافة والجرد، وأنصبة الشركاء - وبعدين بيقارن بكل تقرير بالنظام عبر HTTP حقيقي.
///
///   المرحلة 1 ("الشهر الماضي"): عمليات عشوائية - بيع (كاش/فيزا/مقسوم/بالدين، حبة/كرتونة/دفعة، عروض)، شراء من موردين
///   (حبة/كرتونة/دفعات) ودفعات للموردين، تسديد ديون زبائن، إلغاء، إرجاع، تلف (ومستبدَل)، ضيافة، مصاريف (من الصندوق
///   وبرّا)، رواتب وسلف، تغيير أسعار، سحب بالتكلفة (دفع/اخصمها مني)، جرد جزئي، تقفيل صندوق.
///   ← فحص كل شي ← نقل البيانات شهر لورا (SQL، الترتيب محفوظ) ← شركاء ورأس مال ← كشف الشهر الماضي
///   المرحلة 2 ("هالشهر"): سحوبات شركاء (أقل وأكتر من النصيب، من الصندوق ومن الجيب) + عمليات عشوائية تانية ← فحص نهائي.
///
/// كل تشغيلة بذرة عشوائية جديدة (بتنطبع). لإعادة نفس التشغيلة بالزبط: SIM_SEED=الرقم. عدد العمليات: SIM_OPS (افتراضي 80
/// للمرحلة 1، ونصّه للمرحلة 2). أي فرق بيطلع مع البذرة وآخر العمليات قبله.
///
/// التكلفة: كل سطر بيع بيتفحص لحاله (تكلفته = حساب الحَكَم بدقة 4 خانات، زي عمود القاعدة)، وبعدين الحَكَم بيعتمد الرقم
/// المخزَّن للمجاميع - فالمجاميع بتنقارن بالزبط بلا فروقات تقريب.
/// </summary>
[Collection(DatabaseCollection.Name)]
public sealed class RandomStoreSimulationTests : IntegrationTestBase
{
    private readonly ITestOutputHelper _output;

    public RandomStoreSimulationTests(DatabaseFixture fixture, ITestOutputHelper output) : base(fixture)
    {
        _output = output;
    }

    // ===================================================================== الحَكَم

    private sealed class SimBatch
    {
        public Guid Id { get; init; }
        public decimal UnitCost { get; set; }
        public decimal Stock { get; set; }
    }

    private sealed class SimProduct
    {
        public Guid Id { get; init; }
        public string Name { get; init; } = "";
        public Guid ProductBranchId { get; set; }
        public decimal Price { get; set; }
        public Guid BaseUnit { get; set; }
        public Guid? Carton { get; set; }
        public decimal CartonFactor { get; init; }
        public bool IsBatch { get; init; }
        public bool ComplimentaryAllowed { get; init; }
        public bool NoCostEver { get; init; }
        public decimal Stock { get; set; }
        public List<SimBatch> Batches { get; } = new();
        public decimal PurchasedBaseQty { get; set; }
        public decimal PurchasedValue { get; set; }
        public (int Qty, decimal Price)? Promo { get; set; }

        public decimal TotalStock => IsBatch ? Batches.Sum(b => b.Stock) : Stock;
        public decimal? AverageCost => PurchasedBaseQty > 0 ? PurchasedValue / PurchasedBaseQty : null;
    }

    private sealed class SimLine
    {
        public Guid ItemId { get; set; }
        public SimProduct Product { get; init; } = null!;
        public Guid UnitId { get; init; }
        public decimal Factor { get; init; }
        public decimal Qty { get; init; }
        public decimal UnitPrice { get; init; }
        public decimal PromotionAmount { get; init; }
        public decimal? UnitCost { get; set; }
        public decimal Returned { get; set; }
        public SimBatch? Batch { get; init; }
        public decimal LineTotal => UnitPrice * Qty - PromotionAmount;
    }

    private sealed class SimSale
    {
        public Guid Id { get; init; }
        public int Phase { get; init; }
        public List<SimLine> Lines { get; } = new();
        public List<(Guid Method, decimal Amount)> Payments { get; } = new();
        public Guid? CustomerId { get; init; }
        public bool IsAtCost { get; init; }
        public bool DeductFromShare { get; init; }
        public bool Voided { get; set; }
        public bool HasLaterPayments { get; set; }
        public decimal Total => Lines.Sum(l => l.LineTotal);
        public decimal Paid => Voided ? 0m : Payments.Sum(p => p.Amount);
        public decimal Returned => Lines.Sum(l => l.Returned * l.UnitPrice);
    }

    private sealed record SimMovement(int Phase, SimProduct Product, decimal Qty, string Kind, bool Replaced);

    private sealed class SimPurchase
    {
        public Guid Id { get; init; }
        public Guid SupplierId { get; init; }
        public decimal Total { get; init; }
        public decimal Paid { get; set; }
    }

    private sealed class SimEmployee
    {
        public Guid Id { get; init; }
        public decimal Salary { get; init; }
        public decimal OutstandingAdvance { get; set; }
    }

    private sealed class SimPartner
    {
        public Guid Id { get; init; }
        public string Name { get; init; } = "";
        public bool IsSpeculative { get; init; }
        public decimal Percent { get; init; }
        public decimal Capital { get; set; }
        public decimal Shares { get; set; }
        public decimal Withdrawals { get; set; }
        public bool LinkedToAdmin { get; init; }
    }

    /// <summary>أرقام شهر واحد زي ما الحَكَم شافها - بتنحسب مرة بآخر الشهر وبتنقارن بعدين كمان (بعد النقل وبعد الشهر الجديد).</summary>
    private sealed record MonthExpectation(
        decimal TotalSales, decimal Returned, decimal Cogs, int NoCostLines, decimal Expenses,
        decimal Surplus, decimal Shortage, decimal Waste, decimal Complimentary)
    {
        public decimal NetRevenue => TotalSales - Returned;
        public decimal Gross => NetRevenue - Cogs;
        public decimal Net => Gross - Expenses + Surplus - Shortage - Waste - Complimentary;
    }

    private Random _rng = null!;
    private int _seed;
    private int _phase = 1;
    private int _checks;
    private readonly List<string> _failures = new();
    private readonly List<string> _log = new();
    private readonly List<SimProduct> _products = new();
    private readonly List<SimSale> _sales = new();
    private readonly List<SimMovement> _movements = new();
    private readonly List<SimPurchase> _purchases = new();
    private readonly List<SimEmployee> _employees = new();
    private readonly List<SimPartner> _partners = new();
    private readonly Dictionary<int, decimal> _expensesByPhase = new() { [1] = 0m, [2] = 0m };
    private readonly List<Guid> _suppliers = new();
    private readonly List<Guid> _customers = new();
    private decimal _drawer;
    private decimal _ownerReceivable;
    private int _batchCounter;
    private HttpClient _admin = null!;
    private HttpClient _cashier = null!;
    private Guid _branchId;
    private static Guid Cash => TestDataBuilder.CashPaymentMethodId;
    private static Guid Visa => TestDataBuilder.VisaPaymentMethodId;

    // ===================================================================== أدوات

    private void Log(string message)
    {
        var line = $"[م{_phase}] {message}";
        _log.Add(line);
        _output.WriteLine(line);
    }

    private void Check(string what, decimal expected, decimal actual, decimal tolerance = 0m)
    {
        _checks++;
        var ok = Math.Abs(expected - actual) <= tolerance;
        if (!ok)
        {
            var message = $"{what}: متوقع {expected:0.000####} ← فعلي {actual:0.000####}";
            _failures.Add($"[م{_phase}] {message}");
            _output.WriteLine($"✘ {message}");
        }
    }

    private void CheckTrue(string what, bool ok, string? detail = null)
    {
        _checks++;
        if (!ok)
        {
            var message = $"{what}{(detail is null ? "" : $" ({detail})")}";
            _failures.Add($"[م{_phase}] {message}");
            _output.WriteLine($"✘ {message}");
        }
    }

    private async Task<JsonElement> OkAsync(HttpResponseMessage response, string what)
    {
        var body = await response.Content.ReadAsStringAsync();
        if (!response.IsSuccessStatusCode)
        {
            throw new InvalidOperationException($"{what}: {(int)response.StatusCode} {body}");
        }

        return string.IsNullOrWhiteSpace(body) ? default : JsonDocument.Parse(body).RootElement.Clone();
    }

    private async Task<JsonElement> PostAsync(HttpClient client, string url, object body, string what) =>
        await OkAsync(await client.PostAsJsonAsync(url, body), what);

    private async Task<JsonElement> GetAsync(HttpClient client, string url, string what) =>
        await OkAsync(await client.GetAsync(url), what);

    private static decimal D(JsonElement e, string name) => e.GetProperty(name).GetDecimal();

    private static string Iso(DateTime value) => Uri.EscapeDataString(value.ToString("o"));

    private T Pick<T>(IReadOnlyList<T> items) => items[_rng.Next(items.Count)];

    private bool Chance(double p) => _rng.NextDouble() < p;

    /// <summary>مبلغ عشوائي بمضاعفات step (3 خانات بالضبط).</summary>
    private decimal Money(decimal min, decimal max, decimal step)
    {
        var steps = (int)Math.Floor((max - min) / step);
        return min + step * _rng.Next(steps + 1);
    }

    private int Int(int min, int maxInclusive) => _rng.Next(min, maxInclusive + 1);

    private static int EnvInt(string name, int fallback) =>
        int.TryParse(Environment.GetEnvironmentVariable(name), out var value) && value > 0 ? value : fallback;

    // ===================================================================== التشغيل

    [Fact]
    public async Task محاكاة_عشوائية_لمحل_كامل_شهرين_والحَكَم_بيطابق_كل_تقرير()
    {
        _seed = int.TryParse(Environment.GetEnvironmentVariable("SIM_SEED"), out var fixedSeed) ? fixedSeed : Random.Shared.Next(1, int.MaxValue);
        _rng = new Random(_seed);
        var opsPhase1 = EnvInt("SIM_OPS", 80);
        var opsPhase2 = Math.Max(10, opsPhase1 / 2);
        _output.WriteLine($"🎲 البذرة SIM_SEED={_seed} · عمليات المرحلة 1 = {opsPhase1} · المرحلة 2 = {opsPhase2}");

        try
        {
            await RunAsync(opsPhase1, opsPhase2);
        }
        catch (Exception ex)
        {
            throw new Exception(Report($"توقّفت المحاكاة بخطأ: {ex.Message}"), ex);
        }

        _output.WriteLine($"\n✔ {_checks - _failures.Count}/{_checks} فحص صح · {_log.Count} عملية · SIM_SEED={_seed}");
        Assert.True(_failures.Count == 0, Report($"{_failures.Count} من {_checks} فحص غلط:\n" + string.Join("\n", _failures.Take(40))));
    }

    private string Report(string headline)
    {
        var sb = new StringBuilder();
        sb.AppendLine(headline);
        sb.AppendLine($"لإعادة نفس التشغيلة بالزبط: SIM_SEED={_seed}");
        sb.AppendLine("آخر العمليات:");
        foreach (var line in _log.TakeLast(25))
        {
            sb.AppendLine("  " + line);
        }

        return sb.ToString();
    }

    private async Task RunAsync(int opsPhase1, int opsPhase2)
    {
        _branchId = Fixture.TestBranchId;
        await SetUpAsync();

        // ---------------- المرحلة 1 ("الشهر الماضي") ----------------
        var phase1Start = DateTime.UtcNow;
        for (var i = 0; i < opsPhase1; i++)
        {
            await RandomOperationAsync();
        }

        var local = await LocalNowAsync();
        var phase1 = ExpectMonth(1);
        Log($"آخر المرحلة 1: مبيعات {phase1.TotalSales:0.000}، صافي ربح {phase1.Net:0.000}");
        await VerifyProfitStatementAsync(local.Year, local.Month, phase1, "كشف الشهر (قبل النقل)");
        await VerifySalesReportsAsync(1, phase1Start.AddHours(-1), DateTime.UtcNow.AddHours(1), phase1);
        await VerifyStockAndCapitalAsync();
        await VerifyDebtsAsync();
        await CloseDrawerAsync();

        // ---------------- الشهر خلص: البيانات شهر لورا ----------------
        await ShiftBusinessDataOneMonthBackAsync();
        var previous = new DateTime(local.Year, local.Month, 1).AddMonths(-1);
        Log($"نقل البيانات شهر لورا ← الشهر الماضي = {previous:yyyy-MM}");
        await VerifyProfitStatementAsync(previous.Year, previous.Month, phase1, "كشف الشهر الماضي (بعد النقل)");
        var emptyNow = await GetAsync(_admin, $"/api/v1/finance/profit-statement?branchId={_branchId}&year={local.Year}&month={local.Month}", "كشف هالشهر");
        Check("هالشهر فاضي بعد النقل", 0m, D(emptyNow, "netProfit"));

        _phase = 2;
        await PartnersAndStatementAsync(previous, phase1);

        // ---------------- المرحلة 2 ("هالشهر") ----------------
        var phase2Start = DateTime.UtcNow;
        for (var i = 0; i < Int(2, 4); i++)
        {
            await PartnerWithdrawalAsync();
        }

        for (var i = 0; i < opsPhase2; i++)
        {
            await RandomOperationAsync();
        }

        var phase2 = ExpectMonth(2);
        Log($"آخر المرحلة 2: مبيعات {phase2.TotalSales:0.000}، صافي ربح {phase2.Net:0.000}");
        await VerifyProfitStatementAsync(local.Year, local.Month, phase2, "كشف هالشهر");
        await VerifyProfitStatementAsync(previous.Year, previous.Month, phase1, "الشهر الماضي ما تأثر بهالشهر");
        await VerifySalesReportsAsync(2, phase2Start.AddHours(-1), DateTime.UtcNow.AddHours(1), phase2);
        await VerifyStockAndCapitalAsync();
        await VerifyDebtsAsync();
        await VerifyPartnersAsync();
        var currentStatement = await _admin.PostAsJsonAsync("/api/v1/partners/statements", new { branchId = _branchId, year = local.Year, month = local.Month });
        CheckTrue("كشف الشركاء لهالشهر مرفوض لأنه ما خلص", currentStatement.StatusCode == HttpStatusCode.UnprocessableEntity,
            ((int)currentStatement.StatusCode).ToString());
        await CloseDrawerAsync();
    }

    private async Task<DateTime> LocalNowAsync()
    {
        var settings = await GetAsync(_admin, "/api/v1/system/time-settings", "توقيت المحل");
        return settings.GetProperty("localNow").GetDateTime();
    }

    // ===================================================================== التجهيز

    private async Task SetUpAsync()
    {
        using (var scope = CreateScope())
        {
            await TestDataBuilder.EnsureDocumentSequencesProvisionedAsync(CreateDbContext(scope), _branchId);
            // الإعدادات مش بتتصفّر بين الاختبارات (وكاشها عايش) - بنثبّت اللي المحاكاة بتعتمد عليه.
            await TestDataBuilder.SetSettingAsync(scope, PosPolicyKeys.AllowVoidSale, true);
            await TestDataBuilder.SetSettingAsync(scope, PosPolicyKeys.AllowReturn, true);
        }

        _admin = await CreateAuthenticatedClientAsync();
        var (_, cashierName) = await UsersTestDataHelper.CreateUserWithRoleReturningUsernameAsync(
            Fixture.Factory.Services, _branchId, UsersTestDataHelper.CashierRoleId, "sim.cashier");
        _cashier = await LoginHelper.LoginAsAsync(Fixture, cashierName, UsersTestDataHelper.DefaultPassword, appType: "Cashier");

        var categoryId = (await PostAsync(_admin, "/api/v1/product-categories", new { name = "محاكاة", parentCategoryId = (Guid?)null }, "تصنيف"))
            .GetProperty("categoryId").GetGuid();

        var productCount = Int(6, 9);
        var batchIndexes = Enumerable.Range(0, productCount).OrderBy(_ => _rng.Next()).Take(Int(1, 2)).ToHashSet();
        for (var i = 0; i < productCount; i++)
        {
            var isBatch = batchIndexes.Contains(i);
            await CreateProductAsync(categoryId, $"صنف {i + 1}", Money(0.250m, 4.000m, 0.050m),
                cartonFactor: Chance(0.45) ? Pick(new[] { 6m, 10m, 12m, 24m }) : 0m,
                isBatch, complimentary: !isBatch && Chance(0.5), noCost: false);
        }

        // صنف رصيده قديم بلا ولا فاتورة شراء (تكلفة غير معروفة - بيتستبعد من التكلفة بالكشف، مش صفر)
        var unknown = await CreateProductAsync(categoryId, "صنف قديم بلا فاتورة", Money(0.250m, 2.000m, 0.050m), 0m, false, false, noCost: true);
        unknown.Stock = Int(20, 60);
        using (var scope = CreateScope())
        {
            await TestDataBuilder.SetStockAsync(CreateDbContext(scope), unknown.Id, _branchId, unknown.Stock);
        }

        // عرض "N بسعر كذا" على صنف عادي
        var promoProduct = Pick(_products.Where(p => !p.IsBatch && !p.NoCostEver).ToList());
        var bundleQty = Int(2, 4);
        var bundlePrice = Math.Max(0.050m, Math.Round(bundleQty * promoProduct.Price * (decimal)(0.70 + 0.25 * _rng.NextDouble()) / 0.050m, 0) * 0.050m);
        if (bundlePrice < bundleQty * promoProduct.Price)
        {
            await PostAsync(_admin, $"/api/v1/products/{promoProduct.Id}/promotions", new
            {
                title = $"{bundleQty} بـ{bundlePrice:0.000}", bundleQuantity = bundleQty, bundlePrice, maxQuantityPerInvoice = (decimal?)null,
                startAtUtc = DateTime.UtcNow.AddHours(-1), endAtUtc = DateTime.UtcNow.AddDays(120), branchIds = (Guid[]?)null
            }, "عرض");
            promoProduct.Promo = (bundleQty, bundlePrice);
            Log($"عرض على {promoProduct.Name}: {bundleQty} بـ{bundlePrice:0.000} (السعر {promoProduct.Price:0.000})");
        }

        for (var i = 0; i < Int(2, 3); i++)
        {
            _suppliers.Add((await PostAsync(_admin, "/api/v1/suppliers", new
            {
                name = $"مورد محاكاة {i + 1}", contactName = (string?)null, phone = $"07900000{i + 10}", email = (string?)null,
                street = (string?)null, city = "عمّان", postalCode = (string?)null, country = "الأردن"
            }, "مورد")).GetProperty("supplierId").GetGuid());
        }

        using (var scope = CreateScope())
        {
            var db = CreateDbContext(scope);
            for (var i = 0; i < Int(2, 3); i++)
            {
                _customers.Add((await TestDataBuilder.CreateCustomerAsync(db, $"زبون دفتر {i + 1}", $"07955500{i + 10}")).Id);
            }
        }

        for (var i = 0; i < 2; i++)
        {
            var salary = Money(20.000m, 60.000m, 5.000m);
            var id = (await PostAsync(_admin, "/api/v1/employees", new { branchId = _branchId, fullName = $"موظف {i + 1}", phone = (string?)null, monthlySalary = salary, notes = (string?)null }, "موظف"))
                .GetProperty("employeeId").GetGuid();
            _employees.Add(new SimEmployee { Id = id, Salary = salary });
        }

        // كل صنف معروف بينشرى مرة أول عشان يكون في رصيد للبيع
        foreach (var product in _products.Where(p => !p.NoCostEver))
        {
            await PurchaseAsync(product);
        }
    }

    private async Task<SimProduct> CreateProductAsync(Guid categoryId, string name, decimal price, decimal cartonFactor, bool isBatch, bool complimentary, bool noCost)
    {
        var units = new List<object> { new { unitName = "حبة", conversionFactorToBase = 1m, isBaseUnit = true } };
        if (cartonFactor > 0)
        {
            units.Add(new { unitName = "كرتونة", conversionFactorToBase = cartonFactor, isBaseUnit = false });
        }

        var created = await PostAsync(_admin, "/api/v1/products", new
        {
            name, description = (string?)null, categoryId, isBatchTracked = isBatch, suggestedRetailPrice = (decimal?)null,
            expectedShelfLifeDays = (int?)null, units, barcodes = Array.Empty<object>()
        }, $"منتج {name}");
        var id = created.GetProperty("productId").GetGuid();
        await PostAsync(_admin, $"/api/v1/products/{id}/branches",
            new { branchId = _branchId, sellingPrice = price, minimumStock = 0m, maximumStock = (decimal?)null }, $"ربط {name}");
        var unitList = (await GetAsync(_admin, $"/api/v1/products/{id}/units", "وحدات")).EnumerateArray().ToList();
        var branches = (await GetAsync(_admin, $"/api/v1/products/{id}/branches", "فروع المنتج")).EnumerateArray().ToList();
        var product = new SimProduct
        {
            Id = id, Name = name, Price = price, CartonFactor = cartonFactor, IsBatch = isBatch, ComplimentaryAllowed = complimentary, NoCostEver = noCost,
            BaseUnit = unitList.Single(u => u.GetProperty("isBaseUnit").GetBoolean()).GetProperty("id").GetGuid(),
            Carton = cartonFactor > 0 ? unitList.Single(u => !u.GetProperty("isBaseUnit").GetBoolean()).GetProperty("id").GetGuid() : null,
            ProductBranchId = branches.Single(b => b.GetProperty("branchId").GetGuid() == _branchId).GetProperty("productBranchId").GetGuid()
        };

        if (complimentary)
        {
            using var scope = CreateScope();
            await CreateDbContext(scope).Products.IgnoreQueryFilters().Where(p => p.Id == id)
                .ExecuteUpdateAsync(s => s.SetProperty(p => p.IsComplimentaryAllowed, true));
        }

        _products.Add(product);
        Log($"صنف {name}: {price:0.000}{(cartonFactor > 0 ? $"، كرتونة ×{cartonFactor:0}" : "")}{(isBatch ? "، دفعات" : "")}{(complimentary ? "، ضيافة" : "")}");
        return product;
    }

    // ===================================================================== العمليات العشوائية

    private async Task RandomOperationAsync()
    {
        var operations = new (int Weight, Func<Task<bool>> Run)[]
        {
            (40, SaleAsync), (10, () => PurchaseAsync(null)), (5, SupplierPaymentAsync), (4, CreditPaymentAsync), (4, VoidAsync),
            (7, ReturnAsync), (4, WasteAsync), (3, ComplimentaryAsync), (5, ExpenseAsync), (2, SalaryAsync), (2, AdvanceAsync),
            (3, PriceChangeAsync), (3, AtCostAsync), (3, StocktakeAsync), (3, async () => { await CloseDrawerAsync(); return true; }),
            (_phase == 2 ? 4 : 0, async () => { await PartnerWithdrawalAsync(); return _partners.Count > 0; })
        };

        // عملية ما إلها ظروف (مثلًا ما في فاتورة ترجعها) بتنعاد بعملية تانية
        for (var attempt = 0; attempt < 10; attempt++)
        {
            var roll = _rng.Next(operations.Sum(o => o.Weight));
            foreach (var (weight, run) in operations)
            {
                if (roll < weight)
                {
                    if (await run())
                    {
                        return;
                    }

                    break;
                }

                roll -= weight;
            }
        }
    }

    private object Pay(Guid method, decimal amount) =>
        new { paymentMethodId = method, amount, externalReference = method == Visa ? $"V-{_rng.Next(100000)}" : null, clientRequestId = Guid.NewGuid() };

    private async Task<bool> SaleAsync()
    {
        var candidates = _products.Where(p => p.TotalStock > 0).OrderBy(_ => _rng.Next()).Take(Int(1, 4)).ToList();
        if (candidates.Count == 0)
        {
            return false;
        }

        var lines = new List<SimLine>();
        foreach (var product in candidates)
        {
            SimBatch? batch = product.IsBatch ? Pick(product.Batches.Where(b => b.Stock > 0).ToList()) : null;
            var available = batch?.Stock ?? product.Stock;
            var useCarton = product.Carton is not null && available >= product.CartonFactor && Chance(0.3);
            var factor = useCarton ? product.CartonFactor : 1m;
            var qty = useCarton ? Int(1, (int)Math.Min(2, Math.Floor(available / factor))) : Int(1, (int)Math.Min(6, available));
            var unitPrice = product.Price * factor;
            var promo = !useCarton && product.Promo is { } p
                ? PromotionPricingCalculator.Calculate(qty, product.Price, p.Qty, p.Price, null).PromotionAmount
                : 0m;
            decimal? cost = batch is not null ? batch.UnitCost * factor : product.AverageCost * factor;
            lines.Add(new SimLine
            {
                Product = product, UnitId = useCarton ? product.Carton!.Value : product.BaseUnit, Factor = factor, Qty = qty,
                UnitPrice = unitPrice, PromotionAmount = promo, UnitCost = cost, Batch = batch
            });
        }

        var total = lines.Sum(l => l.LineTotal);
        var payments = new List<(Guid Method, decimal Amount)>();
        Guid? customerId = null;
        var credit = false;
        var mode = _rng.NextDouble();
        if (mode < 0.55)
        {
            payments.Add((Cash, total));
        }
        else if (mode < 0.80)
        {
            payments.Add((Visa, total));
        }
        else if (mode < 0.90 && total >= 0.100m)
        {
            var cashPart = Math.Round(total * (decimal)_rng.NextDouble(), 3);
            cashPart = Math.Clamp(cashPart, 0.001m, total - 0.001m);
            payments.Add((Cash, cashPart));
            payments.Add((Visa, total - cashPart));
        }
        else
        {
            credit = true;
            customerId = Pick(_customers);
            var paidNow = Chance(0.5) ? 0m : Math.Round(total * (decimal)(_rng.NextDouble() * 0.8), 3);
            if (paidNow > 0)
            {
                payments.Add((Cash, paidNow));
            }
        }

        var response = await PostAsync(_cashier, "/api/v1/sales", new
        {
            branchId = _branchId, clientRequestId = Guid.NewGuid(), customerId, invoiceLevelDiscountAmount = 0m,
            items = lines.Select(l => new { productId = l.Product.Id, productUnitId = l.UnitId, quantity = l.Qty, manualDiscountAmount = 0m, productBatchId = l.Batch?.Id }).ToArray(),
            payments = payments.Select(p => Pay(p.Method, p.Amount)).ToArray(),
            allowCreditSale = credit
        }, "بيع");
        var sale = new SimSale { Id = response.GetProperty("saleInvoiceId").GetGuid(), Phase = _phase, CustomerId = customerId };
        sale.Lines.AddRange(lines);
        sale.Payments.AddRange(payments);
        Check("إجمالي فاتورة البيع", total, D(response, "totalAmount"));
        await MatchStoredLinesAsync(sale);
        ApplyStock(lines, -1);
        _drawer += payments.Where(p => p.Method == Cash).Sum(p => p.Amount);
        _sales.Add(sale);
        Log($"بيع {total:0.000} ({string.Join("، ", lines.Select(l => $"{l.Product.Name} {l.Qty:0}{(l.Factor > 1 ? " كرتونة" : "")}{(l.PromotionAmount > 0 ? " بالعرض" : "")}"))})" +
            $" {(credit ? $"بالدين، مدفوع {sale.Payments.Sum(p => p.Amount):0.000}" : string.Join("+", payments.Select(p => p.Method == Cash ? "كاش" : "فيزا")))}");
        return true;
    }

    /// <summary>كل سطر مخزَّن لازم يطابق الحَكَم: السعر والعرض والإجمالي، والتكلفة بدقة عمود القاعدة (4 خانات).</summary>
    private async Task MatchStoredLinesAsync(SimSale sale)
    {
        using var scope = CreateScope();
        var stored = await CreateDbContext(scope).SaleInvoiceItems.AsNoTracking().Where(i => i.SaleInvoiceId == sale.Id).ToListAsync();
        CheckTrue("عدد أسطر الفاتورة", stored.Count == sale.Lines.Count, $"{stored.Count} مقابل {sale.Lines.Count}");
        foreach (var line in sale.Lines)
        {
            var item = stored.FirstOrDefault(i => i.ProductId == line.Product.Id && i.ProductUnitId == line.UnitId);
            if (item is null)
            {
                CheckTrue($"سطر {line.Product.Name} موجود بالفاتورة", false);
                continue;
            }

            line.ItemId = item.Id;
            Check($"سعر {line.Product.Name}", line.UnitPrice, item.UnitPriceSnapshot);
            Check($"العرض على {line.Product.Name}", line.PromotionAmount, item.PromotionAmount);
            Check($"إجمالي سطر {line.Product.Name}", line.LineTotal, item.LineTotal);
            if (line.UnitCost is null || item.UnitCostSnapshot is null)
            {
                CheckTrue($"تكلفة {line.Product.Name} غير معروفة بالطرفين", line.UnitCost is null && item.UnitCostSnapshot is null,
                    $"الحَكَم {line.UnitCost?.ToString("0.0000") ?? "—"}، النظام {item.UnitCostSnapshot?.ToString("0.0000") ?? "—"}");
            }
            else
            {
                Check($"تكلفة {line.Product.Name} وقت البيع", line.UnitCost.Value, item.UnitCostSnapshot.Value, 0.0001m);
            }

            // المجاميع بعدين بالرقم المخزَّن بالزبط (بعد ما اتأكدنا إنه صح لـ4 خانات)
            line.UnitCost = item.UnitCostSnapshot;
        }
    }

    private static void ApplyStock(IEnumerable<SimLine> lines, int sign)
    {
        foreach (var line in lines)
        {
            var baseQty = line.Qty * line.Factor * sign;
            if (line.Batch is not null)
            {
                line.Batch.Stock += baseQty;
            }
            else
            {
                line.Product.Stock += baseQty;
            }
        }
    }

    private async Task<bool> PurchaseAsync(SimProduct? forced)
    {
        var chosen = forced is not null
            ? new List<SimProduct> { forced }
            : _products.Where(p => !p.NoCostEver).OrderBy(_ => _rng.Next()).Take(Int(1, 3)).ToList();
        var supplier = Pick(_suppliers);
        var items = new List<object>();
        var plan = new List<(SimProduct Product, decimal Factor, decimal Qty, decimal UnitCost, string? Batch)>();
        foreach (var product in chosen)
        {
            var baseCost = Math.Round(product.Price * (decimal)(0.45 + 0.45 * _rng.NextDouble()) / 0.005m, 0) * 0.005m;
            baseCost = Math.Max(0.005m, baseCost);
            var byCarton = product.Carton is not null && Chance(0.5);
            var factor = byCarton ? product.CartonFactor : 1m;
            var qty = byCarton ? Int(1, 4) : Int(5, 40);
            var unitCost = byCarton ? baseCost * factor + Money(-0.050m, 0.050m, 0.005m) : baseCost;
            unitCost = Math.Max(0.005m, unitCost);
            string? batch = product.IsBatch ? $"SIM-{++_batchCounter}" : null;
            plan.Add((product, factor, qty, unitCost, batch));
            items.Add(new
            {
                productId = product.Id, productUnitId = byCarton ? product.Carton!.Value : product.BaseUnit, quantity = qty, unitCost,
                existingProductBatchId = (Guid?)null, newBatchNumber = batch,
                newBatchExpiryDate = batch is null ? (DateOnly?)null : DateOnly.FromDateTime(DateTime.UtcNow.AddDays(Int(30, 180)))
            });
        }

        var response = await PostAsync(_admin, "/api/v1/purchase-invoices", new
        {
            branchId = _branchId, supplierId = supplier, supplierInvoiceReference = $"P-{_rng.Next(100000)}",
            items, imageReferences = (string[]?)null, dueDate = (DateOnly?)null
        }, "شراء");
        var total = plan.Sum(p => p.Qty * p.UnitCost);
        Check("إجمالي فاتورة الشراء", total, D(response, "totalAmount"));
        _purchases.Add(new SimPurchase { Id = response.GetProperty("purchaseInvoiceId").GetGuid(), SupplierId = supplier, Total = total });

        foreach (var (product, factor, qty, unitCost, batch) in plan)
        {
            var baseQty = qty * factor;
            product.PurchasedBaseQty += baseQty;
            product.PurchasedValue += qty * unitCost;
            if (batch is null)
            {
                product.Stock += baseQty;
                continue;
            }

            using var scope = CreateScope();
            var stored = await CreateDbContext(scope).ProductBatches.IgnoreQueryFilters().AsNoTracking()
                .SingleAsync(b => b.ProductId == product.Id && b.BatchNumber == batch);
            Check($"تكلفة حبة الدفعة {batch}", unitCost / factor, stored.UnitCost, 0.0001m);
            product.Batches.Add(new SimBatch { Id = stored.Id, UnitCost = stored.UnitCost, Stock = baseQty });
        }

        Log($"شراء {total:0.000} ({string.Join("، ", plan.Select(p => $"{p.Product.Name} {p.Qty:0}{(p.Factor > 1 ? " كرتونة" : "")}×{p.UnitCost:0.000}"))})");
        return true;
    }

    private async Task<bool> SupplierPaymentAsync()
    {
        var open = _purchases.Where(p => p.Total - p.Paid >= 0.010m).ToList();
        if (open.Count == 0)
        {
            return false;
        }

        var purchase = Pick(open);
        var remaining = purchase.Total - purchase.Paid;
        var amount = Chance(0.4) ? remaining : Math.Max(0.001m, Math.Round(remaining * (decimal)_rng.NextDouble(), 3));
        var method = Chance(0.6) ? Cash : Visa;
        var response = await PostAsync(_admin, $"/api/v1/purchase-invoices/{purchase.Id}/payments", Pay(method, amount), "دفعة لمورد");
        purchase.Paid += amount;
        Check("دين فاتورة الشراء بعد الدفعة", purchase.Total - purchase.Paid, D(response, "remainingDebt"));
        if (method == Cash)
        {
            _drawer -= amount;
        }

        Log($"دفعة لمورد {amount:0.000} {(method == Cash ? "كاش من الصندوق" : "فيزا")}");
        return true;
    }

    private async Task<bool> CreditPaymentAsync()
    {
        var open = _sales.Where(s => s.CustomerId is not null && !s.Voided && s.Total - s.Paid >= 0.010m).ToList();
        if (open.Count == 0)
        {
            return false;
        }

        var sale = Pick(open);
        var remaining = sale.Total - sale.Paid;
        var amount = Chance(0.4) ? remaining : Math.Max(0.001m, Math.Round(remaining * (decimal)_rng.NextDouble(), 3));
        var method = Chance(0.7) ? Cash : Visa;
        await PostAsync(_cashier, $"/api/v1/sales/{sale.Id}/payments", Pay(method, amount), "تسديد دين زبون");
        sale.Payments.Add((method, amount));
        sale.HasLaterPayments = true;
        if (method == Cash)
        {
            _drawer += amount;
        }

        Log($"زبون سدّد {amount:0.000} من دينه");
        return true;
    }

    private async Task<bool> VoidAsync()
    {
        // إلغاء بيعة من نفس الشهر بس، بلا إرجاع ولا تسديد لاحق ولا دفعات (الدفعة بترجع لنفس الدفعة - ما بنخمّن)
        var candidates = _sales.Where(s => s.Phase == _phase && !s.Voided && !s.IsAtCost && !s.HasLaterPayments
                                           && s.Lines.All(l => l.Returned == 0 && l.Batch is null)).ToList();
        if (candidates.Count == 0)
        {
            return false;
        }

        var sale = Pick(candidates);
        await PostAsync(_cashier, $"/api/v1/sales/{sale.Id}/void", new { reason = "CashierError", notes = "محاكاة" }, "إلغاء فاتورة");
        _drawer -= sale.Payments.Where(p => p.Method == Cash).Sum(p => p.Amount);
        sale.Voided = true;
        ApplyStock(sale.Lines, +1);
        Log($"إلغاء فاتورة {sale.Total:0.000}");
        return true;
    }

    private async Task<bool> ReturnAsync()
    {
        // إرجاع من فاتورة نفس الشهر، مسدَّدة بالكامل بطريقة دفع وحدة، لسطر بلا عرض وبلا دفعة
        var candidates = _sales
            .Where(s => s.Phase == _phase && !s.Voided && !s.IsAtCost && s.CustomerId is null
                        && s.Payments.Select(p => p.Method).Distinct().Count() == 1 && s.Paid == s.Total)
            .SelectMany(s => s.Lines.Where(l => l.PromotionAmount == 0 && l.Batch is null && l.Qty - l.Returned > 0).Select(l => (Sale: s, Line: l)))
            .ToList();
        if (candidates.Count == 0)
        {
            return false;
        }

        var (sale, line) = Pick(candidates);
        var qty = Int(1, (int)(line.Qty - line.Returned));
        var refund = qty * line.UnitPrice;
        var method = sale.Payments[0].Method;
        await PostAsync(_cashier, "/api/v1/returns", new
        {
            originalSaleInvoiceId = sale.Id, clientRequestId = Guid.NewGuid(), reason = "Defective", notes = (string?)null,
            items = new[] { new { saleInvoiceItemId = line.ItemId, quantity = (decimal)qty } },
            refunds = new[] { Pay(method, refund) }
        }, "إرجاع");
        line.Returned += qty;
        line.Product.Stock += qty * line.Factor;
        if (method == Cash)
        {
            _drawer -= refund;
        }

        Log($"إرجاع {line.Product.Name} {qty} ({refund:0.000})");
        return true;
    }

    private SimProduct? StockedSimpleProduct(Func<SimProduct, bool>? filter = null) =>
        _products.Where(p => !p.IsBatch && !p.NoCostEver && p.Stock >= 1 && (filter?.Invoke(p) ?? true))
            .OrderBy(_ => _rng.Next()).FirstOrDefault();

    private async Task<bool> WasteAsync()
    {
        var product = StockedSimpleProduct();
        if (product is null)
        {
            return false;
        }

        var qty = Int(1, (int)Math.Min(3, product.Stock));
        var replaced = Chance(0.25);
        await PostAsync(_admin, "/api/v1/inventory/waste-issues", new
        {
            productId = product.Id, productUnitId = product.BaseUnit, branchId = _branchId, quantity = (decimal)qty,
            reason = Pick(new[] { "Expired", "Broken", "StorageDamage", "Other" }), notes = (string?)null, isReplacedBySupplier = replaced
        }, "تلف");
        product.Stock -= qty;
        _movements.Add(new SimMovement(_phase, product, qty, "waste", replaced));
        Log($"تلف {product.Name} {qty}{(replaced ? " (مستبدَل من الشركة)" : "")}");
        return true;
    }

    private async Task<bool> ComplimentaryAsync()
    {
        var product = StockedSimpleProduct(p => p.ComplimentaryAllowed);
        if (product is null)
        {
            return false;
        }

        var qty = Int(1, (int)Math.Min(2, product.Stock));
        await PostAsync(_admin, "/api/v1/inventory/complimentary-issues",
            new { productId = product.Id, productUnitId = product.BaseUnit, branchId = _branchId, quantity = (decimal)qty, reason = "ضيافة محاكاة" }, "ضيافة");
        product.Stock -= qty;
        _movements.Add(new SimMovement(_phase, product, qty, "complimentary", false));
        Log($"ضيافة {product.Name} {qty}");
        return true;
    }

    private async Task<bool> ExpenseAsync()
    {
        var local = await LocalNowAsync();
        var amount = Money(1.000m, 25.000m, 0.250m);
        var fromDrawer = Chance(0.4);
        await PostAsync(_admin, "/api/v1/finance/expenses", new
        {
            branchId = _branchId, category = Int(1, 3), amount, paymentDateUtc = DateTime.UtcNow.ToString("yyyy-MM-dd"),
            periodYear = local.Year, periodMonth = local.Month, notes = "مصروف محاكاة", expenseTypeId = (Guid?)null, paidFromDrawer = fromDrawer
        }, "مصروف");
        _expensesByPhase[_phase] += amount;
        if (fromDrawer)
        {
            _drawer -= amount;
        }

        Log($"مصروف {amount:0.000}{(fromDrawer ? " من الصندوق" : "")}");
        return true;
    }

    private async Task<bool> SalaryAsync()
    {
        var employee = Pick(_employees);
        var local = await LocalNowAsync();
        var deducted = employee.OutstandingAdvance > 0 && Chance(0.6)
            ? Math.Min(employee.OutstandingAdvance, Money(1.000m, employee.Salary, 1.000m))
            : 0m;
        var fromDrawer = Chance(0.5);
        var response = await PostAsync(_admin, $"/api/v1/employees/{employee.Id}/payments", new
        {
            type = "Salary", amount = employee.Salary, advanceDeducted = deducted, periodYear = local.Year, periodMonth = local.Month,
            paidFromDrawer = fromDrawer, occurredAtUtc = (DateTime?)null, notes = (string?)null, clientRequestId = Guid.NewGuid()
        }, "راتب");
        employee.OutstandingAdvance -= deducted;
        _expensesByPhase[_phase] += employee.Salary;
        Check("صافي الراتب المنصرف", employee.Salary - deducted, D(response, "netPaid"));
        Check("رصيد سلف الموظف", employee.OutstandingAdvance, D(response, "outstandingAdvance"));
        if (fromDrawer)
        {
            _drawer -= employee.Salary - deducted;
        }

        Log($"راتب {employee.Salary:0.000} (خصم سلفة {deducted:0.000}){(fromDrawer ? " من الصندوق" : "")}");
        return true;
    }

    private async Task<bool> AdvanceAsync()
    {
        var employee = Pick(_employees);
        var amount = Money(2.000m, 20.000m, 1.000m);
        var fromDrawer = Chance(0.6);
        await PostAsync(_admin, $"/api/v1/employees/{employee.Id}/payments", new
        {
            type = "Advance", amount, advanceDeducted = 0m, periodYear = (int?)null, periodMonth = (int?)null,
            paidFromDrawer = fromDrawer, occurredAtUtc = (DateTime?)null, notes = (string?)null, clientRequestId = Guid.NewGuid()
        }, "سلفة");
        employee.OutstandingAdvance += amount;
        if (fromDrawer)
        {
            _drawer -= amount;
        }

        Log($"سلفة {amount:0.000} (مش مصروف){(fromDrawer ? " من الصندوق" : "")}");
        return true;
    }

    private async Task<bool> PriceChangeAsync()
    {
        var product = Pick(_products.Where(p => !p.NoCostEver).ToList());
        var newPrice = Math.Max(0.100m, Math.Round(product.Price * (decimal)(0.85 + 0.35 * _rng.NextDouble()) / 0.050m, 0) * 0.050m);
        if (newPrice == product.Price)
        {
            return false;
        }

        await PostAsync(_admin, $"/api/v1/products/{product.Id}/branches/{product.ProductBranchId}/price-change-requests",
            new { requestedPrice = newPrice }, "تغيير سعر");
        Log($"سعر {product.Name}: {product.Price:0.000} ← {newPrice:0.000}");
        product.Price = newPrice;
        return true;
    }

    private async Task<bool> AtCostAsync()
    {
        var product = StockedSimpleProduct();
        if (product?.AverageCost is not { } average)
        {
            return false;
        }

        var qty = Int(1, (int)Math.Min(3, product.Stock));
        var unitPrice = Math.Round(average, 3, MidpointRounding.AwayFromZero);
        var deduct = Chance(0.5);
        var response = await PostAsync(_admin, "/api/v1/sales/at-cost", new
        {
            branchId = _branchId, clientRequestId = Guid.NewGuid(),
            items = new[] { new { productId = product.Id, productUnitId = product.BaseUnit, quantity = (decimal)qty, productBatchId = (Guid?)null } },
            deductFromShare = deduct, paymentMethodId = deduct ? (Guid?)null : Cash
        }, "سحب بالتكلفة");
        var sale = new SimSale { Id = response.GetProperty("saleInvoiceId").GetGuid(), Phase = _phase, IsAtCost = true, DeductFromShare = deduct };
        var line = new SimLine { Product = product, UnitId = product.BaseUnit, Factor = 1m, Qty = qty, UnitPrice = unitPrice, UnitCost = unitPrice };
        sale.Lines.Add(line);
        if (!deduct)
        {
            sale.Payments.Add((Cash, sale.Total));
            _drawer += sale.Total;
        }

        Check("سحب بالتكلفة: الإجمالي = التكلفة", sale.Total, D(response, "totalAmount"));
        await MatchStoredLinesAsync(sale);
        product.Stock -= qty;
        _sales.Add(sale);
        Log($"سحب بالتكلفة {product.Name} {qty} = {sale.Total:0.000} ({(deduct ? "اخصمها مني" : "دفع كاش")})");
        return true;
    }

    private async Task<bool> StocktakeAsync()
    {
        var chosen = _products.Where(p => !p.IsBatch && !p.NoCostEver).OrderBy(_ => _rng.Next()).Take(Int(1, 3)).ToList();
        var stocktake = await PostAsync(_admin, "/api/v1/stocktakes",
            new { branchId = _branchId, includeAllProductsAtBranch = false, productIds = chosen.Select(p => p.Id).ToArray() }, "جرد جزئي");
        var stocktakeId = stocktake.GetProperty("stocktakeId").GetGuid();
        var notes = new List<string>();
        foreach (var item in (await GetAsync(_admin, $"/api/v1/stocktakes/{stocktakeId}", "تفاصيل الجرد")).GetProperty("items").EnumerateArray())
        {
            var product = _products.Single(p => p.Id == item.GetProperty("productId").GetGuid());
            var expected = D(item, "expectedQuantity");
            Check($"الجرد: الرصيد المتوقع لـ{product.Name}", product.Stock, expected);
            var counted = Math.Max(0m, product.Stock + Int(-2, 2));
            await PostAsync(_admin, $"/api/v1/stocktakes/{stocktakeId}/items/{item.GetProperty("stocktakeItemId").GetGuid()}/count",
                new { countedQuantity = counted }, "عدّ");
            var diff = counted - product.Stock;
            if (diff != 0)
            {
                _movements.Add(new SimMovement(_phase, product, Math.Abs(diff), diff > 0 ? "surplus" : "shortage", false));
            }

            product.Stock = counted;
            notes.Add($"{product.Name} {(diff >= 0 ? "+" : "")}{diff:0}");
        }

        await PostAsync(_admin, $"/api/v1/stocktakes/{stocktakeId}/complete", new { }, "إنهاء الجرد");
        await PostAsync(_admin, $"/api/v1/stocktakes/{stocktakeId}/approve", new { }, "اعتماد الجرد");
        Log($"جرد: {string.Join("، ", notes)}");
        return true;
    }

    private async Task CloseDrawerAsync()
    {
        var variance = Pick(new[] { 0m, 0m, 0m, -0.250m, 0.500m, -1.000m });
        var counted = Math.Max(0m, _drawer + variance);
        var closing = await PostAsync(_cashier, "/api/v1/cash-closings", new
        {
            branchId = _branchId, businessDate = DateOnly.FromDateTime(DateTime.UtcNow), countedCash = counted, countedDetails = Array.Empty<object>()
        }, "تقفيل صندوق");
        Check("التقفيل: الكاش المتوقع بالصندوق", _drawer, D(closing, "expectedCash"));
        Check("التقفيل: الفرق", counted - _drawer, D(closing, "variance"));
        Log($"تقفيل: المتوقع {_drawer:0.000}، المعدود {counted:0.000}");
        _drawer = 0m;
    }

    // ===================================================================== الشركاء

    private async Task PartnersAndStatementAsync(DateTime previous, MonthExpectation phase1)
    {
        var (samiUserId, _) = await UsersTestDataHelper.CreateUserWithRoleReturningUsernameAsync(
            Fixture.Factory.Services, _branchId, UsersTestDataHelper.CashierRoleId, "sim.partner");
        async Task<SimPartner> AddAsync(string name, bool speculative, Guid? userId, decimal percent)
        {
            var id = (await PostAsync(_admin, "/api/v1/partners", new
            {
                branchId = _branchId, fullName = name, type = speculative ? "Speculative" : "Capital", userId,
                speculativeProfitPercent = speculative ? percent : (decimal?)null, notes = (string?)null
            }, $"شريك {name}")).GetProperty("partnerId").GetGuid();
            var partner = new SimPartner { Id = id, Name = name, IsSpeculative = speculative, Percent = percent, LinkedToAdmin = userId == Fixture.AdminUserId };
            _partners.Add(partner);
            return partner;
        }

        var ahmad = await AddAsync("أحمد", false, Fixture.AdminUserId, 0m);
        var sami = await AddAsync("سامي", false, samiUserId, 0m);
        var mudarib = await AddAsync("خالد المضارب", true, null, Int(10, 40));

        var capitalDate = DateTime.SpecifyKind(previous.AddDays(2).AddHours(10), DateTimeKind.Utc);
        async Task CapitalAsync(SimPartner partner, string type, decimal amount)
        {
            await PostAsync(_admin, "/api/v1/finance/capital-transactions", new
            {
                branchId = _branchId, type, amount, occurredAtUtc = capitalDate, notes = "رأس مال محاكاة", partnerId = partner.Id
            }, "رأس مال");
            partner.Capital += type == "Deposit" ? amount : -amount;
        }

        await CapitalAsync(ahmad, "Deposit", Money(1000m, 5000m, 100m));
        await CapitalAsync(sami, "Deposit", Money(500m, 3000m, 100m));
        if (Chance(0.5))
        {
            await CapitalAsync(sami, "Withdrawal", Money(100m, 400m, 50m));
        }

        Log($"شركاء: أحمد رأس مال {ahmad.Capital:0}، سامي {sami.Capital:0}، المضارب {mudarib.Percent:0}%");

        // التوزيع زي ما الحَكَم بيفهمه (قرارات صاحب المشروع 20-21/9): المضارب بنسبته من الربح بس، والباقي (ربح أو
        // خسارة) بين أصحاب رأس المال بنسبة رأس مالهم، لأقرب فلس، وفرق التقريب لصاحب أكبر رأس مال.
        var net = Math.Round(phase1.Net, 3, MidpointRounding.AwayFromZero);
        mudarib.Shares = net > 0 ? Math.Round(net * mudarib.Percent / 100m, 3, MidpointRounding.AwayFromZero) : 0m;
        var remaining = net - mudarib.Shares;
        var capitalPartners = new[] { ahmad, sami };
        var totalCapital = capitalPartners.Sum(p => p.Capital);
        foreach (var partner in capitalPartners)
        {
            partner.Shares = Math.Round(remaining * partner.Capital / totalCapital, 3, MidpointRounding.AwayFromZero);
        }

        // التعادل برأس المال: الأقدم (أحمد انضاف أول) - نفس ترتيب السيرفر الثابت
        capitalPartners.OrderByDescending(p => p.Capital).First().Shares += remaining - capitalPartners.Sum(p => p.Shares);

        var statement = await PostAsync(_admin, "/api/v1/partners/statements", new { branchId = _branchId, year = previous.Year, month = previous.Month }, "كشف الشركاء");
        Check("كشف الشركاء: صافي الربح = كشف الربح الشهري", net, D(statement, "netProfit"));
        var lines = statement.GetProperty("lines").EnumerateArray().ToDictionary(l => l.GetProperty("partnerId").GetGuid());
        foreach (var partner in _partners)
        {
            Check($"نصيب {partner.Name} من شهر {previous:yyyy-MM}", partner.Shares, lines.TryGetValue(partner.Id, out var line) ? D(line, "shareAmount") : 0m);
        }

        Check("مجموع الأنصبة = صافي الربح", net, lines.Values.Sum(l => D(l, "shareAmount")) + D(statement, "unallocatedAmount"));
        Log($"كشف {previous:yyyy-MM}: صافي {net:0.000} ← أحمد {ahmad.Shares:0.000}، سامي {sami.Shares:0.000}، المضارب {mudarib.Shares:0.000}");
    }

    private async Task PartnerWithdrawalAsync()
    {
        if (_partners.Count == 0)
        {
            return;
        }

        var partner = Pick(_partners);
        // أقل من النصيب، أكتر منه (سلفة)، أو رقم مدوّر زي "20 دينار"
        var amount = _rng.Next(3) switch
        {
            0 => 20.000m,
            1 => Math.Max(0.500m, Math.Round(Math.Abs(partner.Shares) * (decimal)(0.1 + 1.7 * _rng.NextDouble()), 3)),
            _ => Money(1.000m, 60.000m, 0.500m)
        };
        var fromDrawer = Chance(0.6);
        await PostAsync(_admin, "/api/v1/partners/withdrawals", new
        {
            partnerId = partner.Id, amount, source = fromDrawer ? "Drawer" : "OwnerPocket", notes = (string?)null,
            occurredAtUtc = (DateTime?)null, clientRequestId = Guid.NewGuid()
        }, "سحب شريك");
        partner.Withdrawals += amount;
        if (fromDrawer)
        {
            _drawer -= amount;
        }
        else
        {
            _ownerReceivable += amount;
        }

        Log($"{partner.Name} سحب {amount:0.000} {(fromDrawer ? "من الصندوق" : "من جيب صاحب المحل")} (نصيبه {partner.Shares:0.000})");
    }

    private async Task VerifyPartnersAsync()
    {
        var atCostDeducted = _sales.Where(s => s.IsAtCost && s.DeductFromShare && !s.Voided).Sum(s => s.Total - s.Returned - s.Paid);
        var rows = (await GetAsync(_admin, $"/api/v1/partners?branchId={_branchId}", "الشركاء")).EnumerateArray().ToDictionary(p => p.GetProperty("id").GetGuid());
        foreach (var partner in _partners)
        {
            var expected = partner.Shares - partner.Withdrawals - (partner.LinkedToAdmin ? atCostDeducted : 0m);
            Check($"رصيد {partner.Name} (نصيب − سحوبات{(partner.LinkedToAdmin ? " − اخصمها مني" : "")})", expected, D(rows[partner.Id], "currentBalance"));
        }

        var receivables = (await GetAsync(_admin, $"/api/v1/partners/owner-receivables?branchId={_branchId}", "مستحق لصاحب المحل")).EnumerateArray();
        Check("المستحق لصاحب المحل (سحوبات من جيبه)", _ownerReceivable,
            receivables.Where(r => r.GetProperty("ownerUserId").GetGuid() == Fixture.AdminUserId).Sum(r => D(r, "balance")));
    }

    // ===================================================================== التوقّعات والفحص

    private MonthExpectation ExpectMonth(int phase)
    {
        var sales = _sales.Where(s => s.Phase == phase && !s.Voided).ToList();
        var lines = sales.SelectMany(s => s.Lines).ToList();
        decimal Value(string kind, bool includeReplaced = true) => _movements
            .Where(m => m.Phase == phase && m.Kind == kind && (includeReplaced || !m.Replaced))
            .Sum(m => m.Qty * (m.Product.AverageCost ?? 0m));
        return new MonthExpectation(
            TotalSales: sales.Sum(s => s.Total),
            Returned: sales.Sum(s => s.Returned),
            Cogs: lines.Where(l => l.UnitCost is not null).Sum(l => (l.Qty - l.Returned) * l.UnitCost!.Value),
            NoCostLines: lines.Count(l => l.UnitCost is null),
            Expenses: _expensesByPhase[phase],
            Surplus: Value("surplus"),
            Shortage: Value("shortage"),
            Waste: Value("waste", includeReplaced: false),
            Complimentary: Value("complimentary"));
    }

    private async Task VerifyProfitStatementAsync(int year, int month, MonthExpectation e, string label)
    {
        const decimal valuationTolerance = 0.0005m;
        var p = await GetAsync(_admin, $"/api/v1/finance/profit-statement?branchId={_branchId}&year={year}&month={month}", label);
        Check($"[{label}] المبيعات", e.TotalSales, D(p, "totalSales"));
        Check($"[{label}] المرتجع", e.Returned, D(p, "totalReturnedAmount"));
        Check($"[{label}] صافي الإيراد", e.NetRevenue, D(p, "netRevenue"));
        Check($"[{label}] تكلفة البضاعة المباعة", e.Cogs, D(p, "costOfGoodsSold"));
        Check($"[{label}] أسطر بلا تكلفة معروفة", e.NoCostLines, p.GetProperty("itemsExcludedNoCostHistory").GetInt32());
        Check($"[{label}] الربح الإجمالي", e.Gross, D(p, "grossProfit"));
        Check($"[{label}] المصاريف (والرواتب، بلا السلف)", e.Expenses, D(p, "totalExpenses"));
        Check($"[{label}] فائض الجرد", e.Surplus, D(p, "stocktakeSurplusValue"), valuationTolerance);
        Check($"[{label}] نقص الجرد", e.Shortage, D(p, "stocktakeShortageValue"), valuationTolerance);
        Check($"[{label}] خسارة التلف (المستبدَل مش خسارة)", e.Waste, D(p, "wasteLossValue"), valuationTolerance);
        Check($"[{label}] تكلفة الضيافة", e.Complimentary, D(p, "complimentaryCostValue"), valuationTolerance);
        Check($"[{label}] صافي الربح", e.Net, D(p, "netProfit"), valuationTolerance * 4);
    }

    private async Task VerifySalesReportsAsync(int phase, DateTime from, DateTime to, MonthExpectation e)
    {
        var range = $"branchId={_branchId}&fromUtc={Iso(from)}&toUtc={Iso(to)}";
        var sales = _sales.Where(s => s.Phase == phase && !s.Voided).ToList();
        var summary = (await GetAsync(_admin, $"/api/v1/reports/sales/summary?{range}", "ملخّص المبيعات")).GetProperty("period");
        Check("ملخّص المبيعات: عدد الفواتير (بلا الملغاة)", sales.Count, summary.GetProperty("invoiceCount").GetInt32());
        Check("ملخّص المبيعات: الإجمالي", e.TotalSales, D(summary, "totalSales"));
        Check("ملخّص المبيعات: المرتجع", e.Returned, D(summary, "totalReturnedAmount"));
        Check("ملخّص المبيعات: الصافي", e.NetRevenue, D(summary, "netRevenue"));
        Check("ملخّص المبيعات: توفير العروض", sales.SelectMany(s => s.Lines).Sum(l => l.PromotionAmount), D(summary, "totalPromotionDiscounts"));

        var margin = await GetAsync(_admin, $"/api/v1/reports/product-margin?pageSize=100&{range}", "هامش كل منتج");
        Check("هامش المنتجات: الإيراد = صافي الإيراد", e.NetRevenue, D(margin, "totalNetRevenue"));
        Check("هامش المنتجات: التكلفة = تكلفة البضاعة", e.Cogs, D(margin, "totalCost"));
        Check("هامش المنتجات: الهامش = الربح الإجمالي", e.Gross, D(margin, "totalMargin"));
    }

    private async Task VerifyStockAndCapitalAsync()
    {
        var stock = await GetAsync(_admin, $"/api/v1/inventory/current-stock?pageSize=500&branchId={_branchId}", "المخزون الحالي");
        var stockRows = stock.GetProperty("items").EnumerateArray().ToList();
        foreach (var product in _products)
        {
            var actual = stockRows.Where(r => r.GetProperty("productId").GetGuid() == product.Id).Sum(r => D(r, "quantityOnHand"));
            Check($"مخزون {product.Name}", product.TotalStock, actual);
        }

        var capital = await GetAsync(_admin, $"/api/v1/reports/inventory/capital-value?pageSize=500&branchId={_branchId}", "قيمة رأس المال بالمخزون");
        // صنف بدفعات إله صف لكل دفعة - بنجمع لكل صنف
        var capitalRows = capital.GetProperty("items").GetProperty("items").EnumerateArray()
            .GroupBy(r => r.GetProperty("productId").GetGuid())
            .ToDictionary(g => g.Key, g => g.Sum(r => D(r, "totalValue")));
        var expectedTotal = 0m;
        foreach (var product in _products.Where(p => !p.NoCostEver))
        {
            var expected = product.IsBatch ? product.Batches.Sum(b => b.Stock * b.UnitCost) : product.Stock * (product.AverageCost ?? 0m);
            expectedTotal += expected;
            var actual = capitalRows.GetValueOrDefault(product.Id);
            Check($"قيمة مخزون {product.Name} بالتكلفة", expected, actual, 0.001m);
        }

        Check("قيمة رأس المال بالمخزون: الإجمالي", expectedTotal, D(capital, "totalCapitalValue"), 0.001m * _products.Count);
    }

    private async Task VerifyDebtsAsync()
    {
        var customers = (await GetAsync(_admin, "/api/v1/sales/customer-debts", "ديون الزبائن")).GetProperty("customers").EnumerateArray()
            .ToDictionary(c => c.GetProperty("customerId").GetGuid(), c => D(c, "remainingDebt"));
        foreach (var customer in _customers)
        {
            var expected = _sales.Where(s => s.CustomerId == customer && !s.Voided).Sum(s => s.Total - s.Paid);
            Check("دين زبون الدفتر", expected, customers.GetValueOrDefault(customer));
        }

        var suppliers = (await GetAsync(_admin, "/api/v1/purchase-invoices/supplier-debts", "ديون الموردين")).GetProperty("suppliers").EnumerateArray()
            .ToDictionary(s => s.GetProperty("supplierId").GetGuid(), s => D(s, "remainingDebt"));
        foreach (var supplier in _suppliers)
        {
            Check("دين المورد", _purchases.Where(p => p.SupplierId == supplier).Sum(p => p.Total - p.Paid), suppliers.GetValueOrDefault(supplier));
        }
    }

    /// <summary>نفس أداة اختبار "شهر كامل بالمحل": كل تواريخ البيانات التجارية شهر لورا (الترتيب بينها محفوظ).</summary>
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
