using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using SupermarketSystem.Application.Common.Pagination;
using SupermarketSystem.Application.Common.Results;
using SupermarketSystem.Application.Inventory.ApproveStocktake;
using SupermarketSystem.Application.Inventory.CompleteStocktake;
using SupermarketSystem.Application.Inventory.CreateStocktake;
using SupermarketSystem.Application.Inventory.GetStocktakeById;
using SupermarketSystem.Application.Inventory.GetStocktakes;
using SupermarketSystem.Application.Inventory.RecordStocktakeCount;
using SupermarketSystem.Domain.Inventory;
using SupermarketSystem.Infrastructure.Persistence;
using SupermarketSystem.IntegrationTests.Common;
using Xunit;

namespace SupermarketSystem.IntegrationTests.Inventory;

/// <summary>
/// دورة حياة الجرد الكاملة: إنشاء (InProgress مباشرة) → عدّ → إكمال (يعرض
/// الفروقات) → اعتماد (الخطوة الوحيدة اللي فعليًا بتلمس Stock).
/// </summary>
[Collection(DatabaseCollection.Name)]
public sealed class StocktakeTests : IntegrationTestBase
{
    public StocktakeTests(DatabaseFixture fixture) : base(fixture) { }

    [Fact]
    public async Task إنشاء_جرد_يلتقط_رصيد_المخزون_الحالي_كـExpectedQuantity()
    {
        using var scope = CreateScope();
        await TestDataBuilder.ActAsAdminAsync(scope, Fixture);
        var db = CreateDbContext(scope);
        var (product, _) = await TestDataBuilder.CreateActiveProductAsync(db, "منتج جرد");
        await TestDataBuilder.CreateProductBranchAsync(db, product.Id, Fixture.TestBranchId, 10m);
        await TestDataBuilder.SetStockAsync(db, product.Id, Fixture.TestBranchId, 30m);

        var handler = scope.ServiceProvider.GetRequiredService<CreateStocktakeHandler>();
        var result = await handler.HandleAsync(
            new CreateStocktakeCommand(Fixture.TestBranchId, IncludeAllProductsAtBranch: true, ProductIds: null),
            CancellationToken.None);

        Assert.True(result.IsSuccess);
        Assert.Equal(1, result.Value.ItemCount);

        var stocktake = await db.Stocktakes.AsNoTracking().Include(s => s.Items).FirstAsync(s => s.Id == result.Value.StocktakeId);
        Assert.Equal(StocktakeStatus.InProgress, stocktake.Status);
        Assert.Equal(30m, stocktake.Items.Single().ExpectedQuantity);
    }

    [Fact]
    public async Task إنشاء_جرد_بلا_أصناف_ولا_شمول_كامل_يفشل_بخطأ_تحقق()
    {
        using var scope = CreateScope();
        await TestDataBuilder.ActAsAdminAsync(scope, Fixture);
        var handler = scope.ServiceProvider.GetRequiredService<CreateStocktakeHandler>();

        var result = await handler.HandleAsync(
            new CreateStocktakeCommand(Fixture.TestBranchId, IncludeAllProductsAtBranch: false, ProductIds: null),
            CancellationToken.None);

        Assert.True(result.IsFailure);
        Assert.Equal(ErrorType.Validation, result.Error!.Type);
        Assert.Equal("Stocktake.ProductsRequired", result.Error.Code);
    }

    private async Task<(Guid StocktakeId, Guid StocktakeItemId, Guid ProductId)> CreateInProgressStocktakeAsync(
        IServiceScope scope, decimal expectedQuantity = 30m)
    {
        var db = CreateDbContext(scope);
        var (product, _) = await TestDataBuilder.CreateActiveProductAsync(db, "منتج دورة الجرد");
        await TestDataBuilder.CreateProductBranchAsync(db, product.Id, Fixture.TestBranchId, 10m);
        await TestDataBuilder.SetStockAsync(db, product.Id, Fixture.TestBranchId, expectedQuantity);

        var createHandler = scope.ServiceProvider.GetRequiredService<CreateStocktakeHandler>();
        var created = await createHandler.HandleAsync(
            new CreateStocktakeCommand(Fixture.TestBranchId, true, null), CancellationToken.None);
        Assert.True(created.IsSuccess);

        var itemId = await db.StocktakeItems.AsNoTracking()
            .Where(i => i.StocktakeId == created.Value.StocktakeId).Select(i => i.Id).FirstAsync();

        return (created.Value.StocktakeId, itemId, product.Id);
    }

    [Fact]
    public async Task تسجيل_عدّ_يحسب_الفرق_بشكل_صحيح()
    {
        using var scope = CreateScope();
        await TestDataBuilder.ActAsAdminAsync(scope, Fixture);
        var (stocktakeId, itemId, _) = await CreateInProgressStocktakeAsync(scope, expectedQuantity: 30m);

        var handler = scope.ServiceProvider.GetRequiredService<RecordStocktakeCountHandler>();
        var result = await handler.HandleAsync(new RecordStocktakeCountCommand(stocktakeId, itemId, CountedQuantity: 25m), CancellationToken.None);

        Assert.True(result.IsSuccess);
        Assert.Equal(30m, result.Value.ExpectedQuantity);
        Assert.Equal(25m, result.Value.CountedQuantity);
        Assert.Equal(-5m, result.Value.Variance);
    }

    [Fact]
    public async Task إكمال_الجرد_بلا_عدّ_كل_الأصناف_يفشل()
    {
        using var scope = CreateScope();
        await TestDataBuilder.ActAsAdminAsync(scope, Fixture);
        var (stocktakeId, _, _) = await CreateInProgressStocktakeAsync(scope);

        var handler = scope.ServiceProvider.GetRequiredService<CompleteStocktakeHandler>();
        var result = await handler.HandleAsync(new CompleteStocktakeCommand(stocktakeId), CancellationToken.None);

        Assert.True(result.IsFailure);
        Assert.Equal(ErrorType.BusinessRule, result.Error!.Type);
        Assert.Equal("Stocktake.CannotComplete", result.Error.Code);
    }

    [Fact]
    public async Task دورة_جرد_كاملة_نقص_بالمخزون_يعتمَد_وينزل_الرصيد_فعليًا()
    {
        using var scope = CreateScope();
        await TestDataBuilder.ActAsAdminAsync(scope, Fixture);
        var db = CreateDbContext(scope);
        var (stocktakeId, itemId, productId) = await CreateInProgressStocktakeAsync(scope, expectedQuantity: 30m);

        var countHandler = scope.ServiceProvider.GetRequiredService<RecordStocktakeCountHandler>();
        var countResult = await countHandler.HandleAsync(new RecordStocktakeCountCommand(stocktakeId, itemId, 25m), CancellationToken.None);
        Assert.True(countResult.IsSuccess);

        var completeHandler = scope.ServiceProvider.GetRequiredService<CompleteStocktakeHandler>();
        var completeResult = await completeHandler.HandleAsync(new CompleteStocktakeCommand(stocktakeId), CancellationToken.None);
        Assert.True(completeResult.IsSuccess);
        Assert.Single(completeResult.Value.Variances);

        // الإكمال لا يلمس المخزون إطلاقًا.
        var stockAfterComplete = await db.Stocks.AsNoTracking().FirstAsync(s => s.ProductId == productId);
        Assert.Equal(30m, stockAfterComplete.QuantityOnHand);

        var approveHandler = scope.ServiceProvider.GetRequiredService<ApproveStocktakeHandler>();
        var approveResult = await approveHandler.HandleAsync(new ApproveStocktakeCommand(stocktakeId), CancellationToken.None);
        Assert.True(approveResult.IsSuccess);
        var correction = Assert.Single(approveResult.Value.AppliedCorrections);
        Assert.Equal(-5m, correction.Variance);
        Assert.False(correction.WentNegative);

        var stockAfterApprove = await db.Stocks.AsNoTracking().FirstAsync(s => s.ProductId == productId);
        Assert.Equal(25m, stockAfterApprove.QuantityOnHand);

        var stocktake = await db.Stocktakes.AsNoTracking().FirstAsync(s => s.Id == stocktakeId);
        Assert.Equal(StocktakeStatus.Approved, stocktake.Status);
    }

    [Fact]
    public async Task اعتماد_جرد_غير_مكتمل_يفشل()
    {
        using var scope = CreateScope();
        await TestDataBuilder.ActAsAdminAsync(scope, Fixture);
        var (stocktakeId, _, _) = await CreateInProgressStocktakeAsync(scope);

        var handler = scope.ServiceProvider.GetRequiredService<ApproveStocktakeHandler>();
        var result = await handler.HandleAsync(new ApproveStocktakeCommand(stocktakeId), CancellationToken.None);

        Assert.True(result.IsFailure);
        Assert.Equal(ErrorType.BusinessRule, result.Error!.Type);
        Assert.Equal("Stocktake.NotCompleted", result.Error.Code);
    }

    [Fact]
    public async Task قائمة_الجرد_وتفاصيله_تعرض_البيانات_والعنوان_العربي_الصحيح()
    {
        using var scope = CreateScope();
        await TestDataBuilder.ActAsAdminAsync(scope, Fixture);
        var (stocktakeId, _, _) = await CreateInProgressStocktakeAsync(scope);

        var byIdHandler = scope.ServiceProvider.GetRequiredService<GetStocktakeByIdHandler>();
        var byId = await byIdHandler.HandleAsync(new GetStocktakeByIdQuery(stocktakeId), CancellationToken.None);
        Assert.True(byId.IsSuccess);
        Assert.Equal(StocktakeStatus.InProgress, byId.Value.Status);

        var listHandler = scope.ServiceProvider.GetRequiredService<GetStocktakesHandler>();
        var list = await listHandler.HandleAsync(new GetStocktakesQuery(new PagedRequest(), Fixture.TestBranchId), CancellationToken.None);

        var row = Assert.Single(list.Items);
        Assert.Equal("جارٍ العدّ", row.StatusTitle);
        Assert.Equal(1, row.ItemCount);
    }
}
