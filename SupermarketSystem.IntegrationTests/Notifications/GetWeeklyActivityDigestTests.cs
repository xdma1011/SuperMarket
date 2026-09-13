using Microsoft.Extensions.DependencyInjection;
using SupermarketSystem.Application.Reporting.GetWeeklyActivityDigest;
using SupermarketSystem.Domain.Inventory;
using SupermarketSystem.Domain.Sales;
using SupermarketSystem.IntegrationTests.Reporting;
using Xunit;

namespace SupermarketSystem.IntegrationTests.Notifications;

/// <summary>
/// يغطّي فقط GetWeeklyActivityDigestHandler (الاستعلام المُجمِّع اللي
/// WeeklyActivityDigestBackgroundService بتستخدمه) — لا الخدمة الخلفية
/// نفسها (Timer-based، صعب اختبارها Integration بشكل موثوق، راجع نفس
/// المبدأ المتبع بعدم اختبار DailyBackupBackgroundService/
/// PendingReviewEscalationBackgroundService مباشرة).
/// </summary>
[Collection(DatabaseCollection.Name)]
public sealed class GetWeeklyActivityDigestTests : IntegrationTestBase
{
    public GetWeeklyActivityDigestTests(DatabaseFixture fixture) : base(fixture) { }

    [Fact]
    public async Task يجمع_إلغاء_البيع_والإرجاع_والحركة_اليدوية_ضمن_آخر_7_أيام_ويستثني_الأقدم()
    {
        using var scope = CreateScope();
        // بيانات الاختبار كلها كيانات Branch-owned، وGetWeeklyActivityDigestHandler
        // بيستخدم IgnoreQueryFilters عمدًا (راجع تعليقه) — تفعيل تجاوز
        // الفرع هون مطلوب فقط لعمليات القراءة الوسيطة اللي أدوات البناء
        // المشتركة بتنفّذها (مثل FindAsync بـVoidSaleAsync)، لا للـhandler نفسه.
        scope.ActAsCrossBranchUser();
        var db = CreateDbContext(scope);

        var categoryId = await ReportingTestDataBuilder.CreateCategoryAsync(db, "تصنيف");
        var (productId, unitId) = await ReportingTestDataBuilder.CreateProductAsync(db, categoryId, "منتج الملخّص الأسبوعي");

        var now = DateTime.UtcNow;
        var sinceUtc = now.AddDays(-7);
        var withinWindow = now.AddDays(-1);
        var outsideWindow = now.AddDays(-10);

        // === إلغاء بيع حديث (ضمن النافذة) — يُحتسب ===
        var recentSale = await ReportingTestDataBuilder.CreateCompletedSaleAsync(
            db, Fixture.TestBranchId, productId, unitId, quantity: 2, unitPrice: 10m);
        await ReportingTestDataBuilder.VoidSaleAsync(db, recentSale, Fixture.AdminUserId);

        // === إلغاء بيع قديم (قبل 10 أيام) — يُستثنى ===
        var oldSale = await ReportingTestDataBuilder.CreateCompletedSaleAsync(
            db, Fixture.TestBranchId, productId, unitId, quantity: 5, unitPrice: 100m);
        var trackedOldSale = await db.SaleInvoices.FindAsync(oldSale.Id) ?? throw new InvalidOperationException("Sale not found.");
        trackedOldSale.Void(Fixture.AdminUserId, outsideWindow, VoidReason.SystemError, "قديم - يجب أن يُستثنى");
        await db.SaveChangesAsync();

        // === إرجاع حديث (ضمن النافذة) — يُحتسب ===
        var saleForReturn = await ReportingTestDataBuilder.CreateCompletedSaleAsync(
            db, Fixture.TestBranchId, productId, unitId, quantity: 3, unitPrice: 5m);
        await ReportingTestDataBuilder.CreateReturnAsync(
            db, saleForReturn, productId, unitId, quantity: 1, unitPrice: 5m, ReturnReason.Defective);

        // === إرجاع قديم (قبل 10 أيام) — يُستثنى ===
        var oldSaleForReturn = await ReportingTestDataBuilder.CreateCompletedSaleAsync(
            db, Fixture.TestBranchId, productId, unitId, quantity: 3, unitPrice: 50m);
        await ReportingTestDataBuilder.CreateReturnAsync(
            db, oldSaleForReturn, productId, unitId, quantity: 2, unitPrice: 50m, ReturnReason.Other, createdAtUtc: outsideWindow);

        // === حركة مخزون يدوية حديثة (ضيافة - ReferenceType.ManualAdjustment) — تُحتسب ===
        db.StockMovements.Add(new StockMovement(
            productId, Fixture.TestBranchId, unitId, productBatchId: null,
            quantityBase: 4m, MovementType.ComplimentaryOut, reason: "ضيافة اختبار",
            occurredAtUtc: withinWindow, userId: Fixture.AdminUserId,
            referenceType: StockMovementReferenceType.ManualAdjustment, referenceId: Guid.NewGuid()));

        // === حركة مخزون يدوية قديمة (قبل 10 أيام) — تُستثنى ===
        db.StockMovements.Add(new StockMovement(
            productId, Fixture.TestBranchId, unitId, productBatchId: null,
            quantityBase: 99m, MovementType.ComplimentaryOut, reason: "ضيافة قديمة - يجب أن تُستثنى",
            occurredAtUtc: outsideWindow, userId: Fixture.AdminUserId,
            referenceType: StockMovementReferenceType.ManualAdjustment, referenceId: Guid.NewGuid()));

        // === حركة مخزون حديثة بنوع مرجعية مختلف (بيع عادي) — لازم تُستثنى دايمًا ===
        db.StockMovements.Add(new StockMovement(
            productId, Fixture.TestBranchId, unitId, productBatchId: null,
            quantityBase: 1000m, MovementType.SaleOut, reason: null,
            occurredAtUtc: withinWindow, userId: Fixture.AdminUserId,
            referenceType: StockMovementReferenceType.SaleInvoiceItem, referenceId: Guid.NewGuid()));

        await db.SaveChangesAsync();

        var handler = scope.ServiceProvider.GetRequiredService<GetWeeklyActivityDigestHandler>();
        var result = await handler.HandleAsync(new GetWeeklyActivityDigestQuery(sinceUtc), CancellationToken.None);

        Assert.Equal(1, result.VoidedSalesCount);
        Assert.Equal(20m, result.VoidedSalesTotalAmount); // 2 * 10
        Assert.Equal(1, result.ReturnsCount);
        Assert.Equal(5m, result.ReturnsTotalAmount); // 1 * 5
        Assert.Equal(1, result.ManualStockMovementsCount);
        Assert.Equal(4m, result.ManualStockMovementsTotalQuantity);
    }

    [Fact]
    public async Task بلا_أي_نشاط_بالنافذة_يرجع_أصفار()
    {
        using var scope = CreateScope();
        scope.ActAsCrossBranchUser();

        var handler = scope.ServiceProvider.GetRequiredService<GetWeeklyActivityDigestHandler>();
        var result = await handler.HandleAsync(
            new GetWeeklyActivityDigestQuery(DateTime.UtcNow.AddDays(-7)), CancellationToken.None);

        Assert.Equal(0, result.VoidedSalesCount);
        Assert.Equal(0m, result.VoidedSalesTotalAmount);
        Assert.Equal(0, result.ReturnsCount);
        Assert.Equal(0m, result.ReturnsTotalAmount);
        Assert.Equal(0, result.ManualStockMovementsCount);
        Assert.Equal(0m, result.ManualStockMovementsTotalQuantity);
    }
}
