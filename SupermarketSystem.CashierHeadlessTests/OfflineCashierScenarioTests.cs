using System.Net.Http.Json;
using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using SupermarketSystem.Application.Catalog.CreatePromotion;
using SupermarketSystem.CashierApp.Services;
using SupermarketSystem.CashierApp.Views;
using SupermarketSystem.CashierHeadlessTests.Support;
using SupermarketSystem.Infrastructure.Persistence;
using SupermarketSystem.IntegrationTests;
using SupermarketSystem.IntegrationTests.Common;

namespace SupermarketSystem.CashierHeadlessTests;

/// <summary>
/// بند 2 من "خطة اختبار آلي موسّع" (8/10/2026): كاشير بكوده الفعلي بلا شاشات، والـAPI بينطفي فعلًا بالنص.
/// بيع أونلاين ← السيرفر بيطفي ← بيعات بالطابور (كرتونة، عرض كمية، صنف سعره تغيّر وقت الانقطاع، بيع بالدين) ← السيرفر بيرجع ←
/// كل بيعة وصلت مرة وحدة بالسعر اللي الزبون دفعه، وقت الفاتورة = وقت البيع، المخزون صح، والتقفيل بلا فرق.
/// </summary>
[Collection(CashierDatabaseCollection.Name)]
public sealed class OfflineCashierScenarioTests : IntegrationTestBase
{
    public OfflineCashierScenarioTests(DatabaseFixture fixture) : base(fixture) { }

    private static readonly Guid Cash = TestDataBuilder.CashPaymentMethodId;

    [Fact]
    public async Task بيع_أونلاين_ثم_انقطاع_وبيعات_بالطابور_ثم_رجوع_كل_بيعة_مرة_وحدة_بسعرها_ووقتها_والتقفيل_مطابق()
    {
        var branchId = await StoreSeed.CreateBranchAsync(Fixture, "HDL-OFF");
        var a = await StoreSeed.CreateItemAsync(Fixture, branchId, "صنف كرتونة", price: 1.250m, stock: 100m, cartonFactor: 12m);
        var b = await StoreSeed.CreateItemAsync(Fixture, branchId, "صنف عرض", price: 1.000m, stock: 100m);
        var c = await StoreSeed.CreateItemAsync(Fixture, branchId, "صنف سعره بيتغيّر", price: 0.750m, stock: 100m);

        Guid customerId;
        const string customerPhone = "0791112233";
        using (var scope = CreateScope())
        {
            await TestDataBuilder.ActAsAdminAsync(scope, Fixture);
            var db = CreateDbContext(scope);
            customerId = (await TestDataBuilder.CreateCustomerAsync(db, "زبون الدفتر", customerPhone)).Id;
            var promo = await scope.ServiceProvider.GetRequiredService<CreatePromotionHandler>().HandleAsync(new CreatePromotionCommand(
                b.ProductId, "3 بـ2.500", 3, 2.500m, null, DateTime.UtcNow.AddDays(-1), DateTime.UtcNow.AddDays(5), new[] { branchId }),
                CancellationToken.None);
            Assert.True(promo.IsSuccess, promo.Error?.Message);
        }

        var cashierName = await StoreSeed.CreateCashierAsync(Fixture, branchId, "hdl.off");
        using var api = new LiveApi(Fixture.Factory.ConnectionString);
        api.Start();
        using var cashier = new HeadlessCashier(api.BaseUrl, "offline");
        await cashier.LoginAsync(cashierName, StoreSeed.CashierPassword);
        var firstSync = await cashier.SyncAsync();
        Assert.Equal(CatalogSyncStatus.Completed, firstSync.Catalog.Status);
        Assert.True(TrustedClock.Instance.HasServerReference);

        var sales = new List<CompletedLocalSale>();

        // 1) أونلاين عادي.
        var cart = new List<CartLine>();
        cashier.Scan(cart, a.BaseBarcode, 2m);
        var online = await cashier.CompleteSaleAsync(cart, Cash);
        Assert.True(online.SentImmediately, online.FirstError);
        Assert.Equal(2.500m, online.LocalTotal);
        sales.Add(online);

        // الكاشير تأكد من زبون الدفتر وهو أونلاين (البحث أونلاين بس)، وبعدين النت قطع.
        var (creditCustomer, lookupError) = await cashier.Api.LookupCreditCustomerAsync(customerPhone, CancellationToken.None);
        Assert.True(creditCustomer is not null, lookupError);

        api.Stop();

        // 2) كرتونة + حبة (12 + 1).
        cart = new List<CartLine>();
        cashier.Scan(cart, a.CartonBarcode!);
        cashier.Scan(cart, a.BaseBarcode);
        var cartonSale = await cashier.CompleteSaleAsync(cart, Cash);
        Assert.False(cartonSale.SentImmediately);
        Assert.Equal(15.000m + 1.250m, cartonSale.LocalTotal);
        sales.Add(cartonSale);

        // 3) عرض "3 بـ2.500" على 4 حبات = 2.500 + 1.000.
        cart = new List<CartLine>();
        cashier.Scan(cart, b.BaseBarcode, 4m);
        var promoSale = await cashier.CompleteSaleAsync(cart, Cash);
        Assert.Equal(3.500m, promoSale.LocalTotal);
        sales.Add(promoSale);

        // 4) الإدارة غيّرت سعر C وقت الانقطاع (السيرفر الإداري شغّال، الكاشير مقطوع) - الكاشير لسه بالسعر القديم.
        var admin = await CreateAuthenticatedClientAsync();
        var priceChange = await admin.PostAsJsonAsync(
            $"/api/v1/products/{c.ProductId}/branches/{c.ProductBranchId}/price-change-requests", new { requestedPrice = 0.900m });
        Assert.True(priceChange.IsSuccessStatusCode, await priceChange.Content.ReadAsStringAsync());
        cart = new List<CartLine>();
        cashier.Scan(cart, c.BaseBarcode, 3m);
        var oldPriceSale = await cashier.CompleteSaleAsync(cart, Cash);
        Assert.Equal(2.250m, oldPriceSale.LocalTotal);
        sales.Add(oldPriceSale);

        // 5) بيع بالدين أوفلاين: 2 × 1.250 = 2.500، دفع منها 1.000.
        cart = new List<CartLine>();
        cashier.Scan(cart, a.BaseBarcode, 2m);
        var creditSale = await cashier.CompleteSaleAsync(cart, Cash, creditCustomerId: creditCustomer!.CustomerId, creditPaidNow: 1.000m);
        sales.Add(creditSale);

        // المزامنة الخلفية بتجرّب وهو مقطوع: ولا وحدة بتنبعت، كلهم بيضلوا.
        var offlineFlush = await cashier.FlushQueueAsync();
        Assert.Equal(0, offlineFlush.Sent);
        Assert.Equal(4, cashier.PendingCount());
        var (pendingCount, pendingAmount) = await PendingQueueSummary.ComputeAsync(cashier.DbPath, CancellationToken.None);
        Assert.Equal(4, pendingCount);
        Assert.Equal(16.250m + 3.500m + 2.250m + 1.000m, pendingAmount);

        // فرق واضح بين وقت البيع ووقت الوصول.
        await Task.Delay(TimeSpan.FromSeconds(3));
        api.Start();

        var backOnline = await cashier.SyncAsync();
        Assert.Equal(4, backOnline.Queue.Sent);
        Assert.Equal(0, cashier.PendingCount());
        Assert.Equal(CatalogSyncStatus.Completed, backOnline.Catalog.Status);

        // 6) بعد المزامنة: السعر الجديد للبيعات الجاية.
        cart = new List<CartLine>();
        cashier.Scan(cart, c.BaseBarcode);
        var newPriceSale = await cashier.CompleteSaleAsync(cart, Cash);
        Assert.True(newPriceSale.SentImmediately, newPriceSale.FirstError);
        Assert.Equal(0.900m, newPriceSale.LocalTotal);
        sales.Add(newPriceSale);

        // 7) الرد ضاع وانبعتت نفس البيعة مرة تانية (نفس ClientRequestId) - ما بتتكرر.
        var replayPayload = JsonSerializer.Serialize(new
        {
            branchId, clientRequestId = promoSale.ClientRequestId, occurredAtUtc = promoSale.OccurredAtUtc,
            customerId = (Guid?)null, allowCreditSale = false, invoiceLevelDiscountAmount = 0m,
            items = new[] { new { productId = b.ProductId, productUnitId = b.BaseUnitId, quantity = 4m, manualDiscountAmount = 0m,
                productBatchId = (Guid?)null, catalogVersion = (long?)null, promotionId = (Guid?)null } },
            payments = new[] { new { paymentMethodId = Cash, amount = 3.500m, externalReference = (string?)null, clientRequestId = Guid.NewGuid() } }
        }, new JsonSerializerOptions { PropertyNamingPolicy = JsonNamingPolicy.CamelCase });
        var replay = await cashier.Api.SendPendingSaleAsync(
            new CashierApp.Local.PendingSale { ClientRequestId = promoSale.ClientRequestId, BranchId = branchId, RequestPayloadJson = replayPayload },
            CancellationToken.None);
        Assert.True(replay.Success, replay.ErrorMessage);

        // === التحقق من السيرفر ===
        using (var scope = CreateScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
            var ids = sales.Select(s => s.ClientRequestId).ToList();
            var invoices = await db.SaleInvoices.IgnoreQueryFilters().AsNoTracking()
                .Where(i => i.BranchId == branchId).ToListAsync();
            Assert.Equal(sales.Count, invoices.Count);

            foreach (var sale in sales)
            {
                var invoice = Assert.Single(invoices, i => i.ClientRequestId == sale.ClientRequestId);
                Assert.Equal(sale.LocalTotal, invoice.TotalAmount);
                Assert.Equal(sale.PaidAmount, invoice.TotalPaidAmount);
                // وقت الفاتورة = وقت البيع عند الكاشير (ساعة موثوقة)، مش وقت الوصول - إلا لو ختم الكاشير "بالمستقبل" شوي، فالسيرفر
                // بيقصّه لوقت الوصول (SaleTimeRules). ملاحظة (8/10/2026): بعد إعادة تشغيل الـAPI أول طلب وقت بطيء (JIT/تسخين)،
                // وتصحيح نص زمن الرحلة بيقدّم ساعة الكاشير ~1-2 ثانية لحد الدورة الجاية - مسجَّل بتقرير الاختبار.
                Assert.NotNull(invoice.ReceivedAtUtc);
                var expected = sale.OccurredAtUtc > invoice.ReceivedAtUtc!.Value ? invoice.ReceivedAtUtc.Value : sale.OccurredAtUtc;
                Assert.True(Math.Abs((invoice.CreatedAtUtc - expected).TotalMilliseconds) < 50,
                    $"{invoice.InvoiceNumber}: CreatedAtUtc={invoice.CreatedAtUtc:O} OccurredAt={sale.OccurredAtUtc:O} Received={invoice.ReceivedAtUtc:O}");
            }

            foreach (var queued in new[] { cartonSale, promoSale, oldPriceSale, creditSale })
            {
                var invoice = invoices.Single(i => i.ClientRequestId == queued.ClientRequestId);
                Assert.Equal(queued.OccurredAtUtc, invoice.CreatedAtUtc, TimeSpan.FromMilliseconds(50));
                Assert.True((invoice.ReceivedAtUtc!.Value - invoice.CreatedAtUtc).TotalSeconds >= 2,
                    $"البيعة الأوفلاين لازم توصل بعد وقتها: Created={invoice.CreatedAtUtc:O} Received={invoice.ReceivedAtUtc:O}");
            }

            Assert.Equal(1.500m, invoices.Single(i => i.ClientRequestId == creditSale.ClientRequestId).TotalAmount
                                 - invoices.Single(i => i.ClientRequestId == creditSale.ClientRequestId).TotalPaidAmount);
            Assert.Equal(customerId, invoices.Single(i => i.ClientRequestId == creditSale.ClientRequestId).CustomerId);
        }

        Assert.Equal(100m - 2m - 13m - 2m, await StoreSeed.GetStockAsync(Fixture, a.ProductId, branchId));
        Assert.Equal(96m, await StoreSeed.GetStockAsync(Fixture, b.ProductId, branchId));
        Assert.Equal(96m, await StoreSeed.GetStockAsync(Fixture, c.ProductId, branchId));

        // === التقفيل: الكاش المعدود = كل اللي الزبائن دفعوه كاش ===
        var countedCash = sales.Sum(s => s.PaidAmount);
        var closing = await cashier.Api.CompleteCashClosingAsync(
            branchId, DateOnly.FromDateTime(TrustedClock.Instance.LocalNow()), countedCash,
            new List<CompleteCashClosingCountedDetailDto> { new(Cash, countedCash) }, CancellationToken.None);
        Assert.True(closing.Success, closing.ErrorMessage);
        Assert.Equal(countedCash, closing.Response!.ExpectedCash);
        Assert.Equal(0m, closing.Response.Variance);
    }
}
