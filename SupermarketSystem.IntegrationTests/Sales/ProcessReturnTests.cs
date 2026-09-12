using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using SupermarketSystem.Application.Common.Results;
using SupermarketSystem.Application.Sales.CompleteSale;
using SupermarketSystem.Application.Sales.ProcessReturn;
using SupermarketSystem.Domain.Sales;
using SupermarketSystem.Infrastructure.Persistence;
using SupermarketSystem.IntegrationTests.Common;
using Xunit;

namespace SupermarketSystem.IntegrationTests.Sales;

/// <summary>
/// ProcessReturnHandler — السعر يُؤخذ من لقطة البيع الأصلية
/// (UnitPriceSnapshot)، لا من سعر المنتج الحالي، والمخزون يرجع لنفس
/// الدفعة الأصلية (راجع تعليق الـHandler).
/// </summary>
[Collection(DatabaseCollection.Name)]
public sealed class ProcessReturnTests : IntegrationTestBase
{
    public ProcessReturnTests(DatabaseFixture fixture) : base(fixture) { }

    /// <summary>
    /// Scope خاص فيها لحالها، يُغلق قبل ما ترجع - راجع تعليق نظيرتها
    /// بـVoidSaleTests.cs (نفس السبب بالضبط: تفادي RowVersion زائف الفشل
    /// بسبب خصم المخزون الذري عبر SQL خام بنفس الـScope المتتبِّع).
    /// </summary>
    private async Task<(Guid SaleInvoiceId, Guid SaleInvoiceItemId, Guid ProductId)> CompleteASaleAsync(
        decimal price = 20m, decimal quantity = 4m, decimal initialStock = 50m)
    {
        using var scope = CreateScope();
        await TestDataBuilder.ActAsAdminAsync(scope, Fixture);
        var db = CreateDbContext(scope);
        var (product, unit) = await TestDataBuilder.CreateActiveProductAsync(db, "منتج للإرجاع");
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

        var saleInvoiceItemId = await db.SaleInvoiceItems.AsNoTracking()
            .Where(i => i.SaleInvoiceId == saleResult.Value.SaleInvoiceId)
            .Select(i => i.Id)
            .FirstAsync();

        return (saleResult.Value.SaleInvoiceId, saleInvoiceItemId, product.Id);
    }

    [Fact]
    public async Task إرجاع_جزئي_يعيد_البضاعة_للمخزون_ويحدّث_حالة_الفاتورة_لإرجاع_جزئي()
    {
        var (saleInvoiceId, saleInvoiceItemId, productId) = await CompleteASaleAsync(price: 20m, quantity: 4m, initialStock: 50m);

        using var scope = CreateScope();
        await TestDataBuilder.ActAsAdminAsync(scope, Fixture);
        var db = CreateDbContext(scope);

        // نغيّر سعر المنتج الحالي بعد البيع - الإرجاع لازم يعتمد لقطة
        // البيع الأصلية (20)، لا هذا السعر الجديد (999).
        var productBranch = await db.ProductBranches.FirstAsync(pb => pb.ProductId == productId);
        productBranch.ChangePrice(999m);
        await db.SaveChangesAsync();

        var returnHandler = scope.ServiceProvider.GetRequiredService<ProcessReturnHandler>();
        var result = await returnHandler.HandleAsync(
            new ProcessReturnCommand(
                saleInvoiceId, Guid.NewGuid(), ReturnReason.CustomerChangedMind, "غيّر رأيه",
                new[] { new ProcessReturnItemDto(saleInvoiceItemId, Quantity: 1m) },
                new[] { new ProcessReturnPaymentDto(TestDataBuilder.CashPaymentMethodId, 20m, null, Guid.NewGuid()) }),
            CancellationToken.None);

        Assert.True(result.IsSuccess);
        Assert.False(result.Value.WasReplay);
        // 1 وحدة بسعر البيع الأصلي (20) لا السعر الحالي (999).
        Assert.Equal(20m, result.Value.TotalAmount);
        Assert.Equal(SaleInvoiceStatus.PartiallyReturned, result.Value.OriginalInvoiceNewStatus);

        var stockAfter = await db.Stocks.AsNoTracking().FirstAsync(s => s.ProductId == productId);
        // 50 - 4 (بيع) + 1 (إرجاع) = 47
        Assert.Equal(47m, stockAfter.QuantityOnHand);
    }

    [Fact]
    public async Task إرجاع_كامل_الكمية_يحوّل_حالة_الفاتورة_الأصلية_لإرجاع_كامل()
    {
        var (saleInvoiceId, saleInvoiceItemId, _) = await CompleteASaleAsync(price: 20m, quantity: 4m);

        using var scope = CreateScope();
        await TestDataBuilder.ActAsAdminAsync(scope, Fixture);

        var returnHandler = scope.ServiceProvider.GetRequiredService<ProcessReturnHandler>();
        var result = await returnHandler.HandleAsync(
            new ProcessReturnCommand(
                saleInvoiceId, Guid.NewGuid(), ReturnReason.Defective, null,
                new[] { new ProcessReturnItemDto(saleInvoiceItemId, Quantity: 4m) },
                new[] { new ProcessReturnPaymentDto(TestDataBuilder.CashPaymentMethodId, 80m, null, Guid.NewGuid()) }),
            CancellationToken.None);

        Assert.True(result.IsSuccess);
        Assert.Equal(SaleInvoiceStatus.FullyReturned, result.Value.OriginalInvoiceNewStatus);
    }

    [Fact]
    public async Task إرجاع_كمية_أكبر_من_المباعة_يفشل_بقاعدة_عمل_واضحة()
    {
        var (saleInvoiceId, saleInvoiceItemId, _) = await CompleteASaleAsync(price: 20m, quantity: 4m);

        using var scope = CreateScope();
        await TestDataBuilder.ActAsAdminAsync(scope, Fixture);

        var returnHandler = scope.ServiceProvider.GetRequiredService<ProcessReturnHandler>();
        var result = await returnHandler.HandleAsync(
            new ProcessReturnCommand(
                saleInvoiceId, Guid.NewGuid(), ReturnReason.Other, null,
                new[] { new ProcessReturnItemDto(saleInvoiceItemId, Quantity: 5m) },
                Array.Empty<ProcessReturnPaymentDto>()),
            CancellationToken.None);

        Assert.True(result.IsFailure);
        Assert.Equal(ErrorType.BusinessRule, result.Error!.Type);
        Assert.Equal("Return.ExceedsSoldQuantity", result.Error.Code);
    }

    [Fact]
    public async Task إرسال_نفس_ClientRequestId_مرتين_للإرجاع_يرجّع_نفس_النتيجة_بلا_تكرار()
    {
        var (saleInvoiceId, saleInvoiceItemId, productId) = await CompleteASaleAsync(price: 20m, quantity: 4m);

        using var scope = CreateScope();
        await TestDataBuilder.ActAsAdminAsync(scope, Fixture);
        var db = CreateDbContext(scope);

        var returnHandler = scope.ServiceProvider.GetRequiredService<ProcessReturnHandler>();
        var clientRequestId = Guid.NewGuid();
        var command = new ProcessReturnCommand(
            saleInvoiceId, clientRequestId, ReturnReason.Defective, null,
            new[] { new ProcessReturnItemDto(saleInvoiceItemId, Quantity: 1m) },
            new[] { new ProcessReturnPaymentDto(TestDataBuilder.CashPaymentMethodId, 20m, null, Guid.NewGuid()) });

        var first = await returnHandler.HandleAsync(command, CancellationToken.None);
        Assert.True(first.IsSuccess);

        var second = await returnHandler.HandleAsync(command, CancellationToken.None);
        Assert.True(second.IsSuccess);
        Assert.True(second.Value.WasReplay);
        Assert.Equal(first.Value.ReturnInvoiceId, second.Value.ReturnInvoiceId);

        var returnCount = await db.ReturnInvoices.CountAsync(r => r.ClientRequestId == clientRequestId);
        Assert.Equal(1, returnCount);

        // المخزون رجع مرة وحدة بس (47 = 50 - 4 + 1)، لا مرتين.
        var stockAfter = await db.Stocks.AsNoTracking().FirstAsync(s => s.ProductId == productId);
        Assert.Equal(47m, stockAfter.QuantityOnHand);
    }
}
