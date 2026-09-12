using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using SupermarketSystem.Application.Common.Results;
using SupermarketSystem.Application.Purchasing.CompletePurchaseInvoice;
using SupermarketSystem.Domain.Inventory;
using SupermarketSystem.Infrastructure.Persistence;
using SupermarketSystem.IntegrationTests.Common;
using Xunit;

namespace SupermarketSystem.IntegrationTests.Purchasing;

/// <summary>
/// CompletePurchaseInvoiceHandler — أول معاملة ذرية بالنظام (فاتورة +
/// حركة مخزون PurchaseIn + زيادة Stock بنفس SaveChangesAsync)، وقاعدة
/// "سماح مع مراجعة" لسعر شراء مرتفع بشكل ملحوظ (CLAUDE.md §1.6).
/// </summary>
[Collection(DatabaseCollection.Name)]
public sealed class CompletePurchaseInvoiceTests : IntegrationTestBase
{
    public CompletePurchaseInvoiceTests(DatabaseFixture fixture) : base(fixture) { }

    [Fact]
    public async Task إتمام_فاتورة_شراء_يزيد_المخزون_ويسجل_حركة_PurchaseIn()
    {
        using var scope = CreateScope();
        await TestDataBuilder.ActAsAdminAsync(scope, Fixture);
        var db = CreateDbContext(scope);
        var (product, unit) = await TestDataBuilder.CreateActiveProductAsync(db, "منتج شراء");
        var supplier = await TestDataBuilder.CreateSupplierAsync(db);

        var handler = scope.ServiceProvider.GetRequiredService<CompletePurchaseInvoiceHandler>();
        var result = await handler.HandleAsync(
            new CompletePurchaseInvoiceCommand(
                Fixture.TestBranchId, supplier.Id, "INV-001",
                new[] { new CompletePurchaseInvoiceItemDto(product.Id, unit.Id, 20m, 5m, null, null, null) }),
            CancellationToken.None);

        Assert.True(result.IsSuccess);
        Assert.Equal(100m, result.Value.TotalAmount);

        var stock = await db.Stocks.AsNoTracking().FirstAsync(s => s.ProductId == product.Id);
        Assert.Equal(20m, stock.QuantityOnHand);

        var movement = await db.StockMovements.AsNoTracking().FirstAsync(m => m.ProductId == product.Id);
        Assert.Equal(MovementType.PurchaseIn, movement.MovementType);
        Assert.Equal(20m, movement.QuantityBase);
    }

    [Fact]
    public async Task شراء_منتج_متتبَّع_دفعات_بدون_رقم_دفعة_يفشل_بخطأ_تحقق()
    {
        using var scope = CreateScope();
        await TestDataBuilder.ActAsAdminAsync(scope, Fixture);
        var db = CreateDbContext(scope);
        var (product, unit) = await TestDataBuilder.CreateActiveProductAsync(db, "منتج شراء بدفعات", isBatchTracked: true);
        var supplier = await TestDataBuilder.CreateSupplierAsync(db);

        var handler = scope.ServiceProvider.GetRequiredService<CompletePurchaseInvoiceHandler>();
        var result = await handler.HandleAsync(
            new CompletePurchaseInvoiceCommand(
                Fixture.TestBranchId, supplier.Id, null,
                new[] { new CompletePurchaseInvoiceItemDto(product.Id, unit.Id, 10m, 5m, null, null, null) }),
            CancellationToken.None);

        Assert.True(result.IsFailure);
        Assert.Equal(ErrorType.Validation, result.Error!.Type);
        Assert.Equal("PurchaseInvoice.BatchRequired", result.Error.Code);
    }

    [Fact]
    public async Task شراء_منتج_متتبَّع_دفعات_برقم_دفعة_جديد_ينشئ_دفعة_ويزيد_رصيدها()
    {
        using var scope = CreateScope();
        await TestDataBuilder.ActAsAdminAsync(scope, Fixture);
        var db = CreateDbContext(scope);
        var (product, unit) = await TestDataBuilder.CreateActiveProductAsync(db, "منتج دفعة جديدة", isBatchTracked: true);
        var supplier = await TestDataBuilder.CreateSupplierAsync(db);

        var handler = scope.ServiceProvider.GetRequiredService<CompletePurchaseInvoiceHandler>();
        var result = await handler.HandleAsync(
            new CompletePurchaseInvoiceCommand(
                Fixture.TestBranchId, supplier.Id, null,
                new[] { new CompletePurchaseInvoiceItemDto(product.Id, unit.Id, 10m, 5m, null, "BATCH-001", DateOnly.FromDateTime(DateTime.UtcNow.AddDays(30))) }),
            CancellationToken.None);

        Assert.True(result.IsSuccess);

        var batch = await db.ProductBatches.AsNoTracking().FirstAsync(b => b.ProductId == product.Id);
        Assert.Equal("BATCH-001", batch.BatchNumber);

        var stock = await db.Stocks.AsNoTracking().FirstAsync(s => s.ProductBatchId == batch.Id);
        Assert.Equal(10m, stock.QuantityOnHand);
    }

    [Fact]
    public async Task سعر_شراء_أعلى_بشكل_ملحوظ_من_متوسط_آخر_مشتريات_يُعلَّم_للمراجعة_ولا_يُرفض()
    {
        using var scope = CreateScope();
        await TestDataBuilder.ActAsAdminAsync(scope, Fixture);
        var db = CreateDbContext(scope);
        var (product, unit) = await TestDataBuilder.CreateActiveProductAsync(db, "منتج سعر مرتفع");
        var supplier = await TestDataBuilder.CreateSupplierAsync(db);

        var handler = scope.ServiceProvider.GetRequiredService<CompletePurchaseInvoiceHandler>();

        var baseline = await handler.HandleAsync(
            new CompletePurchaseInvoiceCommand(Fixture.TestBranchId, supplier.Id, null,
                new[] { new CompletePurchaseInvoiceItemDto(product.Id, unit.Id, 10m, 10m, null, null, null) }),
            CancellationToken.None);
        Assert.True(baseline.IsSuccess);

        // 30 أعلى من 10 + 15% الافتراضي (11.5) - لازم تُعلَّم، بس الفاتورة تنجح وتُسجَّل بالكامل.
        var expensive = await handler.HandleAsync(
            new CompletePurchaseInvoiceCommand(Fixture.TestBranchId, supplier.Id, null,
                new[] { new CompletePurchaseInvoiceItemDto(product.Id, unit.Id, 5m, 30m, null, null, null) }),
            CancellationToken.None);

        Assert.True(expensive.IsSuccess);

        var flaggedItem = await db.PurchaseInvoiceItems.AsNoTracking()
            .FirstAsync(i => i.PurchaseInvoiceId == expensive.Value.PurchaseInvoiceId);
        Assert.True(flaggedItem.NeedsReview);

        // المخزون انزاد فعليًا رغم التعليم - العملية لم تُرفض إطلاقًا.
        var stock = await db.Stocks.AsNoTracking().FirstAsync(s => s.ProductId == product.Id);
        Assert.Equal(15m, stock.QuantityOnHand);
    }

    [Fact]
    public async Task شراء_بسعر_عادي_ضمن_الهامش_المسموح_لا_يُعلَّم_للمراجعة()
    {
        using var scope = CreateScope();
        await TestDataBuilder.ActAsAdminAsync(scope, Fixture);
        var db = CreateDbContext(scope);
        var (product, unit) = await TestDataBuilder.CreateActiveProductAsync(db, "منتج سعر مستقر");
        var supplier = await TestDataBuilder.CreateSupplierAsync(db);

        var handler = scope.ServiceProvider.GetRequiredService<CompletePurchaseInvoiceHandler>();

        var baseline = await handler.HandleAsync(
            new CompletePurchaseInvoiceCommand(Fixture.TestBranchId, supplier.Id, null,
                new[] { new CompletePurchaseInvoiceItemDto(product.Id, unit.Id, 10m, 10m, null, null, null) }),
            CancellationToken.None);
        Assert.True(baseline.IsSuccess);

        var stable = await handler.HandleAsync(
            new CompletePurchaseInvoiceCommand(Fixture.TestBranchId, supplier.Id, null,
                new[] { new CompletePurchaseInvoiceItemDto(product.Id, unit.Id, 5m, 10.5m, null, null, null) }),
            CancellationToken.None);
        Assert.True(stable.IsSuccess);

        var item = await db.PurchaseInvoiceItems.AsNoTracking().FirstAsync(i => i.PurchaseInvoiceId == stable.Value.PurchaseInvoiceId);
        Assert.False(item.NeedsReview);
    }
}
