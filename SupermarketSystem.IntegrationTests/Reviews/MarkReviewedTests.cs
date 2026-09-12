using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using SupermarketSystem.Application.Common.Results;
using SupermarketSystem.Application.Inventory.RecordComplimentaryIssue;
using SupermarketSystem.Application.Purchasing.CompletePurchaseInvoice;
using SupermarketSystem.Application.Reviews.MarkComplaintReviewed;
using SupermarketSystem.Application.Reviews.MarkPurchaseInvoiceItemReviewed;
using SupermarketSystem.Application.Reviews.MarkStockMovementReviewed;
using SupermarketSystem.Domain.Customers;
using SupermarketSystem.Infrastructure.Persistence;
using SupermarketSystem.IntegrationTests.Common;
using Xunit;

namespace SupermarketSystem.IntegrationTests.Reviews;

/// <summary>MarkStockMovementReviewedHandler / MarkPurchaseInvoiceItemReviewedHandler / MarkComplaintReviewedHandler - نفس النمط بالضبط بكل الثلاثة (راجع تعليقات الـHandlers).</summary>
[Collection(DatabaseCollection.Name)]
public sealed class MarkReviewedTests : IntegrationTestBase
{
    public MarkReviewedTests(DatabaseFixture fixture) : base(fixture) { }

    [Fact]
    public async Task تعليم_حركة_مخزون_ضيافة_كمُراجَعة_ينجح_ويمنع_التكرار()
    {
        using var scope = CreateScope();
        await TestDataBuilder.ActAsAdminAsync(scope, Fixture);
        var db = CreateDbContext(scope);
        var (product, unit) = await TestDataBuilder.CreateActiveProductAsync(db, "منتج ضيافة", isComplimentaryAllowed: true);
        await TestDataBuilder.SetStockAsync(db, product.Id, Fixture.TestBranchId, 100m);

        var issueHandler = scope.ServiceProvider.GetRequiredService<RecordComplimentaryIssueHandler>();
        var issue = await issueHandler.HandleAsync(
            new RecordComplimentaryIssueCommand(product.Id, unit.Id, Fixture.TestBranchId, 20m, "اختبار"), CancellationToken.None);
        Assert.True(issue.IsSuccess);
        Assert.True(issue.Value.FlaggedForReview);

        var markHandler = scope.ServiceProvider.GetRequiredService<MarkStockMovementReviewedHandler>();
        var firstMark = await markHandler.HandleAsync(new MarkStockMovementReviewedCommand(issue.Value.StockMovementId), CancellationToken.None);
        Assert.True(firstMark.IsSuccess);

        var secondMark = await markHandler.HandleAsync(new MarkStockMovementReviewedCommand(issue.Value.StockMovementId), CancellationToken.None);
        Assert.True(secondMark.IsFailure);
        Assert.Equal(ErrorType.Conflict, secondMark.Error!.Type);
    }

    [Fact]
    public async Task تعليم_سطر_شراء_مرتفع_السعر_كمُراجَع_ينجح()
    {
        using var scope = CreateScope();
        await TestDataBuilder.ActAsAdminAsync(scope, Fixture);
        var db = CreateDbContext(scope);
        var (product, unit) = await TestDataBuilder.CreateActiveProductAsync(db, "منتج شراء غالي");
        var supplier = await TestDataBuilder.CreateSupplierAsync(db);

        // شراء أول بسعر عادي (10) يبني "تاريخ الشراء" اللي المقارنة لاحقًا تعتمد عليه.
        var purchaseHandler = scope.ServiceProvider.GetRequiredService<CompletePurchaseInvoiceHandler>();
        var firstPurchase = await purchaseHandler.HandleAsync(
            new CompletePurchaseInvoiceCommand(
                Fixture.TestBranchId, supplier.Id, null,
                new[] { new CompletePurchaseInvoiceItemDto(product.Id, unit.Id, 10m, 10m, null, null, null) }),
            CancellationToken.None);
        Assert.True(firstPurchase.IsSuccess);

        // شراء ثاني بسعر أعلى بكثير (30، أعلى من 10 + 15% الافتراضي) - يُعلَّم NeedsReview.
        var secondPurchase = await purchaseHandler.HandleAsync(
            new CompletePurchaseInvoiceCommand(
                Fixture.TestBranchId, supplier.Id, null,
                new[] { new CompletePurchaseInvoiceItemDto(product.Id, unit.Id, 5m, 30m, null, null, null) }),
            CancellationToken.None);
        Assert.True(secondPurchase.IsSuccess);

        var flaggedItemId = await db.PurchaseInvoiceItems.AsNoTracking()
            .Where(i => i.PurchaseInvoiceId == secondPurchase.Value.PurchaseInvoiceId)
            .Select(i => i.Id)
            .FirstAsync();

        var markHandler = scope.ServiceProvider.GetRequiredService<MarkPurchaseInvoiceItemReviewedHandler>();
        var result = await markHandler.HandleAsync(new MarkPurchaseInvoiceItemReviewedCommand(flaggedItemId), CancellationToken.None);

        Assert.True(result.IsSuccess);
    }

    [Fact]
    public async Task تعليم_شكوى_كمحلولة_ينجح_ويمنع_التكرار()
    {
        using var scope = CreateScope();
        await TestDataBuilder.ActAsAdminAsync(scope, Fixture);
        var db = CreateDbContext(scope);
        var customer = await TestDataBuilder.CreateCustomerAsync(db);
        var complaint = new Complaint(customer.Id, orderId: null, "الطلب متأخر");
        db.Complaints.Add(complaint);
        await db.SaveChangesAsync();

        var handler = scope.ServiceProvider.GetRequiredService<MarkComplaintReviewedHandler>();
        var first = await handler.HandleAsync(new MarkComplaintReviewedCommand(complaint.Id), CancellationToken.None);
        Assert.True(first.IsSuccess);

        var second = await handler.HandleAsync(new MarkComplaintReviewedCommand(complaint.Id), CancellationToken.None);
        Assert.True(second.IsFailure);
        Assert.Equal(ErrorType.Conflict, second.Error!.Type);
    }

    [Fact]
    public async Task تعليم_شكوى_غير_موجودة_يفشل_بـNotFound()
    {
        using var scope = CreateScope();
        await TestDataBuilder.ActAsAdminAsync(scope, Fixture);
        var handler = scope.ServiceProvider.GetRequiredService<MarkComplaintReviewedHandler>();

        var result = await handler.HandleAsync(new MarkComplaintReviewedCommand(Guid.NewGuid()), CancellationToken.None);

        Assert.True(result.IsFailure);
        Assert.Equal(ErrorType.NotFound, result.Error!.Type);
    }
}
