using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using SupermarketSystem.CashierApp.Local;
using SupermarketSystem.CashierApp.Services;
using SupermarketSystem.CashierApp.Views;

namespace SupermarketSystem.CashierHeadlessTests.Support;

/// <summary>
/// "كاشير بلا شاشة": نفس كود الكاشير الفعلي (ApiClient، CatalogSyncService، PendingSaleSyncService، TrustedClock، PromotionPricing،
/// Local/*) بمجلد بيانات مؤقت (مش %LocalAppData% الحقيقي). اللي كان بالشاشة نفسها (SaleWindow) منسوخ هون سطر بسطر وبإشارة لمكانه:
/// AddSimpleItem/ReadPriceSnapshot/AttachPromotion وCompleteSaleAsync - أي تغيير هناك لازم ينعكس هون.
/// </summary>
public sealed class HeadlessCashier : IDisposable
{
    // نفس SaleWindow.JsonOptions.
    private static readonly JsonSerializerOptions JsonOptions = new() { PropertyNamingPolicy = JsonNamingPolicy.CamelCase };

    public HeadlessCashier(string apiBaseUrl, string label)
    {
        DataDirectory = Path.Combine(Path.GetTempPath(), "spkt-cashier-headless", $"{label}-{Guid.NewGuid():N}");
        Directory.CreateDirectory(DataDirectory);
        DbPath = Path.Combine(DataDirectory, "local.db");

        // نفس App.OnStartup: Migrate() على local.db جديدة.
        using (var db = new LocalDbContext(DbPath))
        {
            db.Database.Migrate();
        }

        TrustedClock.Instance.Load(DbPath);
        Api = new ApiClient(new AppConfig { ApiBaseUrl = apiBaseUrl, SyncIntervalSeconds = 60, CatalogSyncPageSize = 200 });
    }

    public string DataDirectory { get; }
    public string DbPath { get; }
    public ApiClient Api { get; }
    public Guid BranchId { get; private set; }

    public async Task LoginAsync(string username, string password)
    {
        var result = await Api.LoginAsync(username, password, CancellationToken.None);
        Assert.True(result.Success, result.ErrorMessage);
        Api.SetTokens(result.Response!.AccessToken, result.Response.RefreshToken);
        BranchId = result.Response.BranchId ?? throw new InvalidOperationException("الكاشير بلا فرع.");
    }

    /// <summary>دورة مزامنة زي BackgroundSyncService: وقت السيرفر (الساعة الموثوقة) + الكتالوج + إرسال الطابور.</summary>
    public async Task<(CatalogSyncResult Catalog, SyncSummary Queue)> SyncAsync()
    {
        var serverNow = await Api.GetServerUtcNowAsync(CancellationToken.None);
        if (serverNow is { } now)
        {
            await TrustedClock.Instance.RecordServerTimeAsync(DbPath, now, CancellationToken.None);
        }

        var catalog = await new CatalogSyncService(DbPath, Api, 200).SyncIfNeededAsync(BranchId, CancellationToken.None);
        var queue = await new PendingSaleSyncService(DbPath, Api).SyncPendingSalesAsync(CancellationToken.None);
        return (catalog, queue);
    }

    public Task<SyncSummary> FlushQueueAsync() =>
        new PendingSaleSyncService(DbPath, Api).SyncPendingSalesAsync(CancellationToken.None);

    public int PendingCount()
    {
        using var db = new LocalDbContext(DbPath);
        return db.PendingSales.Count();
    }

    public List<PendingSale> PendingSales()
    {
        using var db = new LocalDbContext(DbPath);
        return db.PendingSales.AsNoTracking().ToList();
    }

    /// <summary>نفس SaleWindow.AddScannedItem → AddSimpleItem: بحث محلي بالباركود، سعر الوحدة = سعر الأساسية × المعامل، العرض للأساسية بس.</summary>
    public void Scan(List<CartLine> cart, string barcode, decimal quantity = 1m)
    {
        using var db = new LocalDbContext(DbPath);
        var unitId = db.ProductBarcodes.Where(b => b.BarcodeValue == barcode).Select(b => (Guid?)b.ProductUnitId).FirstOrDefault()
            ?? throw new InvalidOperationException($"باركود مش موجود محليًا: {barcode}");
        var unit = db.ProductUnits.Single(u => u.UnitId == unitId);
        var product = db.Products.Single(p => p.ProductId == unit.ProductId);
        Assert.False(product.IsBatchTracked, "الكاشير بلا شاشة بيغطّي الأصناف العادية بس");

        var existingLine = cart.FirstOrDefault(l => l.ProductUnitId == unit.UnitId && l.ProductBatchId is null);
        if (existingLine is not null)
        {
            existingLine.Quantity += quantity;
            return;
        }

        var (baseUnitPrice, catalogVersion) = ReadPriceSnapshot(product.ProductId);
        var line = new CartLine
        {
            ProductId = product.ProductId,
            ProductUnitId = unit.UnitId,
            ProductName = product.Name,
            UnitName = unit.UnitName,
            Quantity = quantity,
            UnitPrice = baseUnitPrice * unit.ConversionFactorToBase,
            CatalogVersion = catalogVersion
        };
        AttachPromotion(line, unit);
        cart.Add(line);
    }

    private (decimal BaseUnitPrice, long? CatalogVersion) ReadPriceSnapshot(Guid productId)
    {
        using var db = new LocalDbContext(DbPath);
        using var transaction = db.Database.BeginTransaction();
        var baseUnitPrice = db.Products.Where(p => p.ProductId == productId).Select(p => p.SellingPrice).First();
        var catalogVersion = db.SyncStates.Select(s => (long?)s.LastSyncedCatalogVersion).FirstOrDefault();
        return (baseUnitPrice, catalogVersion);
    }

    private void AttachPromotion(CartLine line, LocalProductUnit unit)
    {
        if (!unit.IsBaseUnit)
        {
            return;
        }

        using var db = new LocalDbContext(DbPath);
        var promotion = PromotionPricing.ActiveFor(db, line.ProductId, TrustedClock.Instance.Now());
        if (promotion is null)
        {
            return;
        }

        line.PromotionId = promotion.PromotionId;
        line.PromotionTitle = promotion.Title;
        line.PromotionBundleQuantity = promotion.BundleQuantity;
        line.PromotionBundlePrice = promotion.BundlePrice;
        line.PromotionMaxQuantity = promotion.MaxQuantityPerInvoice;
    }

    /// <summary>
    /// نفس SaleWindow.CompleteSaleAsync: الطلب بشكل CompleteSaleCommand، حفظ محلي أول (PendingSale + ختم الساعة الموثوقة بنفس
    /// المعاملة)، محاولة إرسال فورية، نجاح = حذف من الطابور، فشل = يضل مع آخر خطأ.
    /// </summary>
    public async Task<CompletedLocalSale> CompleteSaleAsync(
        IReadOnlyList<CartLine> cart, Guid? paymentMethodId, Guid? creditCustomerId = null, decimal creditPaidNow = 0m, Guid? clientRequestId = null)
    {
        var total = cart.Sum(l => l.LineTotal);
        var requestId = clientRequestId ?? Guid.NewGuid();
        var paymentAmount = creditCustomerId is null ? total : creditPaidNow;
        var saleTimeUtc = TrustedClock.Instance.Now();

        var payload = new
        {
            branchId = BranchId,
            clientRequestId = requestId,
            occurredAtUtc = saleTimeUtc,
            customerId = creditCustomerId,
            allowCreditSale = creditCustomerId is not null,
            invoiceLevelDiscountAmount = 0m,
            items = cart.Select(l => new
            {
                productId = l.ProductId,
                productUnitId = l.ProductUnitId,
                quantity = l.Quantity,
                manualDiscountAmount = 0m,
                productBatchId = l.ProductBatchId,
                catalogVersion = l.CatalogVersion,
                promotionId = l.AppliedPromotionId
            }),
            payments = new[]
            {
                new
                {
                    paymentMethodId = paymentMethodId ?? Guid.Empty,
                    amount = paymentAmount,
                    externalReference = (string?)null,
                    clientRequestId = Guid.NewGuid()
                }
            }.Where(p => p.amount > 0).ToArray(),
            preparedOrderId = (Guid?)null,
            customerPhone = (string?)null
        };

        var pendingSale = new PendingSale
        {
            ClientRequestId = requestId,
            BranchId = BranchId,
            RequestPayloadJson = JsonSerializer.Serialize(payload, JsonOptions),
            CreatedAtLocal = saleTimeUtc,
            AttemptCount = 0
        };

        using (var db = new LocalDbContext(DbPath))
        {
            db.PendingSales.Add(pendingSale);
            TrustedClock.Instance.Stamp(db);
            await db.SaveChangesAsync();
        }

        var sendResult = await Api.SendPendingSaleAsync(pendingSale, CancellationToken.None);

        using (var db = new LocalDbContext(DbPath))
        {
            var savedRow = await db.PendingSales.FirstOrDefaultAsync(s => s.ClientRequestId == requestId);
            if (savedRow is not null)
            {
                if (sendResult.Success)
                {
                    db.PendingSales.Remove(savedRow);
                }
                else
                {
                    savedRow.AttemptCount += 1;
                    savedRow.LastAttemptAtLocal = DateTime.UtcNow;
                    savedRow.LastErrorMessage = sendResult.ErrorMessage;
                }

                await db.SaveChangesAsync();
            }
        }

        return new CompletedLocalSale(requestId, total, paymentAmount, saleTimeUtc, sendResult.Success, sendResult.ErrorMessage,
            cart.Select(l => (l.ProductUnitId, l.Quantity)).ToList());
    }

    public void Dispose()
    {
        try
        {
            Microsoft.Data.Sqlite.SqliteConnection.ClearAllPools();
            Directory.Delete(DataDirectory, recursive: true);
        }
        catch (IOException)
        {
            // مجلد مؤقت - ما بنوقف الاختبار عشانه.
        }
        catch (UnauthorizedAccessException)
        {
        }
    }
}

public sealed record CompletedLocalSale(
    Guid ClientRequestId, decimal LocalTotal, decimal PaidAmount, DateTime OccurredAtUtc, bool SentImmediately, string? FirstError,
    List<(Guid UnitId, decimal Quantity)> Lines);
