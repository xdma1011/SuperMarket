using Microsoft.Extensions.DependencyInjection;
using SupermarketSystem.Application.Common.Pagination;
using SupermarketSystem.Application.Purchasing.CompletePurchaseInvoice;
using SupermarketSystem.Application.Reporting.GetSupplierPaymentDue;
using SupermarketSystem.IntegrationTests.Common;
using Xunit;

namespace SupermarketSystem.IntegrationTests.Reporting;

[Collection(DatabaseCollection.Name)]
public sealed class GetSupplierPaymentDueTests : IntegrationTestBase
{
    public GetSupplierPaymentDueTests(DatabaseFixture fixture) : base(fixture) { }

    [Fact]
    public async Task فاتورة_بتاريخ_استحقاق_ودين_متبقٍّ_تظهر_بالتقرير()
    {
        using var scope = CreateScope();
        await TestDataBuilder.ActAsAdminAsync(scope, Fixture);
        var db = CreateDbContext(scope);
        var (product, unit) = await TestDataBuilder.CreateActiveProductAsync(db, "منتج استحقاق مورد");
        var supplier = await TestDataBuilder.CreateSupplierAsync(db);
        var dueDate = DateOnly.FromDateTime(DateTime.UtcNow.AddDays(7));

        var purchaseHandler = scope.ServiceProvider.GetRequiredService<CompletePurchaseInvoiceHandler>();
        var purchaseResult = await purchaseHandler.HandleAsync(
            new CompletePurchaseInvoiceCommand(
                Fixture.TestBranchId, supplier.Id, "INV-DUE-RPT-1",
                new[] { new CompletePurchaseInvoiceItemDto(product.Id, unit.Id, 10m, 20m, null, null, null) },
                DueDate: dueDate),
            CancellationToken.None);
        Assert.True(purchaseResult.IsSuccess);

        var reportHandler = scope.ServiceProvider.GetRequiredService<GetSupplierPaymentDueHandler>();
        var result = await reportHandler.HandleAsync(
            new GetSupplierPaymentDueQuery(new PagedRequest(), Fixture.TestBranchId), CancellationToken.None);

        var item = Assert.Single(result.Items);
        Assert.Equal(purchaseResult.Value.InvoiceNumber, item.InvoiceNumber);
        Assert.Equal(200m, item.RemainingDebt);
        Assert.Equal(dueDate, item.DueDate);
        Assert.InRange(item.DaysRemaining, 6, 7);
    }

    [Fact]
    public async Task فاتورة_بلا_تاريخ_استحقاق_لا_تظهر_بالتقرير()
    {
        using var scope = CreateScope();
        await TestDataBuilder.ActAsAdminAsync(scope, Fixture);
        var db = CreateDbContext(scope);
        var (product, unit) = await TestDataBuilder.CreateActiveProductAsync(db, "منتج بلا استحقاق");
        var supplier = await TestDataBuilder.CreateSupplierAsync(db);

        var purchaseHandler = scope.ServiceProvider.GetRequiredService<CompletePurchaseInvoiceHandler>();
        var purchaseResult = await purchaseHandler.HandleAsync(
            new CompletePurchaseInvoiceCommand(
                Fixture.TestBranchId, supplier.Id, "INV-NO-DUE-1",
                new[] { new CompletePurchaseInvoiceItemDto(product.Id, unit.Id, 10m, 20m, null, null, null) }),
            CancellationToken.None);
        Assert.True(purchaseResult.IsSuccess);

        var reportHandler = scope.ServiceProvider.GetRequiredService<GetSupplierPaymentDueHandler>();
        var result = await reportHandler.HandleAsync(
            new GetSupplierPaymentDueQuery(new PagedRequest(), Fixture.TestBranchId), CancellationToken.None);

        Assert.Empty(result.Items);
    }

    [Fact]
    public async Task فاتورة_مسدَّدة_بالكامل_بتاريخ_استحقاق_لا_تظهر_بالتقرير()
    {
        using var scope = CreateScope();
        await TestDataBuilder.ActAsAdminAsync(scope, Fixture);
        var db = CreateDbContext(scope);
        var (product, unit) = await TestDataBuilder.CreateActiveProductAsync(db, "منتج استحقاق مسدَّد");
        var supplier = await TestDataBuilder.CreateSupplierAsync(db);
        var dueDate = DateOnly.FromDateTime(DateTime.UtcNow.AddDays(3));

        var purchaseHandler = scope.ServiceProvider.GetRequiredService<CompletePurchaseInvoiceHandler>();
        var purchaseResult = await purchaseHandler.HandleAsync(
            new CompletePurchaseInvoiceCommand(
                Fixture.TestBranchId, supplier.Id, "INV-DUE-PAID-1",
                new[] { new CompletePurchaseInvoiceItemDto(product.Id, unit.Id, 5m, 10m, null, null, null) },
                DueDate: dueDate),
            CancellationToken.None);
        Assert.True(purchaseResult.IsSuccess);

        var paymentHandler = scope.ServiceProvider
            .GetRequiredService<SupermarketSystem.Application.Purchasing.RecordPurchaseInvoicePayment.RecordPurchaseInvoicePaymentHandler>();
        var paymentResult = await paymentHandler.HandleAsync(
            new SupermarketSystem.Application.Purchasing.RecordPurchaseInvoicePayment.RecordPurchaseInvoicePaymentCommand(
                purchaseResult.Value.PurchaseInvoiceId, TestDataBuilder.CashPaymentMethodId, 50m, null, Guid.NewGuid()),
            CancellationToken.None);
        Assert.True(paymentResult.IsSuccess);

        var reportHandler = scope.ServiceProvider.GetRequiredService<GetSupplierPaymentDueHandler>();
        var result = await reportHandler.HandleAsync(
            new GetSupplierPaymentDueQuery(new PagedRequest(), Fixture.TestBranchId), CancellationToken.None);

        Assert.Empty(result.Items);
    }
}
