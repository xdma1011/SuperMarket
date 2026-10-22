using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using SupermarketSystem.IntegrationTests.Common;
using Xunit;
using Xunit.Abstractions;

namespace SupermarketSystem.IntegrationTests.Chaos;

/// <summary>
/// عاصفة كاشيرات متزامنين (22/10/2026): 8 كاشيرات (مستخدمين وجلسات مختلفة) بيشتغلوا بنفس اللحظة على نفس الأصناف والمخزون - بيع، إرجاع جزئي، إلغاء، بيع بالدين
/// مع دفعة، وفتح درج - كل واحد بعمليات عشوائية بمحاكاة سلوك كاشير حقيقي (بما فيه إعادة محاولة لما يجي 409 "أعد المحاولة").
/// الحَكَم: (1) ولا 5xx؛ (2) مخزون كل صنف = افتتاحي + صافي اللي نجح فعلًا (نموذج مستقل بذاكرة الاختبار)؛ (3) مجموع المقبوض/المرتجع
/// بدفاتر الفواتير = اللي نجح؛ (4) <see cref="InvariantAuditor"/> نظيف. العدد: STORM_OPS لكل كاشير (افتراضي 120)، البذرة: STORM_SEED.
/// </summary>
[Collection(DatabaseCollection.Name)]
public sealed class MultiCashierStormTests : IntegrationTestBase
{
    private readonly ITestOutputHelper _output;
    public MultiCashierStormTests(DatabaseFixture fixture, ITestOutputHelper output) : base(fixture) { _output = output; }

    private const int CashierCount = 8;

    private sealed record Product(Guid Id, Guid UnitId, decimal Price, decimal Opening);

    private sealed class SaleState
    {
        public Guid Id { get; init; }
        public decimal Paid { get; init; }
        public required List<(Guid ItemId, Guid ProductId, decimal Qty, decimal Price)> Lines { get; init; }
        public Dictionary<Guid, decimal> ReturnedByItem { get; } = new();
        public bool Voided { get; set; }
    }

    private sealed class Tally
    {
        public readonly Dictionary<Guid, decimal> NetStock = new();   // تغيّر المخزون الصافي لكل صنف (سالب = انباع)
        public decimal Collected;                                      // مقبوض البيع (نقد) ناقص مرتجع/ملغى
        public int Sales, Returns, Voids, CreditSales, Conflicts, DrawerOpens;
        public readonly List<string> Failures = new();
        public readonly object Gate = new();
    }

    [Fact]
    public async Task عاصفة_8_كاشيرات_متزامنين_المخزون_والمبالغ_والثوابت_بتطابق_ولا_5xx()
    {
        var ops = int.TryParse(Environment.GetEnvironmentVariable("STORM_OPS"), out var o) ? o : 120;
        var seed = int.TryParse(Environment.GetEnvironmentVariable("STORM_SEED"), out var sd) ? sd : Random.Shared.Next(1, int.MaxValue);
        _output.WriteLine($"STORM_SEED={seed} · كاشيرات={CashierCount} · عمليات/كاشير={ops}");

        var products = new List<Product>();
        Guid customerId;
        using (var scope = CreateScope())
        {
            await TestDataBuilder.ActAsAdminAsync(scope, Fixture);
            var db = CreateDbContext(scope);
            await TestDataBuilder.EnsureDocumentSequencesProvisionedAsync(db, Fixture.TestBranchId);
            var rng0 = new Random(seed);
            for (var i = 0; i < 6; i++)
            {
                var (p, u) = await TestDataBuilder.CreateActiveProductAsync(db, $"صنف عاصفة {i}");
                var price = 0.250m * (i + 1) + 0.050m * rng0.Next(0, 4);
                var opening = 40m + rng0.Next(0, 60);
                await TestDataBuilder.CreateProductBranchAsync(db, p.Id, Fixture.TestBranchId, sellingPrice: price);
                await TestDataBuilder.SetStockAsync(db, p.Id, Fixture.TestBranchId, opening);
                products.Add(new Product(p.Id, u.Id, price, opening));
            }

            customerId = (await TestDataBuilder.CreateCustomerAsync(db, "زبون العاصفة", "0791234567")).Id;
        }

        var clients = new List<HttpClient>();
        for (var i = 0; i < CashierCount; i++)
        {
            var (_, name) = await UsersTestDataHelper.CreateUserWithRoleReturningUsernameAsync(
                Fixture.Factory.Services, Fixture.TestBranchId, UsersTestDataHelper.CashierRoleId, $"storm.cashier{i}");
            clients.Add(await LoginHelper.LoginAsAsync(Fixture, name, UsersTestDataHelper.DefaultPassword, appType: "Cashier"));
        }

        var tally = new Tally();
        foreach (var p in products) tally.NetStock[p.Id] = 0m;
        var gate = new TaskCompletionSource();
        var workers = clients.Select((client, index) => Task.Run(async () =>
        {
            await gate.Task;
            await WorkerAsync(client, new Random(seed + index * 7919), ops, products, customerId, tally);
        })).ToArray();
        await Task.Delay(200);
        gate.SetResult();
        await Task.WhenAll(workers);

        _output.WriteLine($"بيوع={tally.Sales} (دين {tally.CreditSales}) · إرجاعات={tally.Returns} · إلغاءات={tally.Voids} · فتح درج={tally.DrawerOpens} · تعارضات 409 أُعيدت={tally.Conflicts}");
        Assert.True(tally.Failures.Count == 0, "إخفاقات أثناء العاصفة:\n" + string.Join('\n', tally.Failures.Take(15)));

        // الحَكَم: مخزون كل صنف.
        using var check = CreateScope();
        var cdb = CreateDbContext(check);
        foreach (var p in products)
        {
            var actual = await cdb.Stocks.IgnoreQueryFilters().AsNoTracking()
                .Where(s => s.ProductId == p.Id && s.BranchId == Fixture.TestBranchId).SumAsync(s => s.QuantityOnHand);
            Assert.True(actual == p.Opening + tally.NetStock[p.Id],
                $"مخزون صنف {p.Id}: فعلي {actual} ≠ افتتاحي {p.Opening} + صافي {tally.NetStock[p.Id]}");
        }

        // المقبوض: دفعات البيع المكتملة − (مرتجع الفواتير) = المقبوض النموذجي. (الإلغاء بيعكس الدفعات فبيطلع من المجموع المكتمل)
        var paid = await cdb.SaleInvoicePayments.IgnoreQueryFilters().AsNoTracking()
            .Where(x => x.Status == SupermarketSystem.Domain.Payments.PaymentStatus.Completed).SumAsync(x => x.Amount);
        var refunded = await cdb.ReturnInvoices.IgnoreQueryFilters().AsNoTracking().SumAsync(r => r.TotalAmount);
        Assert.Equal(tally.Collected, paid - refunded);

        var openings = products.ToDictionary(p => p.Id, p => p.Opening);
        var violations = await InvariantAuditor.AuditAsync(cdb, openings);
        Assert.True(violations.Count == 0, "خرق ثوابت بعد العاصفة (SEED=" + seed + "):\n" + string.Join('\n', violations.Take(10)));
    }

    private async Task WorkerAsync(HttpClient client, Random rng, int ops, List<Product> products, Guid customerId, Tally tally)
    {
        var mySales = new List<SaleState>();

        async Task<HttpResponseMessage> WithRetryAsync(Func<Task<HttpResponseMessage>> send)
        {
            HttpResponseMessage response = null!;
            for (var attempt = 0; attempt < 25; attempt++)
            {
                response = await send();
                if (response.StatusCode != HttpStatusCode.Conflict) return response;
                lock (tally.Gate) tally.Conflicts++;
                await Task.Delay(10 + attempt * 15 + rng.Next(25));
            }

            return response;
        }

        void Fail(string what, HttpResponseMessage r, string body)
        {
            lock (tally.Gate) tally.Failures.Add($"{what}: {(int)r.StatusCode} {(body.Length > 200 ? body[..200] : body)}");
        }

        for (var i = 0; i < ops; i++)
        {
            var roll = rng.Next(100);
            if (roll < 50 || mySales.Count == 0)
            {
                // بيع 1-3 أصناف، دفع نقد كامل (أو بالدين مع دفعة جزئية 15%).
                var credit = rng.Next(100) < 15;
                var chosen = products.OrderBy(_ => rng.Next()).Take(rng.Next(1, 4)).Select(p => (Product: p, Qty: (decimal)rng.Next(1, 4))).ToList();
                var total = chosen.Sum(c => c.Product.Price * c.Qty);
                var paidNow = credit ? Math.Round(total / 3, 3) : total;
                var response = await WithRetryAsync(() => client.PostAsJsonAsync("/api/v1/sales", new
                {
                    branchId = Fixture.TestBranchId, clientRequestId = Guid.NewGuid(), customerId = credit ? customerId : (Guid?)null, allowCreditSale = credit,
                    invoiceLevelDiscountAmount = 0m,
                    items = chosen.Select(c => new { productId = c.Product.Id, productUnitId = c.Product.UnitId, quantity = c.Qty, manualDiscountAmount = 0m, productBatchId = (Guid?)null, catalogVersion = (long?)null, promotionId = (Guid?)null }).ToArray(),
                    payments = paidNow > 0
                        ? new[] { new { paymentMethodId = TestDataBuilder.CashPaymentMethodId, amount = paidNow, externalReference = (string?)null, clientRequestId = Guid.NewGuid() } }
                        : [],
                    preparedOrderId = (Guid?)null, customerPhone = credit ? "0791234567" : null
                }));
                var body = await response.Content.ReadAsStringAsync();
                if (!response.IsSuccessStatusCode) { Fail("بيع", response, body); continue; }

                var saleId = JsonDocument.Parse(body).RootElement.GetProperty("saleInvoiceId").GetGuid();
                var detail = JsonDocument.Parse(await client.GetStringAsync($"/api/v1/sales/{saleId}")).RootElement;
                var lines = detail.GetProperty("items").EnumerateArray()
                    .Select(x => (x.GetProperty("saleInvoiceItemId").GetGuid(), x.GetProperty("productId").GetGuid(), x.GetProperty("quantity").GetDecimal(), x.GetProperty("unitPriceSnapshot").GetDecimal()))
                    .ToList();
                mySales.Add(new SaleState { Id = saleId, Paid = paidNow, Lines = lines });
                lock (tally.Gate)
                {
                    tally.Sales++;
                    if (credit) tally.CreditSales++;
                    tally.Collected += paidNow;
                    foreach (var c in chosen) tally.NetStock[c.Product.Id] -= c.Qty;
                }
            }
            else if (roll < 75)
            {
                // إرجاع جزئي (وحدة وحدة) لسطر من بيعاتي - الإرجاع بيرجّع السعر بالنقد.
                var sale = mySales.Where(s => !s.Voided).OrderBy(_ => rng.Next()).FirstOrDefault();
                var line = sale?.Lines.Where(l => l.Qty - sale.ReturnedByItem.GetValueOrDefault(l.ItemId) >= 1).OrderBy(_ => rng.Next()).Cast<(Guid ItemId, Guid ProductId, decimal Qty, decimal Price)?>().FirstOrDefault();
                if (sale is null || line is null) continue;
                var l = line.Value;
                var response = await WithRetryAsync(() => client.PostAsJsonAsync("/api/v1/returns", new
                {
                    originalSaleInvoiceId = sale.Id, clientRequestId = Guid.NewGuid(), reason = "CustomerChangedMind", notes = (string?)null,
                    items = new[] { new { saleInvoiceItemId = l.ItemId, quantity = 1m } },
                    refunds = new[] { new { paymentMethodId = TestDataBuilder.CashPaymentMethodId, amount = l.Price, externalReference = (string?)null, clientRequestId = Guid.NewGuid() } }
                }));
                var body = await response.Content.ReadAsStringAsync();
                if (response.IsSuccessStatusCode)
                {
                    sale.ReturnedByItem[l.ItemId] = sale.ReturnedByItem.GetValueOrDefault(l.ItemId) + 1m;
                    lock (tally.Gate) { tally.Returns++; tally.Collected -= l.Price; tally.NetStock[l.ProductId] += 1m; }
                }
                else if (response.StatusCode != HttpStatusCode.UnprocessableEntity) // 422 = قاعدة عمل مقبولة (مثلًا رد أكتر من المدفوع على بيع بالدين)
                {
                    Fail("إرجاع", response, body);
                }
            }
            else if (roll < 88)
            {
                // إلغاء فاتورة ما إلها إرجاعات (إلغاء بعد إرجاع جزئي مرفوض بالتصميم).
                var sale = mySales.Where(s => !s.Voided && s.ReturnedByItem.Count == 0).OrderBy(_ => rng.Next()).FirstOrDefault();
                if (sale is null) continue;
                var response = await WithRetryAsync(() => client.PostAsJsonAsync($"/api/v1/sales/{sale.Id}/void", new { reason = "CashierError", notes = (string?)null }));
                var body = await response.Content.ReadAsStringAsync();
                if (response.IsSuccessStatusCode)
                {
                    sale.Voided = true;
                    lock (tally.Gate)
                    {
                        tally.Voids++;
                        tally.Collected -= sale.Paid; // الإلغاء بيعكس دفعات الفاتورة كلها (ما فيها إرجاعات)
                        foreach (var x in sale.Lines) tally.NetStock[x.ProductId] += x.Qty;
                    }
                }
                else if (response.StatusCode != HttpStatusCode.UnprocessableEntity)
                {
                    Fail("إلغاء", response, body);
                }
            }
            else
            {
                var response = await client.PostAsJsonAsync("/api/v1/cash-drawer/open-events",
                    new { branchId = Fixture.TestBranchId, reason = "فكة", clientRequestId = Guid.NewGuid(), occurredAtUtc = (DateTime?)null });
                if (response.IsSuccessStatusCode) lock (tally.Gate) tally.DrawerOpens++;
                else Fail("فتح درج", response, await response.Content.ReadAsStringAsync());
            }
        }
    }
}
