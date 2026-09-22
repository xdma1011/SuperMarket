using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using SupermarketSystem.Application.Common.Results;
using SupermarketSystem.Application.Inventory.RecordWasteIssue;
using SupermarketSystem.Domain.Inventory;
using SupermarketSystem.IntegrationTests.Common;
using Xunit;

namespace SupermarketSystem.IntegrationTests.Inventory;

/// <summary>
/// RecordWasteIssueHandler — نفس مبدأ RecordComplimentaryIssueTests بالضبط
/// ("سماح مع مراجعة" - CLAUDE.md §1.6)، بس بعتبة/عدّاد مستقل، وبلا شرط
/// تفعيل مسبق على المنتج (أي منتج قابل للتلف).
/// </summary>
[Collection(DatabaseCollection.Name)]
public sealed class RecordWasteIssueTests : IntegrationTestBase
{
    public RecordWasteIssueTests(DatabaseFixture fixture) : base(fixture) { }

    [Fact]
    public async Task تلف_ضمن_الحد_اليومي_ينجح_بلا_علامة_مراجعة()
    {
        using var scope = CreateScope();
        await TestDataBuilder.ActAsAdminAsync(scope, Fixture);
        var db = CreateDbContext(scope);
        var (product, unit) = await TestDataBuilder.CreateActiveProductAsync(db, "منتج تلف ضمن الحد");
        await TestDataBuilder.SetStockAsync(db, product.Id, Fixture.TestBranchId, 50m);

        var handler = scope.ServiceProvider.GetRequiredService<RecordWasteIssueHandler>();
        var result = await handler.HandleAsync(
            new RecordWasteIssueCommand(product.Id, unit.Id, Fixture.TestBranchId, Quantity: 5m, WasteReason.Broken, Notes: "انكسر بالنقل"),
            CancellationToken.None);

        Assert.True(result.IsSuccess);
        Assert.False(result.Value.FlaggedForReview);

        var movement = await db.StockMovements.AsNoTracking().FirstAsync(m => m.Id == result.Value.StockMovementId);
        Assert.Equal(MovementType.WasteOut, movement.MovementType);
        Assert.Equal(WasteReason.Broken, movement.WasteReason);
        Assert.False(movement.NeedsReview);

        var stock = await db.Stocks.AsNoTracking().FirstAsync(s => s.ProductId == product.Id);
        Assert.Equal(45m, stock.QuantityOnHand);
    }

    [Fact]
    public async Task تلف_يتجاوز_الحد_اليومي_ينجح_ويُعلَّم_للمراجعة_ولا_يُرفض_أبدًا()
    {
        using var scope = CreateScope();
        await TestDataBuilder.ActAsAdminAsync(scope, Fixture);
        var db = CreateDbContext(scope);
        var (product, unit) = await TestDataBuilder.CreateActiveProductAsync(db, "منتج تلف يتجاوز الحد");
        await TestDataBuilder.SetStockAsync(db, product.Id, Fixture.TestBranchId, 100m);

        var handler = scope.ServiceProvider.GetRequiredService<RecordWasteIssueHandler>();
        var result = await handler.HandleAsync(
            new RecordWasteIssueCommand(product.Id, unit.Id, Fixture.TestBranchId, Quantity: 15m, WasteReason.Expired, Notes: null),
            CancellationToken.None);

        Assert.True(result.IsSuccess);
        Assert.True(result.Value.FlaggedForReview);

        var movement = await db.StockMovements.AsNoTracking().FirstAsync(m => m.Id == result.Value.StockMovementId);
        Assert.True(movement.NeedsReview);

        var stock = await db.Stocks.AsNoTracking().FirstAsync(s => s.ProductId == product.Id);
        Assert.Equal(85m, stock.QuantityOnHand);
    }

    [Fact]
    public async Task تلف_لمنتج_بلا_أي_تفعيل_ضيافة_ينجح_عادي_لأنه_بلا_شرط_تفعيل_مسبق()
    {
        // الفرق الجوهري عن الضيافة: التلف ما بيحتاج IsComplimentaryAllowed
        // ولا أي علم تفعيل مشابه - أي منتج قابل للتلف بطبيعته.
        using var scope = CreateScope();
        await TestDataBuilder.ActAsAdminAsync(scope, Fixture);
        var db = CreateDbContext(scope);
        var (product, unit) = await TestDataBuilder.CreateActiveProductAsync(db, "منتج بلا إذن ضيافة أصلًا");
        await TestDataBuilder.SetStockAsync(db, product.Id, Fixture.TestBranchId, 50m);

        var handler = scope.ServiceProvider.GetRequiredService<RecordWasteIssueHandler>();
        var result = await handler.HandleAsync(
            new RecordWasteIssueCommand(product.Id, unit.Id, Fixture.TestBranchId, 1m, WasteReason.StorageDamage, null),
            CancellationToken.None);

        Assert.True(result.IsSuccess);
    }

    [Fact]
    public async Task تلف_بكمية_صفر_أو_سالبة_يفشل_بخطأ_تحقق()
    {
        using var scope = CreateScope();
        await TestDataBuilder.ActAsAdminAsync(scope, Fixture);
        var db = CreateDbContext(scope);
        var (product, unit) = await TestDataBuilder.CreateActiveProductAsync(db, "منتج تلف كمية غلط");

        var handler = scope.ServiceProvider.GetRequiredService<RecordWasteIssueHandler>();
        var result = await handler.HandleAsync(
            new RecordWasteIssueCommand(product.Id, unit.Id, Fixture.TestBranchId, 0m, WasteReason.Other, null),
            CancellationToken.None);

        Assert.True(result.IsFailure);
        Assert.Equal(ErrorType.Validation, result.Error!.Type);
        Assert.Equal("Waste.QuantityMustBePositive", result.Error.Code);
    }

    [Fact]
    public async Task عدّاد_التلف_وعدّاد_الضيافة_مستقلّان_كليًا_عن_بعض()
    {
        // تلف يتجاوز حده اليومي ما لازم يأثّر على عدّاد الضيافة لنفس
        // المنتج، والعكس - كل عدّاد مفلتَر على MovementType الخاص فيه فقط.
        using var scope = CreateScope();
        await TestDataBuilder.ActAsAdminAsync(scope, Fixture);
        var db = CreateDbContext(scope);
        var (product, unit) = await TestDataBuilder.CreateActiveProductAsync(db, "منتج عدّادين مستقلّين", isComplimentaryAllowed: true);
        await TestDataBuilder.SetStockAsync(db, product.Id, Fixture.TestBranchId, 100m);

        var complimentaryHandler = scope.ServiceProvider
            .GetRequiredService<SupermarketSystem.Application.Inventory.RecordComplimentaryIssue.RecordComplimentaryIssueHandler>();
        var complimentaryResult = await complimentaryHandler.HandleAsync(
            new SupermarketSystem.Application.Inventory.RecordComplimentaryIssue.RecordComplimentaryIssueCommand(
                product.Id, unit.Id, Fixture.TestBranchId, 8m, "ضيافة"),
            CancellationToken.None);
        Assert.True(complimentaryResult.IsSuccess);
        Assert.False(complimentaryResult.Value.FlaggedForReview);

        var wasteHandler = scope.ServiceProvider.GetRequiredService<RecordWasteIssueHandler>();
        var wasteResult = await wasteHandler.HandleAsync(
            new RecordWasteIssueCommand(product.Id, unit.Id, Fixture.TestBranchId, 8m, WasteReason.Expired, null),
            CancellationToken.None);

        // 8 (تلف) لحاله ضمن الحد الافتراضي 10 - ما لازم يتأثر بـ8 (ضيافة) السابقة.
        Assert.True(wasteResult.IsSuccess);
        Assert.False(wasteResult.Value.FlaggedForReview);
    }
}
