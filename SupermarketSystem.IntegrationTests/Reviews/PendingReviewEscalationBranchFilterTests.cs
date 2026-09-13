using Microsoft.Extensions.DependencyInjection;
using SupermarketSystem.Application.Inventory.RecordComplimentaryIssue;
using SupermarketSystem.Application.Reviews.GetPendingReviews;
using SupermarketSystem.IntegrationTests.Common;
using Xunit;

namespace SupermarketSystem.IntegrationTests.Reviews;

/// <summary>
/// فجوة حقيقية مكتشفة بميزة مشحونة أصلًا: PendingReviewEscalationBackgroundService
/// (خدمة خلفية بلا HttpContext) كانت تستدعي GetPendingReviewsHandler بلا
/// ignoreBranchFilter=true، فمرشِّح الفرع العام (راجع AppDbContext.SetBranchFilter)
/// كان يرجّع صفر صفوف دايمًا (BranchId=null دايمًا بخدمة خلفية) - يعني التصعيد
/// ما كان يشتغل إطلاقًا بصمت منذ إضافته. هذا الاختبار يثبّت السلوكين معًا:
/// endpoint المراجعات بالويب (ignoreBranchFilter=false، الافتراضي) لازم يضل
/// يحترم فرع المستخدم بالضبط، وخدمة التصعيد (ignoreBranchFilter=true) لازم
/// تشوف كل الفروع بغض النظر عن HttpContext.
/// </summary>
[Collection(DatabaseCollection.Name)]
public sealed class PendingReviewEscalationBranchFilterTests : IntegrationTestBase
{
    public PendingReviewEscalationBranchFilterTests(DatabaseFixture fixture) : base(fixture) { }

    [Fact]
    public async Task بلا_HttpContext_ignoreBranchFilter_false_يرجع_صفر_رغم_وجود_عنصر_فعلي()
    {
        using (var setupScope = CreateScope())
        {
            await TestDataBuilder.ActAsAdminAsync(setupScope, Fixture);
            var db = CreateDbContext(setupScope);
            var (product, unit) = await TestDataBuilder.CreateActiveProductAsync(
                db, "منتج ضيافة - فحص فلتر الفرع", isComplimentaryAllowed: true);
            await TestDataBuilder.SetStockAsync(db, product.Id, Fixture.TestBranchId, 100m);

            var issueHandler = setupScope.ServiceProvider.GetRequiredService<RecordComplimentaryIssueHandler>();
            var issueResult = await issueHandler.HandleAsync(
                new RecordComplimentaryIssueCommand(product.Id, unit.Id, Fixture.TestBranchId, Quantity: 15m, Reason: "اختبار"),
                CancellationToken.None);
            Assert.True(issueResult.IsSuccess);
            Assert.True(issueResult.Value.FlaggedForReview);
        }

        // Scope جديد بلا أي ActAs* - يحاكي بالضبط خدمة خلفية بلا HttpContext
        // (BranchId=null، IsCrossBranchAccessAllowed=false - نفس حالة
        // PendingReviewEscalationBackgroundService الفعلية).
        using var bareScope = CreateScope();
        var reviewsHandler = bareScope.ServiceProvider.GetRequiredService<GetPendingReviewsHandler>();

        var withoutIgnore = await reviewsHandler.HandleAsync(CancellationToken.None, ignoreBranchFilter: false);
        Assert.DoesNotContain(withoutIgnore.Items, i => i.Type == PendingReviewType.ComplimentaryIssue);

        var withIgnore = await reviewsHandler.HandleAsync(CancellationToken.None, ignoreBranchFilter: true);
        Assert.Contains(withIgnore.Items, i => i.Type == PendingReviewType.ComplimentaryIssue);
    }
}
