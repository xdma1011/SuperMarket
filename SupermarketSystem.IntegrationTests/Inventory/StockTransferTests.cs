using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using SupermarketSystem.Application.Common.Pagination;
using SupermarketSystem.Application.Common.Results;
using SupermarketSystem.Application.Inventory.CreateStockTransfer;
using SupermarketSystem.Application.Inventory.GetProductBatchesWithStock;
using SupermarketSystem.Application.Inventory.GetStockTransferDetail;
using SupermarketSystem.Application.Inventory.GetStockTransfers;
using SupermarketSystem.Application.Inventory.ReceiveStockTransfer;
using SupermarketSystem.Domain.Inventory;
using SupermarketSystem.Infrastructure.Persistence;
using SupermarketSystem.IntegrationTests.Common;
using Xunit;

namespace SupermarketSystem.IntegrationTests.Inventory;

/// <summary>
/// CreateStockTransferHandler/ReceiveStockTransferHandler — خطوتان
/// منفصلتان بالوقت (إرسال يخصم من المصدر فورًا، استلام يضيف للوجهة
/// لاحقًا). راجع تعليق StockTransfer.cs بالـDomain.
/// </summary>
[Collection(DatabaseCollection.Name)]
public sealed class StockTransferTests : IntegrationTestBase
{
    public StockTransferTests(DatabaseFixture fixture) : base(fixture) { }

    [Fact]
    public async Task إرسال_نقل_مخزون_يخصم_من_الفرع_المصدر_فورًا_ولا_يؤثر_على_الوجهة()
    {
        using var scope = CreateScope();
        await TestDataBuilder.ActAsAdminAsync(scope, Fixture);
        var db = CreateDbContext(scope);
        var destinationBranch = await TestDataBuilder.CreateBranchAsync(db, "فرع الوجهة", "DST1");
        var (product, unit) = await TestDataBuilder.CreateActiveProductAsync(db, "منتج للنقل");
        await TestDataBuilder.SetStockAsync(db, product.Id, Fixture.TestBranchId, 20m);

        var handler = scope.ServiceProvider.GetRequiredService<CreateStockTransferHandler>();
        var result = await handler.HandleAsync(
            new CreateStockTransferCommand(
                Fixture.TestBranchId, destinationBranch.Id,
                new[] { new CreateStockTransferItemDto(product.Id, unit.Id, 8m, null) }),
            CancellationToken.None);

        Assert.True(result.IsSuccess);

        var sourceStock = await db.Stocks.AsNoTracking().FirstAsync(s => s.ProductId == product.Id && s.BranchId == Fixture.TestBranchId);
        Assert.Equal(12m, sourceStock.QuantityOnHand);

        var destinationStockExists = await db.Stocks.AsNoTracking()
            .AnyAsync(s => s.ProductId == product.Id && s.BranchId == destinationBranch.Id);
        Assert.False(destinationStockExists);
    }

    [Fact]
    public async Task استلام_نقل_مخزون_يضيف_الكمية_للفرع_الوجهة()
    {
        using var scope = CreateScope();
        await TestDataBuilder.ActAsAdminAsync(scope, Fixture);
        var db = CreateDbContext(scope);
        var destinationBranch = await TestDataBuilder.CreateBranchAsync(db, "فرع الوجهة 2", "DST2");
        var (product, unit) = await TestDataBuilder.CreateActiveProductAsync(db, "منتج للاستلام");
        await TestDataBuilder.SetStockAsync(db, product.Id, Fixture.TestBranchId, 20m);

        var createHandler = scope.ServiceProvider.GetRequiredService<CreateStockTransferHandler>();
        var created = await createHandler.HandleAsync(
            new CreateStockTransferCommand(
                Fixture.TestBranchId, destinationBranch.Id,
                new[] { new CreateStockTransferItemDto(product.Id, unit.Id, 8m, null) }),
            CancellationToken.None);
        Assert.True(created.IsSuccess);

        var receiveHandler = scope.ServiceProvider.GetRequiredService<ReceiveStockTransferHandler>();
        var received = await receiveHandler.HandleAsync(new ReceiveStockTransferCommand(created.Value.StockTransferId), CancellationToken.None);

        Assert.True(received.IsSuccess);

        var destinationStock = await db.Stocks.AsNoTracking().FirstAsync(s => s.ProductId == product.Id && s.BranchId == destinationBranch.Id);
        Assert.Equal(8m, destinationStock.QuantityOnHand);
    }

    [Fact]
    public async Task استلام_نقل_مستلَم_مسبقًا_يفشل_بتعارض()
    {
        using var scope = CreateScope();
        await TestDataBuilder.ActAsAdminAsync(scope, Fixture);
        var db = CreateDbContext(scope);
        var destinationBranch = await TestDataBuilder.CreateBranchAsync(db, "فرع الوجهة 3", "DST3");
        var (product, unit) = await TestDataBuilder.CreateActiveProductAsync(db, "منتج استلام مكرر");
        await TestDataBuilder.SetStockAsync(db, product.Id, Fixture.TestBranchId, 20m);

        var createHandler = scope.ServiceProvider.GetRequiredService<CreateStockTransferHandler>();
        var created = await createHandler.HandleAsync(
            new CreateStockTransferCommand(Fixture.TestBranchId, destinationBranch.Id,
                new[] { new CreateStockTransferItemDto(product.Id, unit.Id, 5m, null) }),
            CancellationToken.None);

        var receiveHandler = scope.ServiceProvider.GetRequiredService<ReceiveStockTransferHandler>();
        var firstReceive = await receiveHandler.HandleAsync(new ReceiveStockTransferCommand(created.Value.StockTransferId), CancellationToken.None);
        Assert.True(firstReceive.IsSuccess);

        var secondReceive = await receiveHandler.HandleAsync(new ReceiveStockTransferCommand(created.Value.StockTransferId), CancellationToken.None);

        Assert.True(secondReceive.IsFailure);
        Assert.Equal(ErrorType.Conflict, secondReceive.Error!.Type);
        Assert.Equal("StockTransfer.NotDispatched", secondReceive.Error.Code);
    }

    [Fact]
    public async Task نقل_بمخزون_مصدر_غير_كافٍ_يفشل_بقاعدة_عمل_واضحة()
    {
        using var scope = CreateScope();
        await TestDataBuilder.ActAsAdminAsync(scope, Fixture);
        var db = CreateDbContext(scope);
        var destinationBranch = await TestDataBuilder.CreateBranchAsync(db, "فرع الوجهة 4", "DST4");
        var (product, unit) = await TestDataBuilder.CreateActiveProductAsync(db, "منتج نقل بلا رصيد");
        await TestDataBuilder.SetStockAsync(db, product.Id, Fixture.TestBranchId, 2m);

        var handler = scope.ServiceProvider.GetRequiredService<CreateStockTransferHandler>();
        var result = await handler.HandleAsync(
            new CreateStockTransferCommand(Fixture.TestBranchId, destinationBranch.Id,
                new[] { new CreateStockTransferItemDto(product.Id, unit.Id, 10m, null) }),
            CancellationToken.None);

        Assert.True(result.IsFailure);
        Assert.Equal(ErrorType.BusinessRule, result.Error!.Type);
        Assert.Equal("StockTransfer.InsufficientStock", result.Error.Code);
    }

    [Fact]
    public async Task نقل_بنفس_الفرع_مصدرًا_ووجهة_يفشل_بخطأ_تحقق()
    {
        using var scope = CreateScope();
        await TestDataBuilder.ActAsAdminAsync(scope, Fixture);
        var handler = scope.ServiceProvider.GetRequiredService<CreateStockTransferHandler>();

        var result = await handler.HandleAsync(
            new CreateStockTransferCommand(Fixture.TestBranchId, Fixture.TestBranchId, Array.Empty<CreateStockTransferItemDto>()),
            CancellationToken.None);

        Assert.True(result.IsFailure);
        Assert.Equal(ErrorType.Validation, result.Error!.Type);
        Assert.Equal("StockTransfer.SameBranch", result.Error.Code);
    }

    [Fact]
    public async Task تفاصيل_وقائمة_عمليات_النقل_تعرض_البيانات_الصحيحة()
    {
        using var scope = CreateScope();
        await TestDataBuilder.ActAsAdminAsync(scope, Fixture);
        var db = CreateDbContext(scope);
        var destinationBranch = await TestDataBuilder.CreateBranchAsync(db, "فرع الوجهة 5", "DST5");
        var (product, unit) = await TestDataBuilder.CreateActiveProductAsync(db, "منتج تفاصيل النقل");
        await TestDataBuilder.SetStockAsync(db, product.Id, Fixture.TestBranchId, 20m);

        var createHandler = scope.ServiceProvider.GetRequiredService<CreateStockTransferHandler>();
        var created = await createHandler.HandleAsync(
            new CreateStockTransferCommand(Fixture.TestBranchId, destinationBranch.Id,
                new[] { new CreateStockTransferItemDto(product.Id, unit.Id, 4m, null) }),
            CancellationToken.None);
        Assert.True(created.IsSuccess);

        var detailHandler = scope.ServiceProvider.GetRequiredService<GetStockTransferDetailHandler>();
        var detail = await detailHandler.HandleAsync(new GetStockTransferDetailQuery(created.Value.StockTransferId), CancellationToken.None);
        Assert.True(detail.IsSuccess);
        Assert.Equal("بالطريق", detail.Value.StatusTitle);
        Assert.Single(detail.Value.Items);

        var listHandler = scope.ServiceProvider.GetRequiredService<GetStockTransfersHandler>();
        var list = await listHandler.HandleAsync(new GetStockTransfersQuery(new PagedRequest(), Fixture.TestBranchId), CancellationToken.None);
        Assert.Single(list.Items);
        Assert.Equal(created.Value.StockTransferId, list.Items[0].Id);
    }

    [Fact]
    public async Task قائمة_عمليات_النقل_افتراضيًا_تصاعدية_وبطلب_تنازلي_تعكس_الترتيب()
    {
        // خطأ إنتاج حقيقي كان هون: paging.IsDescending كانت معكوسة (طلب
        // "تنازلي" يرجّع تصاعدي والعكس) - راجع تقرير الجلسة. تم تصحيحه
        // بـGetStockTransfersQuery.cs، وهذا الاختبار يثبّت السلوك الصحيح.
        using var scope = CreateScope();
        await TestDataBuilder.ActAsAdminAsync(scope, Fixture);
        var db = CreateDbContext(scope);
        var destinationBranch = await TestDataBuilder.CreateBranchAsync(db, "فرع الوجهة 6", "DST6");
        var (product, unit) = await TestDataBuilder.CreateActiveProductAsync(db, "منتج ترتيب النقل");
        await TestDataBuilder.SetStockAsync(db, product.Id, Fixture.TestBranchId, 20m);

        var createHandler = scope.ServiceProvider.GetRequiredService<CreateStockTransferHandler>();
        var first = await createHandler.HandleAsync(
            new CreateStockTransferCommand(Fixture.TestBranchId, destinationBranch.Id,
                new[] { new CreateStockTransferItemDto(product.Id, unit.Id, 1m, null) }),
            CancellationToken.None);
        var second = await createHandler.HandleAsync(
            new CreateStockTransferCommand(Fixture.TestBranchId, destinationBranch.Id,
                new[] { new CreateStockTransferItemDto(product.Id, unit.Id, 1m, null) }),
            CancellationToken.None);
        Assert.True(first.IsSuccess);
        Assert.True(second.IsSuccess);

        var listHandler = scope.ServiceProvider.GetRequiredService<GetStockTransfersHandler>();

        var ascending = await listHandler.HandleAsync(
            new GetStockTransfersQuery(new PagedRequest { SortDirection = "asc" }, Fixture.TestBranchId), CancellationToken.None);
        Assert.Equal(first.Value.StockTransferId, ascending.Items[0].Id);
        Assert.Equal(second.Value.StockTransferId, ascending.Items[1].Id);

        var descending = await listHandler.HandleAsync(
            new GetStockTransfersQuery(new PagedRequest { SortDirection = "desc" }, Fixture.TestBranchId), CancellationToken.None);
        Assert.Equal(second.Value.StockTransferId, descending.Items[0].Id);
        Assert.Equal(first.Value.StockTransferId, descending.Items[1].Id);
    }

    [Fact]
    public async Task دفعات_منتج_فيها_رصيد_فعلي_فقط_تظهر_لاختيار_دفعة_النقل()
    {
        using var scope = CreateScope();
        await TestDataBuilder.ActAsAdminAsync(scope, Fixture);
        var db = CreateDbContext(scope);
        var (product, _) = await TestDataBuilder.CreateActiveProductAsync(db, "منتج دفعات النقل", isBatchTracked: true);
        var batchWithStock = await TestDataBuilder.CreateBatchAsync(db, product.Id, Fixture.TestBranchId, "BWS");
        var batchEmpty = await TestDataBuilder.CreateBatchAsync(db, product.Id, Fixture.TestBranchId, "EMPTY");
        await TestDataBuilder.SetStockAsync(db, product.Id, Fixture.TestBranchId, 5m, batchWithStock.Id);
        await TestDataBuilder.SetStockAsync(db, product.Id, Fixture.TestBranchId, 0m, batchEmpty.Id);

        var handler = scope.ServiceProvider.GetRequiredService<GetProductBatchesWithStockHandler>();
        var result = await handler.HandleAsync(new GetProductBatchesWithStockQuery(product.Id, Fixture.TestBranchId), CancellationToken.None);

        var batch = Assert.Single(result);
        Assert.Equal("BWS", batch.BatchNumber);
        Assert.Equal(5m, batch.QuantityOnHand);
    }
}
