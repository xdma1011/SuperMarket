using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using SupermarketSystem.Application.Common.Results;
using SupermarketSystem.Application.Inventory.RecordComplimentaryIssue;
using SupermarketSystem.Domain.Inventory;
using SupermarketSystem.Infrastructure.Persistence;
using SupermarketSystem.IntegrationTests.Common;
using Xunit;

namespace SupermarketSystem.IntegrationTests.Inventory;

/// <summary>
/// RecordComplimentaryIssueHandler — أهم قاعدة هون: "سماح مع مراجعة" حصرًا
/// (CLAUDE.md §1.6). تجاوز الحد اليومي **لازم ينجح** بـNeedsReview=true،
/// أبدًا لا يُرفض.
/// </summary>
[Collection(DatabaseCollection.Name)]
public sealed class RecordComplimentaryIssueTests : IntegrationTestBase
{
    public RecordComplimentaryIssueTests(DatabaseFixture fixture) : base(fixture) { }

    [Fact]
    public async Task ضيافة_ضمن_الحد_اليومي_تنجح_بلا_علامة_مراجعة()
    {
        using var scope = CreateScope();
        await TestDataBuilder.ActAsAdminAsync(scope, Fixture);
        var db = CreateDbContext(scope);
        var (product, unit) = await TestDataBuilder.CreateActiveProductAsync(db, "منتج ضيافة ضمن الحد", isComplimentaryAllowed: true);
        await TestDataBuilder.SetStockAsync(db, product.Id, Fixture.TestBranchId, 50m);

        var handler = scope.ServiceProvider.GetRequiredService<RecordComplimentaryIssueHandler>();
        // الحد الافتراضي 10 (Complimentary.DailyReviewThresholdQuantity) - هذا ضمنه.
        var result = await handler.HandleAsync(
            new RecordComplimentaryIssueCommand(product.Id, unit.Id, Fixture.TestBranchId, Quantity: 5m, Reason: "عيّنة مجانية"),
            CancellationToken.None);

        Assert.True(result.IsSuccess);
        Assert.False(result.Value.FlaggedForReview);

        var movement = await db.StockMovements.AsNoTracking().FirstAsync(m => m.Id == result.Value.StockMovementId);
        Assert.Equal(MovementType.ComplimentaryOut, movement.MovementType);
        Assert.False(movement.NeedsReview);

        var stock = await db.Stocks.AsNoTracking().FirstAsync(s => s.ProductId == product.Id);
        Assert.Equal(45m, stock.QuantityOnHand);
    }

    [Fact]
    public async Task ضيافة_تتجاوز_الحد_اليومي_تنجح_وتُعلَّم_للمراجعة_ولا_تُرفض_أبدًا()
    {
        using var scope = CreateScope();
        await TestDataBuilder.ActAsAdminAsync(scope, Fixture);
        var db = CreateDbContext(scope);
        var (product, unit) = await TestDataBuilder.CreateActiveProductAsync(db, "منتج ضيافة يتجاوز الحد", isComplimentaryAllowed: true);
        await TestDataBuilder.SetStockAsync(db, product.Id, Fixture.TestBranchId, 100m);

        var handler = scope.ServiceProvider.GetRequiredService<RecordComplimentaryIssueHandler>();
        // 15 > الحد الافتراضي 10 - لازم تنجح العملية بالكامل (مبدأ حاكم
        // §1.6: لا نوقف العملية أبدًا لمجرد الشك، نسمح ونعلّم للمراجعة).
        var result = await handler.HandleAsync(
            new RecordComplimentaryIssueCommand(product.Id, unit.Id, Fixture.TestBranchId, Quantity: 15m, Reason: "مناسبة كبيرة"),
            CancellationToken.None);

        Assert.True(result.IsSuccess);
        Assert.True(result.Value.FlaggedForReview);

        var movement = await db.StockMovements.AsNoTracking().FirstAsync(m => m.Id == result.Value.StockMovementId);
        Assert.True(movement.NeedsReview);

        // المخزون انخصم فعليًا - العملية أُتمَّت بالكامل، لا مجرد "تعليم بلا تنفيذ".
        var stock = await db.Stocks.AsNoTracking().FirstAsync(s => s.ProductId == product.Id);
        Assert.Equal(85m, stock.QuantityOnHand);
    }

    [Fact]
    public async Task تجاوز_الحد_بتراكم_عمليتين_بنفس_اليوم_يُعلَّم_ثانيهما_للمراجعة()
    {
        // الحد يُحسب على مجموع آخر 24 ساعة، لا العملية الواحدة لحالها.
        using var scope = CreateScope();
        await TestDataBuilder.ActAsAdminAsync(scope, Fixture);
        var db = CreateDbContext(scope);
        var (product, unit) = await TestDataBuilder.CreateActiveProductAsync(db, "منتج ضيافة تراكمي", isComplimentaryAllowed: true);
        await TestDataBuilder.SetStockAsync(db, product.Id, Fixture.TestBranchId, 100m);

        var handler = scope.ServiceProvider.GetRequiredService<RecordComplimentaryIssueHandler>();

        var firstResult = await handler.HandleAsync(
            new RecordComplimentaryIssueCommand(product.Id, unit.Id, Fixture.TestBranchId, 6m, "دفعة أولى"), CancellationToken.None);
        Assert.True(firstResult.IsSuccess);
        Assert.False(firstResult.Value.FlaggedForReview);

        var secondResult = await handler.HandleAsync(
            new RecordComplimentaryIssueCommand(product.Id, unit.Id, Fixture.TestBranchId, 6m, "دفعة ثانية"), CancellationToken.None);

        // 6 + 6 = 12 > 10 - تنجح بس تُعلَّم.
        Assert.True(secondResult.IsSuccess);
        Assert.True(secondResult.Value.FlaggedForReview);
    }

    [Fact]
    public async Task ضيافة_لمنتج_غير_مفعَّل_للضيافة_تُرفض_بقاعدة_عمل_واضحة()
    {
        using var scope = CreateScope();
        await TestDataBuilder.ActAsAdminAsync(scope, Fixture);
        var db = CreateDbContext(scope);
        // isComplimentaryAllowed: false (الافتراضي) - القيد الوحيد اللي
        // *لازم* يمنع العملية فعليًا: قرار إداري واعٍ غير موجود أصلًا.
        var (product, unit) = await TestDataBuilder.CreateActiveProductAsync(db, "منتج بلا إذن ضيافة");
        await TestDataBuilder.SetStockAsync(db, product.Id, Fixture.TestBranchId, 50m);

        var handler = scope.ServiceProvider.GetRequiredService<RecordComplimentaryIssueHandler>();
        var result = await handler.HandleAsync(
            new RecordComplimentaryIssueCommand(product.Id, unit.Id, Fixture.TestBranchId, 1m, null), CancellationToken.None);

        Assert.True(result.IsFailure);
        Assert.Equal(ErrorType.BusinessRule, result.Error!.Type);
        Assert.Equal("Complimentary.NotAllowedForProduct", result.Error.Code);
    }

    [Fact]
    public async Task ضيافة_بكمية_صفر_أو_سالبة_تفشل_بخطأ_تحقق()
    {
        using var scope = CreateScope();
        await TestDataBuilder.ActAsAdminAsync(scope, Fixture);
        var db = CreateDbContext(scope);
        var (product, unit) = await TestDataBuilder.CreateActiveProductAsync(db, "منتج ضيافة كمية غلط", isComplimentaryAllowed: true);

        var handler = scope.ServiceProvider.GetRequiredService<RecordComplimentaryIssueHandler>();
        var result = await handler.HandleAsync(
            new RecordComplimentaryIssueCommand(product.Id, unit.Id, Fixture.TestBranchId, 0m, null), CancellationToken.None);

        Assert.True(result.IsFailure);
        Assert.Equal(ErrorType.Validation, result.Error!.Type);
        Assert.Equal("Complimentary.QuantityMustBePositive", result.Error.Code);
    }
}
