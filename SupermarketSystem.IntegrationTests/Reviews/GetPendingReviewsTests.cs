using Microsoft.Extensions.DependencyInjection;
using SupermarketSystem.Application.Inventory.RecordComplimentaryIssue;
using SupermarketSystem.Application.Reviews.GetPendingReviews;
using SupermarketSystem.IntegrationTests.Common;
using Xunit;

namespace SupermarketSystem.IntegrationTests.Reviews;

/// <summary>
/// GetPendingReviewsHandler — نقطة تجميع موحَّدة لكل شي "بانتظار مراجعة".
/// نختبرها هون عبر مصدر ضيافة (ComplimentaryIssue) لأنه أسهل مصدر نطلعه
/// ذاتيًا بلا الحاجة لتسلسل عمليات طويل (بيع ثم إرجاع).
/// </summary>
[Collection(DatabaseCollection.Name)]
public sealed class GetPendingReviewsTests : IntegrationTestBase
{
    public GetPendingReviewsTests(DatabaseFixture fixture) : base(fixture) { }

    [Fact]
    public async Task ضيافة_تجاوزت_الحد_تظهر_بقائمة_المراجعات_المعلَّقة()
    {
        using var scope = CreateScope();
        await TestDataBuilder.ActAsAdminAsync(scope, Fixture);
        var db = CreateDbContext(scope);
        var (product, unit) = await TestDataBuilder.CreateActiveProductAsync(db, "منتج ضيافة للمراجعة", isComplimentaryAllowed: true);
        await TestDataBuilder.SetStockAsync(db, product.Id, Fixture.TestBranchId, 100m);

        var issueHandler = scope.ServiceProvider.GetRequiredService<RecordComplimentaryIssueHandler>();
        var issueResult = await issueHandler.HandleAsync(
            new RecordComplimentaryIssueCommand(product.Id, unit.Id, Fixture.TestBranchId, Quantity: 15m, Reason: "اختبار"),
            CancellationToken.None);
        Assert.True(issueResult.IsSuccess);
        Assert.True(issueResult.Value.FlaggedForReview);

        var reviewsHandler = scope.ServiceProvider.GetRequiredService<GetPendingReviewsHandler>();
        var result = await reviewsHandler.HandleAsync(CancellationToken.None);

        var complimentaryReview = Assert.Single(result.Items, i => i.Type == PendingReviewType.ComplimentaryIssue);
        Assert.Equal("ضيافة", complimentaryReview.TypeTitle);
        Assert.Equal("منتج ضيافة للمراجعة", complimentaryReview.Title);
    }

    [Fact]
    public async Task ضيافة_ضمن_الحد_تظهر_أيضًا_بقائمة_المراجعات_بوصف_مختلف_عن_المتجاوزة()
    {
        // فجوة حقيقية أصلحها Agent B (commit dbd5529): الفلتر كان NeedsReview
        // فقط، فالضيافة ضمن الحد اليومي ما كانت تظهر أبدًا بصفحة المراجعات
        // حتى لو صاحب المحل فتحها كل يوم - راجع تعليق GetPendingReviewsQuery.cs.
        // الفلتر الصحيح الآن: أي تعديل مخزون يدوي (ManualAdjustment) غير
        // مُراجَع يظهر، والتمييز بين "متجاوز الحد" و"ضمن الحد" صار بنص
        // Detail فقط، لا بالظهور/الإخفاء.
        using var scope = CreateScope();
        await TestDataBuilder.ActAsAdminAsync(scope, Fixture);
        var db = CreateDbContext(scope);
        var (product, unit) = await TestDataBuilder.CreateActiveProductAsync(db, "منتج ضيافة ضمن الحد", isComplimentaryAllowed: true);
        await TestDataBuilder.SetStockAsync(db, product.Id, Fixture.TestBranchId, 100m);

        var issueHandler = scope.ServiceProvider.GetRequiredService<RecordComplimentaryIssueHandler>();
        var issueResult = await issueHandler.HandleAsync(
            new RecordComplimentaryIssueCommand(product.Id, unit.Id, Fixture.TestBranchId, Quantity: 2m, Reason: "اختبار"),
            CancellationToken.None);
        Assert.True(issueResult.IsSuccess);
        Assert.False(issueResult.Value.FlaggedForReview);

        var reviewsHandler = scope.ServiceProvider.GetRequiredService<GetPendingReviewsHandler>();
        var result = await reviewsHandler.HandleAsync(CancellationToken.None);

        var withinLimitReview = Assert.Single(result.Items, i => i.Type == PendingReviewType.ComplimentaryIssue);
        Assert.Equal("تعديل مخزون يدوي بانتظار المراجعة", withinLimitReview.Detail);
    }
}
