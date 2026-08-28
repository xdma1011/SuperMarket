using Microsoft.EntityFrameworkCore;
using SupermarketSystem.CashierApp.Local;

namespace SupermarketSystem.CashierApp.Services;

/// <summary>
/// "رقم نسخة أولًا" — تسأل السيرفر بس "شو آخر نسخة؟" (خفيف جدًا)، ولو
/// نفس رقمك المحلي، صفر سحب. لو مختلف، تسحب الكتالوج الكامل بصفحات
/// وتخزّنها محليًا.
///
/// كل صفحة ناجحة تُطبَّق فورًا على الجداول المحلية، بدل تجميع كل شي
/// بالذاكرة أول — لو انقطع النت بمنتصف السحب، الصفحات اللي نجحت قبل
/// الانقطاع محفوظة فعليًا. SyncState ما تُحدَّث لآخر نسخة إلا بعد نجاح
/// كل الصفحات، فمحاولة لاحقة بتعيد من الصفحة 1 - بسيط ومضمون، بثمن
/// إعادة سحب كامل بدل استئناف دقيق، وهو ثمن مقبول لحجم كتالوج ميني
/// ماركت عادي.
/// </summary>
public sealed class CatalogSyncService
{
    private readonly string _dbPath;
    private readonly ApiClient _apiClient;
    private readonly int _pageSize;

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

        var pageNumber = 1;
        var totalProductsSynced = 0;

        while (true)
        {
            var page = await _apiClient.GetCatalogSyncPageAsync(branchId, pageNumber, _pageSize, cancellationToken);
            if (page is null)
            {
                return CatalogSyncResult.PartialFailure(totalProductsSynced);
            }

            ApplyPageToLocalDb(db, page.Items);
            totalProductsSynced += page.Items.Count;

            var totalPages = (int)Math.Ceiling(page.TotalCount / (double)_pageSize);
            if (pageNumber >= totalPages || page.Items.Count == 0)
            {
                break;
            }

            pageNumber++;
        }

        if (state is null)
        {
            state = new SyncState { LastSyncedCatalogVersion = remoteVersion.Value, LastSuccessfulSyncAtLocal = DateTime.UtcNow };
            db.SyncStates.Add(state);
        }
        else
        {
            state.LastSyncedCatalogVersion = remoteVersion.Value;
            state.LastSuccessfulSyncAtLocal = DateTime.UtcNow;
        }

        await db.SaveChangesAsync(cancellationToken);

        return CatalogSyncResult.Completed(remoteVersion.Value, totalProductsSynced);
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
