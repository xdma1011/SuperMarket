using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using SupermarketSystem.Application.Common.Results;
using SupermarketSystem.Application.Sales.CompleteSale;
using SupermarketSystem.IntegrationTests.Common;
using System.Text.Json;
using Xunit;

namespace SupermarketSystem.IntegrationTests.CashierSync;

/// <summary>
/// تغطية Backend لمنتجات الدفعات (Batch) أوفلاين - راجع CLAUDE.md
/// "دعم منتجات الدفعات أوفلاين: مبني، بس محتاج تجربة فعلية" (ما كان
/// فيه ولا اختبار integration واحد يغطي هالمسار كاملًا قبل هالملف).
/// تطبيق الكاشير (WPF) نفسه ما ينفحص هون - Windows-only، بلا بيئة
/// تشغيل فعلية هون (راجع نقاش صاحب المشروع). هذا يغطي حصرًا الطرفين
/// اللي الباك إند مسؤول عنهم: (1) صفحة مزامنة الكتالوج ترجّع كل دفعة
/// متوفرة فعليًا لمنتج متتبَّع دفعات، (2) CompleteSaleCommand يتطلب
/// دفعة صريحة ويخصم من الدفعة الصحيحة بالذات - لا من أي دفعة تانية
/// لنفس المنتج.
/// </summary>
[Collection(DatabaseCollection.Name)]
public sealed class BatchTrackedCatalogSyncAndSaleTests : IntegrationTestBase
{
    public BatchTrackedCatalogSyncAndSaleTests(DatabaseFixture fixture) : base(fixture) { }

    [Fact]
    public async Task صفحة_مزامنة_الكتالوج_ترجع_كل_دفعات_المنتج_المتوفرة_بتاريخ_صلاحيتها_وكميتها()
    {
        using var scope = CreateScope();
        await TestDataBuilder.ActAsAdminAsync(scope, Fixture);
        var db = CreateDbContext(scope);
        var (product, _) = await TestDataBuilder.CreateActiveProductAsync(db, "لبن متتبَّع دفعات", isBatchTracked: true);
        await TestDataBuilder.CreateProductBranchAsync(db, product.Id, Fixture.TestBranchId, sellingPrice: 5m);

        var nearExpiry = DateOnly.FromDateTime(DateTime.UtcNow.AddDays(3));
        var farExpiry = DateOnly.FromDateTime(DateTime.UtcNow.AddDays(30));
        var batchNear = await TestDataBuilder.CreateBatchAsync(db, product.Id, Fixture.TestBranchId, "B-NEAR", unitCost: 3m, expiryDate: nearExpiry);
        var batchFar = await TestDataBuilder.CreateBatchAsync(db, product.Id, Fixture.TestBranchId, "B-FAR", unitCost: 3m, expiryDate: farExpiry);
        await TestDataBuilder.SetStockAsync(db, product.Id, Fixture.TestBranchId, 10m, batchNear.Id);
        await TestDataBuilder.SetStockAsync(db, product.Id, Fixture.TestBranchId, 20m, batchFar.Id);

        // دفعة ثالثة نفدت كميتها بالكامل - ما لازم تظهر أصلًا بالمزامنة
        // (راجع تعليق GetCatalogSyncPageQuery: QuantityAvailable لدفعة
        // فاضية = لا داعي نعرضها للكاشير كخيار).
        var batchDepleted = await TestDataBuilder.CreateBatchAsync(db, product.Id, Fixture.TestBranchId, "B-EMPTY", unitCost: 3m);
        await TestDataBuilder.SetStockAsync(db, product.Id, Fixture.TestBranchId, 0m, batchDepleted.Id);

        var client = await CreateAuthenticatedClientAsync();
        var response = await client.GetAsync($"/api/v1/cashier-sync/catalog-page?branchId={Fixture.TestBranchId}&pageNumber=1&pageSize=200");
        response.EnsureSuccessStatusCode();

        var json = JsonDocument.Parse(await response.Content.ReadAsStringAsync()).RootElement;
        var item = json.GetProperty("items").EnumerateArray().Single(i => i.GetProperty("productId").GetGuid() == product.Id);

        Assert.True(item.GetProperty("isBatchTracked").GetBoolean());

        var batches = item.GetProperty("batches").EnumerateArray().ToList();
        Assert.Equal(2, batches.Count);

        var syncedNear = batches.Single(b => b.GetProperty("batchNumber").GetString() == "B-NEAR");
        Assert.Equal(nearExpiry.ToString("yyyy-MM-dd"), syncedNear.GetProperty("expiryDate").GetString());
        Assert.Equal(10m, syncedNear.GetProperty("quantityAvailable").GetDecimal());

        var syncedFar = batches.Single(b => b.GetProperty("batchNumber").GetString() == "B-FAR");
        Assert.Equal(20m, syncedFar.GetProperty("quantityAvailable").GetDecimal());

        Assert.DoesNotContain(batches, b => b.GetProperty("batchNumber").GetString() == "B-EMPTY");
    }

    [Fact]
    public async Task بيع_منتج_متتبَّع_دفعات_بلا_تحديد_دفعة_يفشل_بخطأ_تحقق()
    {
        using var scope = CreateScope();
        await TestDataBuilder.ActAsAdminAsync(scope, Fixture);
        var db = CreateDbContext(scope);
        var (product, unit) = await TestDataBuilder.CreateActiveProductAsync(db, "منتج دفعات بلا تحديد", isBatchTracked: true);
        await TestDataBuilder.CreateProductBranchAsync(db, product.Id, Fixture.TestBranchId, sellingPrice: 5m);
        var batch = await TestDataBuilder.CreateBatchAsync(db, product.Id, Fixture.TestBranchId, "B-1");
        await TestDataBuilder.SetStockAsync(db, product.Id, Fixture.TestBranchId, 10m, batch.Id);

        var handler = scope.ServiceProvider.GetRequiredService<CompleteSaleHandler>();
        var command = new CompleteSaleCommand(
            Fixture.TestBranchId,
            Guid.NewGuid(),
            CustomerId: null,
            InvoiceLevelDiscountAmount: 0m,
            Items: new[] { new CompleteSaleItemDto(product.Id, unit.Id, Quantity: 1m, ManualDiscountAmount: 0m, ProductBatchId: null) },
            Payments: new[] { new CompleteSalePaymentDto(TestDataBuilder.CashPaymentMethodId, 5m, null, Guid.NewGuid()) });

        var result = await handler.HandleAsync(command, CancellationToken.None);

        Assert.True(result.IsFailure);
        Assert.Equal(ErrorType.Validation, result.Error!.Type);
        Assert.Equal("Sale.BatchRequired", result.Error.Code);
    }

    [Fact]
    public async Task بيع_من_دفعة_محددة_يخصم_من_تلك_الدفعة_فقط_ويترك_الدفعة_الأخرى_كما_هي()
    {
        using var scope = CreateScope();
        await TestDataBuilder.ActAsAdminAsync(scope, Fixture);
        var db = CreateDbContext(scope);
        var (product, unit) = await TestDataBuilder.CreateActiveProductAsync(db, "منتج دفعتين", isBatchTracked: true);
        await TestDataBuilder.CreateProductBranchAsync(db, product.Id, Fixture.TestBranchId, sellingPrice: 5m);

        var batchOld = await TestDataBuilder.CreateBatchAsync(db, product.Id, Fixture.TestBranchId, "OLD");
        var batchNew = await TestDataBuilder.CreateBatchAsync(db, product.Id, Fixture.TestBranchId, "NEW");
        await TestDataBuilder.SetStockAsync(db, product.Id, Fixture.TestBranchId, 5m, batchOld.Id);
        await TestDataBuilder.SetStockAsync(db, product.Id, Fixture.TestBranchId, 8m, batchNew.Id);

        var handler = scope.ServiceProvider.GetRequiredService<CompleteSaleHandler>();
        var command = new CompleteSaleCommand(
            Fixture.TestBranchId,
            Guid.NewGuid(),
            CustomerId: null,
            InvoiceLevelDiscountAmount: 0m,
            // نختار قصدًا الدفعة "OLD" (لو كان فيه ترتيب FEFO ضمني كان
            // يفترض دايمًا الأقدم - هون نتحقق إنه الاختيار الصريح
            // من الكاشير (ProductBatchId) هو المحترَم، لا أي افتراض تلقائي).
            Items: new[] { new CompleteSaleItemDto(product.Id, unit.Id, Quantity: 2m, ManualDiscountAmount: 0m, ProductBatchId: batchOld.Id) },
            Payments: new[] { new CompleteSalePaymentDto(TestDataBuilder.CashPaymentMethodId, 10m, null, Guid.NewGuid()) });

        var result = await handler.HandleAsync(command, CancellationToken.None);

        Assert.True(result.IsSuccess);

        var oldStock = await db.Stocks.AsNoTracking().FirstAsync(s => s.ProductBatchId == batchOld.Id);
        var newStock = await db.Stocks.AsNoTracking().FirstAsync(s => s.ProductBatchId == batchNew.Id);

        Assert.Equal(3m, oldStock.QuantityOnHand);
        Assert.Equal(8m, newStock.QuantityOnHand);
    }

    [Fact]
    public async Task بيع_منتج_غير_متتبَّع_دفعات_بتحديد_دفعة_يفشل_بخطأ_تحقق()
    {
        using var scope = CreateScope();
        await TestDataBuilder.ActAsAdminAsync(scope, Fixture);
        var db = CreateDbContext(scope);
        var (product, unit) = await TestDataBuilder.CreateActiveProductAsync(db, "منتج عادي بلا دفعات");
        await TestDataBuilder.CreateProductBranchAsync(db, product.Id, Fixture.TestBranchId, sellingPrice: 5m);
        await TestDataBuilder.SetStockAsync(db, product.Id, Fixture.TestBranchId, 10m);

        var handler = scope.ServiceProvider.GetRequiredService<CompleteSaleHandler>();
        var command = new CompleteSaleCommand(
            Fixture.TestBranchId,
            Guid.NewGuid(),
            CustomerId: null,
            InvoiceLevelDiscountAmount: 0m,
            Items: new[] { new CompleteSaleItemDto(product.Id, unit.Id, Quantity: 1m, ManualDiscountAmount: 0m, ProductBatchId: Guid.NewGuid()) },
            Payments: new[] { new CompleteSalePaymentDto(TestDataBuilder.CashPaymentMethodId, 5m, null, Guid.NewGuid()) });

        var result = await handler.HandleAsync(command, CancellationToken.None);

        Assert.True(result.IsFailure);
        Assert.Equal(ErrorType.Validation, result.Error!.Type);
        Assert.Equal("Sale.BatchNotApplicable", result.Error.Code);
    }
}
