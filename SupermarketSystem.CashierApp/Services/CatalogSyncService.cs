using Microsoft.EntityFrameworkCore;
using SupermarketSystem.CashierApp.Local;

namespace SupermarketSystem.CashierApp.Services;

/// <summary>
/// "رقم نسخة أولًا" — تسأل السيرفر بس "شو آخر نسخة؟" (خفيف جدًا)، ولو
/// نفس رقمك المحلي، صفر سحب. لو مختلف، تسحب الكتالوج الكامل بصفحات
/// وتخزّنها محليًا.
///
/// ذرّية (24/9/2026): كل الصفحات بتنسحب بالذاكرة أول، وبعدها رقم النسخة بينفحص مرة
/// تانية - لو تغيّر أثناء السحب (سعر اتعدّل بالنص)، بنعيد السحب. بس لما النسخة تثبت،
/// المنتجات ورقم النسخة بينكتبوا سوا بمعاملة SQLite وحدة. السبب: السيرفر بيحسب البيع
/// الأوفلاين بأسعار رقم النسخة اللي الكاشير بيبعته (CartLine.CatalogVersion)، فلازم
/// الرقم المخزَّن يطابق الأسعار المخزَّنة بالضبط - صفحات نص محدَّثة (انقطاع بالنص) برقم
/// نسخة قديم كانت رح تخلي البيع ينرفض. انقطاع بالنص = ولا إشي بيتغيّر محليًا، والمحاولة
/// الجاية بتعيد من الصفحة 1 (نفس قرار "لا استئناف" الموثَّق بـCLAUDE.md).
/// </summary>
public sealed class CatalogSyncService
{
    private readonly string _dbPath;
    private readonly ApiClient _apiClient;
    private readonly int _pageSize;
    private const int MaxVersionChangeRetries = 3;

    public CatalogSyncService(string dbPath, ApiClient apiClient, int pageSize)
    {
        _dbPath = dbPath;
        _apiClient = apiClient;
        _pageSize = pageSize;
    }

    public async Task<CatalogSyncResult> SyncIfNeededAsync(Guid branchId, CancellationToken cancellationToken)
    {
        var remoteVersion = await _apiClient.GetCatalogVersionAsync(cancellationToken);
        if (remoteVersion is null)
        {
            return CatalogSyncResult.ConnectionFailed();
        }

        using var db = new LocalDbContext(_dbPath);
        var state = await db.SyncStates.FirstOrDefaultAsync(cancellationToken);

        if (state is not null && state.LastSyncedCatalogVersion == remoteVersion.Value)
        {
            return CatalogSyncResult.AlreadyUpToDate(remoteVersion.Value);
        }

        var version = remoteVersion.Value;

        for (var attempt = 1; attempt <= MaxVersionChangeRetries; attempt++)
        {
            var products = new List<CatalogSyncProductDto>();
            var pageNumber = 1;

            while (true)
            {
                var page = await _apiClient.GetCatalogSyncPageAsync(branchId, pageNumber, _pageSize, cancellationToken);
                if (page is null)
                {
                    return CatalogSyncResult.PartialFailure(0);
                }

                products.AddRange(page.Items);

                var totalPages = (int)Math.Ceiling(page.TotalCount / (double)_pageSize);
                if (pageNumber >= totalPages || page.Items.Count == 0)
                {
                    break;
                }

                pageNumber++;
            }

            var versionAfterPaging = await _apiClient.GetCatalogVersionAsync(cancellationToken);
            if (versionAfterPaging is null)
            {
                return CatalogSyncResult.ConnectionFailed();
            }

            if (versionAfterPaging.Value != version)
            {
                // الكتالوج تغيّر أثناء السحب - الصفحات ممكن تكون خليط نسختين.
                version = versionAfterPaging.Value;
                continue;
            }

            await using (var transaction = await db.Database.BeginTransactionAsync(cancellationToken))
            {
                ApplyPageToLocalDb(db, products);

                if (state is null)
                {
                    state = new SyncState { LastSyncedCatalogVersion = version, LastSuccessfulSyncAtLocal = DateTime.UtcNow };
                    db.SyncStates.Add(state);
                }
                else
                {
                    state.LastSyncedCatalogVersion = version;
                    state.LastSuccessfulSyncAtLocal = DateTime.UtcNow;
                }

                await db.SaveChangesAsync(cancellationToken);
                await transaction.CommitAsync(cancellationToken);
            }

            return CatalogSyncResult.Completed(version, products.Count);
        }

        // الكتالوج ضل يتغيّر بكل محاولة - بنجرّب بالدورة الجاية للمزامنة الخلفية.
        return CatalogSyncResult.PartialFailure(0);
    }

    /// <summary>استبدال كامل لكل منتج بالصفحة - أبسط من تعديل جزئي، وصحيح دائمًا.</summary>
    private static void ApplyPageToLocalDb(LocalDbContext db, IReadOnlyList<CatalogSyncProductDto> products)
    {
        foreach (var product in products)
        {
            var existingUnits = db.ProductUnits.Where(u => u.ProductId == product.ProductId).ToList();
            var existingUnitIds = existingUnits.Select(u => u.UnitId).ToList();
            db.ProductBarcodes.RemoveRange(db.ProductBarcodes.Where(b => existingUnitIds.Contains(b.ProductUnitId)));
            db.ProductUnits.RemoveRange(existingUnits);
            db.ProductBatches.RemoveRange(db.ProductBatches.Where(b => b.ProductId == product.ProductId));

            var existingProduct = db.Products.Find(product.ProductId);
            if (existingProduct is null)
            {
                db.Products.Add(new LocalProduct
                {
                    ProductId = product.ProductId,
                    Name = product.Name,
                    CategoryId = product.CategoryId,
                    CategoryName = product.CategoryName,
                    SellingPrice = product.SellingPrice,
                    IsAvailableForSale = product.IsAvailableForSale,
                    IsBatchTracked = product.IsBatchTracked
                });
            }
            else
            {
                existingProduct.Name = product.Name;
                existingProduct.CategoryId = product.CategoryId;
                existingProduct.CategoryName = product.CategoryName;
                existingProduct.SellingPrice = product.SellingPrice;
                existingProduct.IsAvailableForSale = product.IsAvailableForSale;
                existingProduct.IsBatchTracked = product.IsBatchTracked;
            }

            foreach (var unit in product.Units)
            {
                db.ProductUnits.Add(new LocalProductUnit
                {
                    UnitId = unit.UnitId,
                    ProductId = product.ProductId,
                    UnitName = unit.UnitName,
                    ConversionFactorToBase = unit.ConversionFactorToBase,
                    IsBaseUnit = unit.IsBaseUnit
                });

                foreach (var barcode in unit.Barcodes)
                {
                    db.ProductBarcodes.Add(new LocalProductBarcode { BarcodeValue = barcode, ProductUnitId = unit.UnitId });
                }
            }

            foreach (var batch in product.Batches)
            {
                db.ProductBatches.Add(new LocalProductBatch
                {
                    BatchId = batch.BatchId,
                    ProductId = product.ProductId,
                    BatchNumber = batch.BatchNumber,
                    ExpiryDate = batch.ExpiryDate,
                    QuantityAvailable = batch.QuantityAvailable
                });
            }
        }

        db.SaveChanges();
    }
}

public sealed record CatalogSyncResult(CatalogSyncStatus Status, long? Version, int ProductsSynced)
{
    public static CatalogSyncResult ConnectionFailed() => new(CatalogSyncStatus.ConnectionFailed, null, 0);
    public static CatalogSyncResult AlreadyUpToDate(long version) => new(CatalogSyncStatus.AlreadyUpToDate, version, 0);
    public static CatalogSyncResult PartialFailure(int synced) => new(CatalogSyncStatus.PartialFailure, null, synced);
    public static CatalogSyncResult Completed(long version, int synced) => new(CatalogSyncStatus.Completed, version, synced);
}

public enum CatalogSyncStatus
{
    Completed,
    AlreadyUpToDate,
    ConnectionFailed,
    PartialFailure
}
