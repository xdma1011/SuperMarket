using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using SupermarketSystem.Application.Common.Results;
using SupermarketSystem.Application.Sales.CompleteSale;
using SupermarketSystem.Application.Sales.VoidSale;
using SupermarketSystem.Domain.Sales;
using SupermarketSystem.Infrastructure.Persistence;
using SupermarketSystem.IntegrationTests.Common;
using Xunit;

namespace SupermarketSystem.IntegrationTests.Sales;

/// <summary>
/// VoidSaleHandler — التركيز على انعكاس المخزون والدفعات وحركة الدرج
/// بالضبط بمقدار البيعة الأصلية (راجع تعليق الـHandler: يعكس الحركات
/// المسجَّلة فعليًا لا يعيد حسابها من الأسطر).
/// </summary>
[Collection(DatabaseCollection.Name)]
public sealed class VoidSaleTests : IntegrationTestBase
{
    public VoidSaleTests(DatabaseFixture fixture) : base(fixture) { }

    /// <summary>
    /// عمدًا بـScope خاص فيها لحالها (يُغلق قبل ما ترجع) - لو استخدمنا نفس
    /// الـScope لعملية البيع وعملية الإلغاء اللاحقة، AppDbContext الواحد
    /// بيضل متتبِّع (tracking) نسخة الفاتورة من لحظة إنشائها، وأي حركة SQL
    /// خام لاحقة تُغيّر RowVersion الفعلي بقاعدة البيانات (زي
    /// IStockOperations.TryDecreaseAsync، راجع CLAUDE.md §3.3) ما بتنعكس
    /// على القيمة المتتبَّعة بالذاكرة - أول SaveChanges لاحق على نفس الصف
    /// بيفشل بـDbUpdateConcurrencyException زائفة ("0 rows affected")
    /// رغم إن العملية الحقيقية عبر HTTP (Scope منفصل تلقائيًا لكل طلب)
    /// ما ممكن تواجه هذا إطلاقًا. Scope منفصل هون يحاكي الواقع الحقيقي
    /// (كل طلب HTTP = Scope جديد) بدل ما يخترع فشلًا وهميًا خاصًا بالاختبار.
    /// </summary>
    private async Task<(Guid SaleInvoiceId, Guid ProductId)> CompleteASaleAsync(
        decimal price = 10m, decimal quantity = 3m, decimal initialStock = 50m)
    {
        using var scope = CreateScope();
        await TestDataBuilder.ActAsAdminAsync(scope, Fixture);
        var db = CreateDbContext(scope);
        var (product, unit) = await TestDataBuilder.CreateActiveProductAsync(db, "منتج للإلغاء");
        await TestDataBuilder.CreateProductBranchAsync(db, product.Id, Fixture.TestBranchId, price);
        await TestDataBuilder.SetStockAsync(db, product.Id, Fixture.TestBranchId, initialStock);

        var saleHandler = scope.ServiceProvider.GetRequiredService<CompleteSaleHandler>();
        var saleResult = await saleHandler.HandleAsync(
            new CompleteSaleCommand(
                Fixture.TestBranchId, Guid.NewGuid(), null, 0m,
                new[] { new CompleteSaleItemDto(product.Id, unit.Id, quantity, 0m, null) },
                new[] { new CompleteSalePaymentDto(TestDataBuilder.CashPaymentMethodId, price * quantity, null, Guid.NewGuid()) }),
            CancellationToken.None);

        Assert.True(saleResult.IsSuccess);
        return (saleResult.Value.SaleInvoiceId, product.Id);
    }

    [Fact]
    public async Task إلغاء_فاتورة_مكتملة_يعكس_المخزون_والدفعات_وحركة_الدرج()
    {
        var (saleInvoiceId, productId) = await CompleteASaleAsync(price: 10m, quantity: 3m, initialStock: 50m);

        using var scope = CreateScope();
        await TestDataBuilder.ActAsAdminAsync(scope, Fixture);
        var db = CreateDbContext(scope);

        var stockBeforeVoid = await db.Stocks.AsNoTracking().FirstAsync(s => s.ProductId == productId);
        Assert.Equal(47m, stockBeforeVoid.QuantityOnHand);

        var voidHandler = scope.ServiceProvider.GetRequiredService<VoidSaleHandler>();
        var result = await voidHandler.HandleAsync(
            new VoidSaleCommand(saleInvoiceId, VoidReason.CashierError, "خطأ بالكاشير"), CancellationToken.None);

        Assert.True(result.IsSuccess);
        Assert.Equal(1, result.Value.StockMovementsReversed);
        Assert.Equal(1, result.Value.PaymentsReversed);
        Assert.Equal(30m, result.Value.CashReturnedToDrawer);

        var stockAfterVoid = await db.Stocks.AsNoTracking().FirstAsync(s => s.ProductId == productId);
        Assert.Equal(50m, stockAfterVoid.QuantityOnHand);

        var invoice = await db.SaleInvoices.AsNoTracking().FirstAsync(s => s.Id == saleInvoiceId);
        Assert.Equal(SaleInvoiceStatus.Voided, invoice.Status);
    }

    [Fact]
    public async Task إلغاء_فاتورة_ملغاة_مسبقًا_يفشل()
    {
        var (saleInvoiceId, _) = await CompleteASaleAsync();

        using var scope = CreateScope();
        await TestDataBuilder.ActAsAdminAsync(scope, Fixture);

        var voidHandler = scope.ServiceProvider.GetRequiredService<VoidSaleHandler>();
        var firstVoid = await voidHandler.HandleAsync(
            new VoidSaleCommand(saleInvoiceId, VoidReason.CashierError, null), CancellationToken.None);
        Assert.True(firstVoid.IsSuccess);

        var secondVoid = await voidHandler.HandleAsync(
            new VoidSaleCommand(saleInvoiceId, VoidReason.CashierError, null), CancellationToken.None);

        Assert.True(secondVoid.IsFailure);
        Assert.Equal(ErrorType.BusinessRule, secondVoid.Error!.Type);
        Assert.Equal("Sale.NotVoidable", secondVoid.Error.Code);
    }

    [Fact]
    public async Task إلغاء_فاتورة_غير_موجودة_يفشل_بـNotFound()
    {
        using var scope = CreateScope();
        await TestDataBuilder.ActAsAdminAsync(scope, Fixture);
        var voidHandler = scope.ServiceProvider.GetRequiredService<VoidSaleHandler>();

        var result = await voidHandler.HandleAsync(
            new VoidSaleCommand(Guid.NewGuid(), VoidReason.CashierError, null), CancellationToken.None);

        Assert.True(result.IsFailure);
        Assert.Equal(ErrorType.NotFound, result.Error!.Type);
        Assert.Equal("Sale.NotFound", result.Error.Code);
    }
}
