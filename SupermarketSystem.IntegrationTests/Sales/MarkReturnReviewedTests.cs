using Microsoft.Extensions.DependencyInjection;
using SupermarketSystem.Application.Common.Results;
using SupermarketSystem.Application.Sales.CompleteSale;
using SupermarketSystem.Application.Sales.MarkReturnReviewed;
using SupermarketSystem.Application.Sales.ProcessReturn;
using SupermarketSystem.Domain.Sales;
using SupermarketSystem.Infrastructure.Persistence;
using SupermarketSystem.IntegrationTests.Common;
using Microsoft.EntityFrameworkCore;
using Xunit;

namespace SupermarketSystem.IntegrationTests.Sales;

/// <summary>
/// MarkReturnReviewedHandler — إجراء إداري بحت، ما بيغيّر أي شي مالي ولا
/// حالة الإرجاع، بس يعلّم "تمت المراجعة" (راجع تعليق الـHandler).
/// </summary>
[Collection(DatabaseCollection.Name)]
public sealed class MarkReturnReviewedTests : IntegrationTestBase
{
    public MarkReturnReviewedTests(DatabaseFixture fixture) : base(fixture) { }

    /// <summary>
    /// Scope منفصل لكل خطوة (بيع، ثم إرجاع) - نفس سبب VoidSaleTests
    /// بالضبط: خصم المخزون الذري (TryDecreaseAsync، SQL خام) وقت البيع
    /// بيغيّر RowVersion الفعلي بقاعدة البيانات بلا ما يحدّث أي كيان
    /// Stock متتبَّع بنفس الـContext - لو الإرجاع (اللي بيحمّل/يعدّل Stock
    /// عبر EF عادي) صار بنفس الـScope، بيصطدم بـDbUpdateConcurrencyException
    /// زائف. راجع تعليق CompleteASaleAsync بـVoidSaleTests.cs لتفاصيل أكتر.
    /// </summary>
    private async Task<Guid> CreateAReturnAsync()
    {
        Guid saleInvoiceId;
        Guid saleItemId;

        using (var saleScope = CreateScope())
        {
            await TestDataBuilder.ActAsAdminAsync(saleScope, Fixture);
            var saleDb = CreateDbContext(saleScope);
            var (product, unit) = await TestDataBuilder.CreateActiveProductAsync(saleDb, "منتج لإرجاع مراجعة");
            await TestDataBuilder.CreateProductBranchAsync(saleDb, product.Id, Fixture.TestBranchId, 10m);
            await TestDataBuilder.SetStockAsync(saleDb, product.Id, Fixture.TestBranchId, 20m);

            var saleHandler = saleScope.ServiceProvider.GetRequiredService<CompleteSaleHandler>();
            var sale = await saleHandler.HandleAsync(
                new CompleteSaleCommand(
                    Fixture.TestBranchId, Guid.NewGuid(), null, 0m,
                    new[] { new CompleteSaleItemDto(product.Id, unit.Id, 2m, 0m, null) },
                    new[] { new CompleteSalePaymentDto(TestDataBuilder.CashPaymentMethodId, 20m, null, Guid.NewGuid()) }),
                CancellationToken.None);
            Assert.True(sale.IsSuccess);

            saleInvoiceId = sale.Value.SaleInvoiceId;
            saleItemId = await saleDb.SaleInvoiceItems.AsNoTracking()
                .Where(i => i.SaleInvoiceId == saleInvoiceId).Select(i => i.Id).FirstAsync();
        }

        using var returnScope = CreateScope();
        await TestDataBuilder.ActAsAdminAsync(returnScope, Fixture);

        var returnHandler = returnScope.ServiceProvider.GetRequiredService<ProcessReturnHandler>();
        var returnResult = await returnHandler.HandleAsync(
            new ProcessReturnCommand(
                saleInvoiceId, Guid.NewGuid(), ReturnReason.Other, null,
                new[] { new ProcessReturnItemDto(saleItemId, 1m) },
                new[] { new ProcessReturnPaymentDto(TestDataBuilder.CashPaymentMethodId, 10m, null, Guid.NewGuid()) }),
            CancellationToken.None);

        Assert.True(returnResult.IsSuccess);
        return returnResult.Value.ReturnInvoiceId;
    }

    [Fact]
    public async Task تعليم_إرجاع_كمُراجَع_ينجح_ولا_يغيّر_أي_قيمة_مالية_أو_حالة()
    {
        var returnInvoiceId = await CreateAReturnAsync();

        using var scope = CreateScope();
        await TestDataBuilder.ActAsAdminAsync(scope, Fixture);
        var db = CreateDbContext(scope);

        var totalBefore = (await db.ReturnInvoices.AsNoTracking().FirstAsync(r => r.Id == returnInvoiceId)).TotalAmount;

        var handler = scope.ServiceProvider.GetRequiredService<MarkReturnReviewedHandler>();
        var result = await handler.HandleAsync(new MarkReturnReviewedCommand(returnInvoiceId), CancellationToken.None);

        Assert.True(result.IsSuccess);
        Assert.Equal(returnInvoiceId, result.Value.ReturnInvoiceId);

        var afterMark = await db.ReturnInvoices.AsNoTracking().FirstAsync(r => r.Id == returnInvoiceId);
        Assert.NotNull(afterMark.ReviewedAtUtc);
        Assert.Equal(totalBefore, afterMark.TotalAmount);
    }

    [Fact]
    public async Task تعليم_نفس_الإرجاع_مرتين_يفشل_بتعارض_واضح()
    {
        var returnInvoiceId = await CreateAReturnAsync();

        using var scope = CreateScope();
        await TestDataBuilder.ActAsAdminAsync(scope, Fixture);

        var handler = scope.ServiceProvider.GetRequiredService<MarkReturnReviewedHandler>();
        var first = await handler.HandleAsync(new MarkReturnReviewedCommand(returnInvoiceId), CancellationToken.None);
        Assert.True(first.IsSuccess);

        var second = await handler.HandleAsync(new MarkReturnReviewedCommand(returnInvoiceId), CancellationToken.None);

        Assert.True(second.IsFailure);
        Assert.Equal(ErrorType.Conflict, second.Error!.Type);
        Assert.Equal("Return.AlreadyReviewed", second.Error.Code);
    }

    [Fact]
    public async Task تعليم_إرجاع_غير_موجود_يفشل_بـNotFound()
    {
        using var scope = CreateScope();
        await TestDataBuilder.ActAsAdminAsync(scope, Fixture);
        var handler = scope.ServiceProvider.GetRequiredService<MarkReturnReviewedHandler>();

        var result = await handler.HandleAsync(new MarkReturnReviewedCommand(Guid.NewGuid()), CancellationToken.None);

        Assert.True(result.IsFailure);
        Assert.Equal(ErrorType.NotFound, result.Error!.Type);
    }
}
