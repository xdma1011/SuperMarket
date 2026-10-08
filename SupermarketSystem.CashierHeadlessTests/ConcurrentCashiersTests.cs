using System.Net;
using System.Net.Http.Json;
using System.Text;
using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using SupermarketSystem.Application.Common.Interfaces;
using SupermarketSystem.CashierApp.Local;
using SupermarketSystem.CashierApp.Services;
using SupermarketSystem.CashierApp.Views;
using SupermarketSystem.CashierHeadlessTests.Support;
using SupermarketSystem.Infrastructure.Persistence;
using SupermarketSystem.IntegrationTests;
using SupermarketSystem.IntegrationTests.Common;

namespace SupermarketSystem.CashierHeadlessTests;

/// <summary>
/// بند 4 من "خطة اختبار آلي موسّع": كاشيرات كتير بنفس اللحظة (كود الكاشير الفعلي، API على Kestrel حقيقي، SQL Server حقيقي).
/// كل كاشير بقاعدته المحلية ومستخدمه. الطلبات بتنطلق سوا بحاجز (Barrier) عشان تتصادم فعلًا بالسيرفر.
/// قبل هالملف ما كان في ولا اختبار تزامن بالمستودع (grep على Parallel/Task.WhenAll = صفر).
/// </summary>
[Collection(CashierDatabaseCollection.Name)]
public sealed class ConcurrentCashiersTests : IntegrationTestBase
{
    public ConcurrentCashiersTests(DatabaseFixture fixture) : base(fixture) { }

    private static readonly Guid Cash = TestDataBuilder.CashPaymentMethodId;

    private async Task<List<HeadlessCashier>> StartCashiersAsync(LiveApi api, Guid branchId, int count, string prefix)
    {
        var cashiers = new List<HeadlessCashier>();
        for (var i = 0; i < count; i++)
        {
            var name = await StoreSeed.CreateCashierAsync(Fixture, branchId, $"{prefix}{i}");
            var cashier = new HeadlessCashier(api.BaseUrl, $"{prefix}{i}");
            await cashier.LoginAsync(name, StoreSeed.CashierPassword);
            var sync = await cashier.SyncAsync();
            Assert.Equal(CatalogSyncStatus.Completed, sync.Catalog.Status);
            cashiers.Add(cashier);
        }

        return cashiers;
    }

    /// <summary>كل المهام بتستنى بعض وبعدين بتنطلق بنفس اللحظة.</summary>
    private static async Task<T[]> AllAtOnceAsync<T>(int count, Func<int, Task<T>> work)
    {
        using var gate = new SemaphoreSlim(0, count);
        var tasks = Enumerable.Range(0, count).Select(i => Task.Run(async () =>
        {
            await gate.WaitAsync();
            return await work(i);
        })).ToArray();
        await Task.Delay(200);
        gate.Release(count);
        return await Task.WhenAll(tasks);
    }

    [Fact]
    public async Task سباق_على_آخر_قطعة_بالمخزون_بلا_رصيد_سالب_واحد_بس_بيبيع()
    {
        var branchId = await StoreSeed.CreateBranchAsync(Fixture, "HDL-RACE");
        var item = await StoreSeed.CreateItemAsync(Fixture, branchId, "آخر قطعة", price: 2.000m, stock: 1m);
        using var api = new LiveApi(Fixture.Factory.ConnectionString);
        api.Start();
        var cashiers = await StartCashiersAsync(api, branchId, 15, "race");

        using (var scope = CreateScope())
        {
            await TestDataBuilder.SetSettingAsync(scope, InventorySettingsKeys.AllowNegativeStock, false);
        }

        try
        {
            var results = await AllAtOnceAsync(cashiers.Count, async i =>
            {
                var cart = new List<CartLine>();
                cashiers[i].Scan(cart, item.BaseBarcode);
                return await cashiers[i].CompleteSaleAsync(cart, Cash);
            });

            Assert.Equal(1, results.Count(r => r.SentImmediately));
            Assert.All(results.Where(r => !r.SentImmediately), r => Assert.Contains("مخزون", r.FirstError ?? "", StringComparison.Ordinal));
            Assert.Equal(0m, await StoreSeed.GetStockAsync(Fixture, item.ProductId, branchId));

            using var verify = CreateScope();
            var db = verify.ServiceProvider.GetRequiredService<AppDbContext>();
            Assert.Equal(1, await db.SaleInvoices.IgnoreQueryFilters().CountAsync(i => i.BranchId == branchId));
        }
        finally
        {
            using var scope = CreateScope();
            await TestDataBuilder.SetSettingAsync(scope, InventorySettingsKeys.AllowNegativeStock, true);
            cashiers.ForEach(c => c.Dispose());
        }
    }

    [Fact]
    public async Task عشرين_كاشير_بنفس_اللحظة_بنفس_الفرع_أرقام_فواتير_فريدة_ومتسلسلة_وما_في_بيعة_ضايعة()
    {
        var branchId = await StoreSeed.CreateBranchAsync(Fixture, "HDL-SEQ");
        var item = await StoreSeed.CreateItemAsync(Fixture, branchId, "صنف ترقيم", price: 0.500m, stock: 10_000m);
        using var api = new LiveApi(Fixture.Factory.ConnectionString);
        api.Start();
        var cashiers = await StartCashiersAsync(api, branchId, 20, "seq");

        try
        {
            const int salesPerCashier = 5;
            var results = await AllAtOnceAsync(cashiers.Count, async i =>
            {
                var mine = new List<CompletedLocalSale>();
                for (var s = 0; s < salesPerCashier; s++)
                {
                    var cart = new List<CartLine>();
                    cashiers[i].Scan(cart, item.BaseBarcode, 1 + s % 3);
                    mine.Add(await cashiers[i].CompleteSaleAsync(cart, Cash));
                }

                return mine;
            });

            var all = results.SelectMany(r => r).ToList();
            var failures = all.Where(s => !s.SentImmediately).Select(s => s.FirstError).ToList();
            Assert.True(failures.Count == 0, "بيعات انرفضت تحت التزامن: " + string.Join(" | ", failures.Distinct()));

            using var scope = CreateScope();
            var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
            var invoices = await db.SaleInvoices.IgnoreQueryFilters().AsNoTracking().Where(i => i.BranchId == branchId).ToListAsync();
            Assert.Equal(all.Count, invoices.Count);
            Assert.Equal(invoices.Count, invoices.Select(i => i.InvoiceNumber).Distinct().Count());
            Assert.Equal(all.Count, invoices.Select(i => i.ClientRequestId).Intersect(all.Select(s => s.ClientRequestId)).Count());

            var expectedSold = all.Sum(s => s.Lines.Sum(l => l.Quantity));
            Assert.Equal(10_000m - expectedSold, await StoreSeed.GetStockAsync(Fixture, item.ProductId, branchId));
            Assert.Equal(all.Sum(s => s.LocalTotal), invoices.Sum(i => i.TotalAmount));
        }
        finally
        {
            cashiers.ForEach(c => c.Dispose());
        }
    }

    [Fact]
    public async Task نفس_الـClientRequestId_من_عشر_اتصالات_بنفس_اللحظة_بيعة_وحدة_بس_وبلا_خطأ_سيرفر()
    {
        var branchId = await StoreSeed.CreateBranchAsync(Fixture, "HDL-IDEM");
        var item = await StoreSeed.CreateItemAsync(Fixture, branchId, "صنف تكرار", price: 1.000m, stock: 100m);
        using var api = new LiveApi(Fixture.Factory.ConnectionString);
        api.Start();
        var cashiers = await StartCashiersAsync(api, branchId, 1, "idem");
        var cashier = cashiers[0];

        try
        {
            // البيعة محفوظة محليًا (نفس الطابور)، والمزامنة الخلفية + زر "مزامنة الآن" + إعادة محاولة بعتوها سوا.
            var cart = new List<CartLine>();
            cashier.Scan(cart, item.BaseBarcode, 3m);
            var requestId = Guid.NewGuid();
            var payload = JsonSerializer.Serialize(new
            {
                branchId, clientRequestId = requestId, occurredAtUtc = TrustedClock.Instance.Now(), customerId = (Guid?)null,
                allowCreditSale = false, invoiceLevelDiscountAmount = 0m,
                items = cart.Select(l => new { productId = l.ProductId, productUnitId = l.ProductUnitId, quantity = l.Quantity,
                    manualDiscountAmount = 0m, productBatchId = (Guid?)null, catalogVersion = l.CatalogVersion, promotionId = (Guid?)null }),
                payments = new[] { new { paymentMethodId = Cash, amount = 3.000m, externalReference = (string?)null, clientRequestId = Guid.NewGuid() } }
            }, new JsonSerializerOptions { PropertyNamingPolicy = JsonNamingPolicy.CamelCase });

            using var http = new HttpClient { BaseAddress = new Uri(api.BaseUrl.TrimEnd('/') + "/") };
            http.DefaultRequestHeaders.Authorization = new System.Net.Http.Headers.AuthenticationHeaderValue("Bearer", cashier.Api.AccessToken);

            var responses = await AllAtOnceAsync(10, async _ =>
            {
                var response = await http.PostAsync("sales", new StringContent(payload, Encoding.UTF8, "application/json"));
                return (response.StatusCode, Body: await response.Content.ReadAsStringAsync());
            });

            Assert.DoesNotContain(responses, r => (int)r.StatusCode >= 500);
            Assert.Contains(responses, r => r.StatusCode == HttpStatusCode.OK || r.StatusCode == HttpStatusCode.Created);

            // اللي ما نجح (لو في) لازم ينجح بإعادة الإرسال (WasReplay) - زي ما الطابور بيعمل.
            var retry = await cashier.Api.SendPendingSaleAsync(
                new PendingSale { ClientRequestId = requestId, BranchId = branchId, RequestPayloadJson = payload }, CancellationToken.None);
            Assert.True(retry.Success, retry.ErrorMessage);

            using var scope = CreateScope();
            var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
            Assert.Equal(1, await db.SaleInvoices.IgnoreQueryFilters().CountAsync(i => i.ClientRequestId == requestId));
            Assert.Equal(97m, await StoreSeed.GetStockAsync(Fixture, item.ProductId, branchId));
            Assert.Equal(1, await db.SaleInvoicePayments.IgnoreQueryFilters().CountAsync(p => p.BranchId == branchId));
        }
        finally
        {
            cashiers.ForEach(c => c.Dispose());
        }
    }

    [Fact]
    public async Task عشر_تقفيلات_متزامنة_لنفس_الفرع_والتاريخ_بلا_خطأ_سيرفر_وبلا_ورديات_مكررة()
    {
        var branchId = await StoreSeed.CreateBranchAsync(Fixture, "HDL-CLOSE");
        var item = await StoreSeed.CreateItemAsync(Fixture, branchId, "صنف تقفيل", price: 1.000m, stock: 100m);
        using var api = new LiveApi(Fixture.Factory.ConnectionString);
        api.Start();
        var cashiers = await StartCashiersAsync(api, branchId, 10, "close");

        try
        {
            var cart = new List<CartLine>();
            cashiers[0].Scan(cart, item.BaseBarcode, 5m);
            Assert.True((await cashiers[0].CompleteSaleAsync(cart, Cash)).SentImmediately);

            var date = DateOnly.FromDateTime(TrustedClock.Instance.LocalNow());
            var results = await AllAtOnceAsync(cashiers.Count, i => cashiers[i].Api.CompleteCashClosingAsync(
                branchId, date, 5.000m, new List<CompleteCashClosingCountedDetailDto> { new(Cash, 5.000m) }, CancellationToken.None));

            // رفض متوقع لبعضهم (تعارض)، بس لازم يكون رسالة واضحة مش 500/استثناء SQL خام.
            var failures = results.Where(r => !r.Success).Select(r => r.ErrorMessage ?? "").ToList();
            Assert.DoesNotContain(failures, f => f.Contains("Internal", StringComparison.OrdinalIgnoreCase)
                                                 || f.Contains("خطأ غير متوقع", StringComparison.Ordinal)
                                                 || f.Contains("500", StringComparison.Ordinal));
            Assert.Contains(results, r => r.Success);

            using var scope = CreateScope();
            var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
            var closings = await db.CashClosings.IgnoreQueryFilters().AsNoTracking()
                .Where(c => c.BranchId == branchId && c.BusinessDate == date).ToListAsync();
            Assert.Equal(results.Count(r => r.Success), closings.Count);
            Assert.Equal(closings.Count, closings.Select(c => c.ShiftNumber).Distinct().Count());
            // الكاش اتعدّ مرة وحدة بس كمتوقع (أول وردية)؛ الورديات اللي بعدها متوقعها صفر.
            Assert.Equal(5.000m, closings.Sum(c => c.ExpectedCash));
        }
        finally
        {
            cashiers.ForEach(c => c.Dispose());
        }
    }

    [Fact]
    public async Task نفس_المستخدم_بيدخل_من_جهازين_بنفس_اللحظة_بتضل_جلسة_وحدة_فعّالة_بس()
    {
        var branchId = await StoreSeed.CreateBranchAsync(Fixture, "HDL-SESS");
        await StoreSeed.CreateItemAsync(Fixture, branchId, "صنف جلسات", price: 1.000m, stock: 10m);
        var username = await StoreSeed.CreateCashierAsync(Fixture, branchId, "sess");
        using var api = new LiveApi(Fixture.Factory.ConnectionString);
        api.Start();

        var devices = Enumerable.Range(0, 6).Select(i => new HeadlessCashier(api.BaseUrl, $"sess{i}")).ToList();
        try
        {
            var logins = await AllAtOnceAsync(devices.Count, i => devices[i].Api.LoginAsync(username, StoreSeed.CashierPassword, CancellationToken.None));
            Assert.All(logins, l => Assert.True(l.Success, l.ErrorMessage));

            using var scope = CreateScope();
            var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
            var userId = logins[0].Response!.UserId;
            var sessions = await db.UserSessions.IgnoreQueryFilters().AsNoTracking()
                .Where(s => s.UserId == userId).OrderBy(s => s.CreatedAtUtc).ToListAsync();
            var active = sessions.Count(s => s.RevokedAtUtc == null && s.ExpiresAtUtc > DateTime.UtcNow);
            Assert.True(active == 1, $"جلسات فعّالة: {active}\n" + string.Join("\n", sessions.Select(s =>
                $"{s.Id} created={s.CreatedAtUtc:HH:mm:ss.fffffff} revoked={s.RevokedAtUtc:HH:mm:ss.fffffff} reason={s.RevocationReason}")));

            // وبس توكن وحدة لسه بتشتغل (الطرد الفوري بيفحص الجلسة بكل طلب).
            var working = 0;
            for (var i = 0; i < devices.Count; i++)
            {
                devices[i].Api.SetTokens(logins[i].Response!.AccessToken, logins[i].Response!.RefreshToken);
                if (await devices[i].Api.GetCatalogVersionAsync(CancellationToken.None) is not null)
                {
                    working++;
                }
            }

            Assert.Equal(1, working);
        }
        finally
        {
            devices.ForEach(d => d.Dispose());
        }
    }
}
