using System.Net;
using System.Net.Http.Json;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using SupermarketSystem.Application.Common.Pagination;
using SupermarketSystem.Application.Finance.CreateCapitalTransaction;
using SupermarketSystem.Application.Finance.CreateExpense;
using SupermarketSystem.Application.Finance.GetCapitalTransactions;
using SupermarketSystem.Application.Finance.GetExpenses;
using SupermarketSystem.Application.Finance.GetMonthlyProfitStatement;
using SupermarketSystem.Application.Inventory.ApproveStocktake;
using SupermarketSystem.Application.Inventory.CompleteStocktake;
using SupermarketSystem.Application.Inventory.CreateStocktake;
using SupermarketSystem.Application.Inventory.ReceiveStockTransfer;
using SupermarketSystem.Application.Inventory.RecordStocktakeCount;
using SupermarketSystem.Application.Inventory.RecordWasteIssue;
using SupermarketSystem.Application.Sales.CompleteSale;
using SupermarketSystem.Domain.Finance;
using SupermarketSystem.Domain.Inventory;
using SupermarketSystem.Domain.Purchasing;
using SupermarketSystem.IntegrationTests.Common;
using Xunit;

namespace SupermarketSystem.IntegrationTests.Finance;

/// <summary>
/// مصاريف تشغيلية، حركات رأس مال، وحساب الربح الحقيقي الشهري - طلب صاحب
/// المشروع المباشر (17/9/2026): "بدقة شديدة". يغطي تحديدًا: UnitCostSnapshot
/// (متوسط مرجّح وقت البيع للمنتجات غير المتتبَّعة، تكلفة الدفعة بالضبط
/// للمتتبَّعة، null صريح بلا تاريخ شراء)، تعريف كل رقم بكشف الربح، وإصلاح
/// ReceiveStockTransferCommand (كان يصفّر تكلفة الدفعة الجديدة دايمًا).
/// </summary>
[Collection(DatabaseCollection.Name)]
public sealed class FinanceTests : IntegrationTestBase
{
    public FinanceTests(DatabaseFixture fixture) : base(fixture) { }

    [Fact]
    public async Task صلاحية_المالية_لـMaster_Admin_حصرًا_لا_لمساعد_الأدمن()
    {
        // خطأ بذر قديم: Finance.Manage كانت مربوطة بمساعد أدمن بدل Master Admin -
        // صاحب المحل ياخد 403 على صفحة المالية، ومساعد الأدمن يفوت عليها.
        var adminClient = await CreateAuthenticatedClientAsync();
        var adminResponse = await adminClient.GetAsync("/api/v1/finance/capital-transactions");
        Assert.Equal(HttpStatusCode.OK, adminResponse.StatusCode);

        var (_, assistantUsername) = await UsersTestDataHelper.CreateUserWithRoleReturningUsernameAsync(
            Fixture.Factory.Services, Fixture.TestBranchId, UsersTestDataHelper.AssistantAdminRoleId, "finance.assistant");
        var assistantClient = await LoginHelper.LoginAsAsync(Fixture, assistantUsername, UsersTestDataHelper.DefaultPassword);
        var assistantResponse = await assistantClient.GetAsync("/api/v1/finance/capital-transactions");
        Assert.Equal(HttpStatusCode.Forbidden, assistantResponse.StatusCode);
    }

    [Fact]
    public async Task صفحة_المالية_عبر_HTTP_بنفس_شكل_طلبات_لوحة_الإدارة()
    {
        // لوحة الإدارة بتبعت التصنيف رقم (القيمة الافتراضية) أو نص رقمي ("2" من <select>)،
        // والباك إند بيرجّع الـenums كأسماء (JsonStringEnumConverter عام) - الواجهة بتترجم بالاسم.
        var client = await CreateAuthenticatedClientAsync();
        var paymentDate = DateTime.UtcNow.ToString("yyyy-MM-dd");

        var numberCategory = await client.PostAsJsonAsync("/api/v1/finance/expenses", new
        {
            branchId = Fixture.TestBranchId, category = 1, amount = 150.250m, paymentDateUtc = paymentDate,
            periodYear = 2031, periodMonth = 1, notes = (string?)null
        });
        Assert.Equal(HttpStatusCode.Created, numberCategory.StatusCode);

        var stringCategory = await client.PostAsJsonAsync("/api/v1/finance/expenses", new
        {
            branchId = Fixture.TestBranchId, category = "2", amount = 20.125m, paymentDateUtc = paymentDate,
            periodYear = 2031, periodMonth = 1, notes = "فاتورة كهربا"
        });
        Assert.Equal(HttpStatusCode.Created, stringCategory.StatusCode);

        var capital = await client.PostAsJsonAsync("/api/v1/finance/capital-transactions", new
        {
            branchId = Fixture.TestBranchId, type = "2", amount = 50m, occurredAtUtc = paymentDate, notes = (string?)null
        });
        Assert.Equal(HttpStatusCode.Created, capital.StatusCode);

        var expenses = await client.GetFromJsonAsync<System.Text.Json.JsonElement>(
            $"/api/v1/finance/expenses?branchId={Fixture.TestBranchId}&periodYear=2031&periodMonth=1");
        var categories = expenses.GetProperty("items").EnumerateArray().Select(e => e.GetProperty("category").GetString()).ToList();
        Assert.Contains("Rent", categories);
        Assert.Contains("Electricity", categories);

        var statement = await client.GetFromJsonAsync<System.Text.Json.JsonElement>(
            $"/api/v1/finance/profit-statement?branchId={Fixture.TestBranchId}&year=2031&month=1");
        Assert.Equal(170.375m, statement.GetProperty("totalExpenses").GetDecimal());
        Assert.Equal(-170.375m, statement.GetProperty("netProfit").GetDecimal());

        var capitalList = await client.GetFromJsonAsync<System.Text.Json.JsonElement>(
            $"/api/v1/finance/capital-transactions?branchId={Fixture.TestBranchId}");
        Assert.Contains(capitalList.GetProperty("items").EnumerateArray(), c => c.GetProperty("type").GetString() == "Withdrawal");
    }

    [Fact]
    public async Task تسجيل_مصروف_ثم_جلبه_بفلترة_الفترة()
    {
        using var scope = CreateScope();
        await TestDataBuilder.ActAsAdminAsync(scope, Fixture);

        var createHandler = scope.ServiceProvider.GetRequiredService<CreateExpenseHandler>();
        var result = await createHandler.HandleAsync(new CreateExpenseCommand(
            Fixture.TestBranchId, ExpenseCategory.Rent, 200m, DateTime.UtcNow, 2026, 10, "إيجار تشرين أول"),
            CancellationToken.None);

        Assert.True(result.IsSuccess);

        var getHandler = scope.ServiceProvider.GetRequiredService<GetExpensesHandler>();
        var list = await getHandler.HandleAsync(
            new GetExpensesQuery(new PagedRequest(), Fixture.TestBranchId, 2026, 10, null), CancellationToken.None);

        var item = Assert.Single(list.Items);
        Assert.Equal(ExpenseCategory.Rent, item.Category);
        Assert.Equal(200m, item.Amount);
    }

    [Fact]
    public async Task تسجيل_حركة_رأس_مال_ثم_جلبها()
    {
        using var scope = CreateScope();
        await TestDataBuilder.ActAsAdminAsync(scope, Fixture);

        var createHandler = scope.ServiceProvider.GetRequiredService<CreateCapitalTransactionHandler>();
        var result = await createHandler.HandleAsync(new CreateCapitalTransactionCommand(
            Fixture.TestBranchId, CapitalTransactionType.Deposit, 1000m, DateTime.UtcNow, "ضخ رأس مال إضافي"),
            CancellationToken.None);

        Assert.True(result.IsSuccess);

        var getHandler = scope.ServiceProvider.GetRequiredService<GetCapitalTransactionsHandler>();
        var list = await getHandler.HandleAsync(
            new GetCapitalTransactionsQuery(new PagedRequest(), Fixture.TestBranchId), CancellationToken.None);

        var item = Assert.Single(list.Items);
        Assert.Equal(CapitalTransactionType.Deposit, item.Type);
        Assert.Equal(1000m, item.Amount);
    }

    [Fact]
    public async Task البيع_غير_متتبع_الدفعات_ياخذ_متوسط_تكلفة_مرجّح_من_فواتير_الشراء()
    {
        using var scope = CreateScope();
        await TestDataBuilder.ActAsAdminAsync(scope, Fixture);
        var db = CreateDbContext(scope);
        var (product, unit) = await TestDataBuilder.CreateActiveProductAsync(db, "منتج تكلفة متوسطة");
        await TestDataBuilder.CreateProductBranchAsync(db, product.Id, Fixture.TestBranchId, sellingPrice: 5m);
        await TestDataBuilder.SetStockAsync(db, product.Id, Fixture.TestBranchId, 100m);

        var supplier = await TestDataBuilder.CreateSupplierAsync(db);

        // فاتورة شراء أولى: 10 وحدة بتكلفة 2 دينار = 20
        var invoice1 = new PurchaseInvoice(Fixture.TestBranchId, supplier.Id, "PI-TEST-1", null);
        invoice1.AddItem(product.Id, unit.Id, null, 10m, 2m);
        invoice1.MarkReceived();
        db.PurchaseInvoices.Add(invoice1);
        await db.SaveChangesAsync();

        // فاتورة شراء ثانية: 10 وحدة بتكلفة 4 دنانير = 40
        var invoice2 = new PurchaseInvoice(Fixture.TestBranchId, supplier.Id, "PI-TEST-2", null);
        invoice2.AddItem(product.Id, unit.Id, null, 10m, 4m);
        invoice2.MarkReceived();
        db.PurchaseInvoices.Add(invoice2);
        await db.SaveChangesAsync();

        // متوسط مرجّح متوقَّع = (20 + 40) / 20 = 3 دنانير للوحدة

        var saleHandler = scope.ServiceProvider.GetRequiredService<CompleteSaleHandler>();
        var result = await saleHandler.HandleAsync(new CompleteSaleCommand(
            Fixture.TestBranchId, Guid.NewGuid(), CustomerId: null, InvoiceLevelDiscountAmount: 0m,
            Items: new[] { new CompleteSaleItemDto(product.Id, unit.Id, Quantity: 1m, ManualDiscountAmount: 0m, ProductBatchId: null) },
            Payments: new[] { new CompleteSalePaymentDto(TestDataBuilder.CashPaymentMethodId, 5m, null, Guid.NewGuid()) }),
            CancellationToken.None);

        Assert.True(result.IsSuccess);

        var item = await db.SaleInvoiceItems.AsNoTracking().FirstAsync(i => i.SaleInvoiceId == result.Value.SaleInvoiceId);
        Assert.Equal(3m, item.UnitCostSnapshot);
    }

    [Fact]
    public async Task البيع_من_منتج_متتبع_الدفعات_ياخذ_تكلفة_الدفعة_بالضبط()
    {
        using var scope = CreateScope();
        await TestDataBuilder.ActAsAdminAsync(scope, Fixture);
        var db = CreateDbContext(scope);
        var (product, unit) = await TestDataBuilder.CreateActiveProductAsync(db, "منتج دفعات تكلفة", isBatchTracked: true);
        await TestDataBuilder.CreateProductBranchAsync(db, product.Id, Fixture.TestBranchId, sellingPrice: 5m);
        var batch = await TestDataBuilder.CreateBatchAsync(db, product.Id, Fixture.TestBranchId, "BATCH-1", unitCost: 2.5m);
        await TestDataBuilder.SetStockAsync(db, product.Id, Fixture.TestBranchId, 50m, batch.Id);

        var saleHandler = scope.ServiceProvider.GetRequiredService<CompleteSaleHandler>();
        var result = await saleHandler.HandleAsync(new CompleteSaleCommand(
            Fixture.TestBranchId, Guid.NewGuid(), CustomerId: null, InvoiceLevelDiscountAmount: 0m,
            Items: new[] { new CompleteSaleItemDto(product.Id, unit.Id, Quantity: 1m, ManualDiscountAmount: 0m, ProductBatchId: batch.Id) },
            Payments: new[] { new CompleteSalePaymentDto(TestDataBuilder.CashPaymentMethodId, 5m, null, Guid.NewGuid()) }),
            CancellationToken.None);

        Assert.True(result.IsSuccess);

        var item = await db.SaleInvoiceItems.AsNoTracking().FirstAsync(i => i.SaleInvoiceId == result.Value.SaleInvoiceId);
        Assert.Equal(2.5m, item.UnitCostSnapshot);
    }

    [Fact]
    public async Task البيع_بلا_تاريخ_شراء_سابق_يسجّل_تكلفة_null_لا_صفر()
    {
        using var scope = CreateScope();
        await TestDataBuilder.ActAsAdminAsync(scope, Fixture);
        var db = CreateDbContext(scope);
        var (product, unit) = await TestDataBuilder.CreateActiveProductAsync(db, "منتج بلا تاريخ شراء");
        await TestDataBuilder.CreateProductBranchAsync(db, product.Id, Fixture.TestBranchId, sellingPrice: 5m);
        await TestDataBuilder.SetStockAsync(db, product.Id, Fixture.TestBranchId, 100m);

        var saleHandler = scope.ServiceProvider.GetRequiredService<CompleteSaleHandler>();
        var result = await saleHandler.HandleAsync(new CompleteSaleCommand(
            Fixture.TestBranchId, Guid.NewGuid(), CustomerId: null, InvoiceLevelDiscountAmount: 0m,
            Items: new[] { new CompleteSaleItemDto(product.Id, unit.Id, Quantity: 1m, ManualDiscountAmount: 0m, ProductBatchId: null) },
            Payments: new[] { new CompleteSalePaymentDto(TestDataBuilder.CashPaymentMethodId, 5m, null, Guid.NewGuid()) }),
            CancellationToken.None);

        Assert.True(result.IsSuccess);

        var item = await db.SaleInvoiceItems.AsNoTracking().FirstAsync(i => i.SaleInvoiceId == result.Value.SaleInvoiceId);
        Assert.Null(item.UnitCostSnapshot);
    }

    [Fact]
    public async Task كشف_الربح_الشهري_يحسب_الإيراد_والتكلفة_والمصاريف_بدقة()
    {
        using var scope = CreateScope();
        await TestDataBuilder.ActAsAdminAsync(scope, Fixture);
        var db = CreateDbContext(scope);
        var (product, unit) = await TestDataBuilder.CreateActiveProductAsync(db, "منتج كشف ربح");
        await TestDataBuilder.CreateProductBranchAsync(db, product.Id, Fixture.TestBranchId, sellingPrice: 5m);
        await TestDataBuilder.SetStockAsync(db, product.Id, Fixture.TestBranchId, 100m);

        var supplier = await TestDataBuilder.CreateSupplierAsync(db);
        var invoice = new PurchaseInvoice(Fixture.TestBranchId, supplier.Id, "PI-PROFIT-1", null);
        invoice.AddItem(product.Id, unit.Id, null, 100m, 3m); // تكلفة الوحدة = 3
        invoice.MarkReceived();
        db.PurchaseInvoices.Add(invoice);
        await db.SaveChangesAsync();

        var saleHandler = scope.ServiceProvider.GetRequiredService<CompleteSaleHandler>();
        var saleResult = await saleHandler.HandleAsync(new CompleteSaleCommand(
            Fixture.TestBranchId, Guid.NewGuid(), CustomerId: null, InvoiceLevelDiscountAmount: 0m,
            Items: new[] { new CompleteSaleItemDto(product.Id, unit.Id, Quantity: 10m, ManualDiscountAmount: 0m, ProductBatchId: null) },
            Payments: new[] { new CompleteSalePaymentDto(TestDataBuilder.CashPaymentMethodId, 50m, null, Guid.NewGuid()) }),
            CancellationToken.None);
        Assert.True(saleResult.IsSuccess);
        // إيراد = 50، تكلفة البضاعة = 10 × 3 = 30، ربح إجمالي = 20

        var now = DateTime.UtcNow;
        var expenseHandler = scope.ServiceProvider.GetRequiredService<CreateExpenseHandler>();
        await expenseHandler.HandleAsync(new CreateExpenseCommand(
            Fixture.TestBranchId, ExpenseCategory.Electricity, 5m, now, now.Year, now.Month, null), CancellationToken.None);

        var statementHandler = scope.ServiceProvider.GetRequiredService<GetMonthlyProfitStatementHandler>();
        var statement = await statementHandler.HandleAsync(
            new GetMonthlyProfitStatementQuery(Fixture.TestBranchId, now.Year, now.Month), CancellationToken.None);

        Assert.True(statement.IsSuccess);
        Assert.Equal(50m, statement.Value.TotalSales);
        Assert.Equal(30m, statement.Value.CostOfGoodsSold);
        Assert.Equal(20m, statement.Value.GrossProfit);
        Assert.Equal(5m, statement.Value.TotalExpenses);
        Assert.Equal(15m, statement.Value.NetProfit);
        Assert.Equal(0, statement.Value.ItemsExcludedNoCostHistory);
    }

    [Fact]
    public async Task كشف_الربح_يستثني_تكلفة_سطر_بلا_تاريخ_شراء_من_المجموع_ويعلّمه()
    {
        using var scope = CreateScope();
        await TestDataBuilder.ActAsAdminAsync(scope, Fixture);
        var db = CreateDbContext(scope);
        var (product, unit) = await TestDataBuilder.CreateActiveProductAsync(db, "منتج بلا تاريخ شراء لكشف الربح");
        await TestDataBuilder.CreateProductBranchAsync(db, product.Id, Fixture.TestBranchId, sellingPrice: 5m);
        await TestDataBuilder.SetStockAsync(db, product.Id, Fixture.TestBranchId, 100m);

        var saleHandler = scope.ServiceProvider.GetRequiredService<CompleteSaleHandler>();
        var saleResult = await saleHandler.HandleAsync(new CompleteSaleCommand(
            Fixture.TestBranchId, Guid.NewGuid(), CustomerId: null, InvoiceLevelDiscountAmount: 0m,
            Items: new[] { new CompleteSaleItemDto(product.Id, unit.Id, Quantity: 2m, ManualDiscountAmount: 0m, ProductBatchId: null) },
            Payments: new[] { new CompleteSalePaymentDto(TestDataBuilder.CashPaymentMethodId, 10m, null, Guid.NewGuid()) }),
            CancellationToken.None);
        Assert.True(saleResult.IsSuccess);

        var now = DateTime.UtcNow;
        var statementHandler = scope.ServiceProvider.GetRequiredService<GetMonthlyProfitStatementHandler>();
        var statement = await statementHandler.HandleAsync(
            new GetMonthlyProfitStatementQuery(Fixture.TestBranchId, now.Year, now.Month), CancellationToken.None);

        Assert.True(statement.IsSuccess);
        Assert.Equal(10m, statement.Value.TotalSales);
        Assert.Equal(0m, statement.Value.CostOfGoodsSold);
        Assert.Equal(1, statement.Value.ItemsExcludedNoCostHistory);
        // بلا تكلفة معروفة، الربح المعروض هون أعلى من الحقيقي فعليًا -
        // بالضبط ما تعليق GetMonthlyProfitStatementQuery بيحذّر منه.
        Assert.Equal(10m, statement.Value.GrossProfit);
    }

    [Fact]
    public async Task استلام_نقل_مخزون_ينقل_تكلفة_دفعة_المصدر_لا_صفر()
    {
        // يتحقّق من إصلاح ReceiveStockTransferCommand - قبل الإصلاح كانت
        // الدفعة الجديدة بالفرع الوجهة تُنشأ بتكلفة 0 دايمًا بغض النظر عن
        // تكلفة دفعة المصدر الفعلية (كان رح يضخّم ربح أي بيع لاحق من هالدفعة
        // بالفرع الوجهة بشكل وهمي - راجع تعليق PROFIT ASSUMPTION بـCompleteSaleCommand).
        using var scope = CreateScope();
        await TestDataBuilder.ActAsAdminAsync(scope, Fixture);
        var db = CreateDbContext(scope);

        var (product, unit) = await TestDataBuilder.CreateActiveProductAsync(db, "منتج نقل دفعة", isBatchTracked: true);
        var destinationBranch = await TestDataBuilder.CreateBranchAsync(db, "فرع وجهة نقل تكلفة", "TRC");

        var sourceBatch = await TestDataBuilder.CreateBatchAsync(db, product.Id, Fixture.TestBranchId, "SRC-BATCH", unitCost: 7m);

        var transfer = new StockTransfer(Fixture.TestBranchId, destinationBranch.Id, "TR-TEST-1", Fixture.AdminUserId, DateTime.UtcNow);
        transfer.AddItem(product.Id, unit.Id, quantityBase: 5m, sourceBatch.Id, "SRC-BATCH", null);
        db.StockTransfers.Add(transfer);
        await db.SaveChangesAsync();

        var receiveHandler = scope.ServiceProvider.GetRequiredService<ReceiveStockTransferHandler>();
        var receiveResult = await receiveHandler.HandleAsync(
            new ReceiveStockTransferCommand(transfer.Id), CancellationToken.None);

        Assert.True(receiveResult.IsSuccess);

        var destinationBatch = await db.ProductBatches.AsNoTracking()
            .FirstAsync(b => b.ProductId == product.Id && b.BranchId == destinationBranch.Id && b.BatchNumber == "SRC-BATCH");

        Assert.Equal(7m, destinationBatch.UnitCost);
    }

    [Fact]
    public async Task فروقات_الجرد_نقص_وزيادة_تُحتسب_ضمن_كشف_الربح_الشهري()
    {
        using var scope = CreateScope();
        await TestDataBuilder.ActAsAdminAsync(scope, Fixture);
        var db = CreateDbContext(scope);

        var (shortageProduct, shortageUnit) = await TestDataBuilder.CreateActiveProductAsync(db, "منتج نقص جرد");
        await TestDataBuilder.CreateProductBranchAsync(db, shortageProduct.Id, Fixture.TestBranchId, sellingPrice: 5m);
        await TestDataBuilder.SetStockAsync(db, shortageProduct.Id, Fixture.TestBranchId, 30m);

        var (surplusProduct, surplusUnit) = await TestDataBuilder.CreateActiveProductAsync(db, "منتج زيادة جرد");
        await TestDataBuilder.CreateProductBranchAsync(db, surplusProduct.Id, Fixture.TestBranchId, sellingPrice: 5m);
        await TestDataBuilder.SetStockAsync(db, surplusProduct.Id, Fixture.TestBranchId, 10m);

        var supplier = await TestDataBuilder.CreateSupplierAsync(db);

        var shortageInvoice = new PurchaseInvoice(Fixture.TestBranchId, supplier.Id, "PI-STOCKTAKE-SHORTAGE", null);
        shortageInvoice.AddItem(shortageProduct.Id, shortageUnit.Id, null, 30m, 3m); // تكلفة الوحدة = 3
        shortageInvoice.MarkReceived();
        db.PurchaseInvoices.Add(shortageInvoice);

        var surplusInvoice = new PurchaseInvoice(Fixture.TestBranchId, supplier.Id, "PI-STOCKTAKE-SURPLUS", null);
        surplusInvoice.AddItem(surplusProduct.Id, surplusUnit.Id, null, 10m, 2m); // تكلفة الوحدة = 2
        surplusInvoice.MarkReceived();
        db.PurchaseInvoices.Add(surplusInvoice);

        await db.SaveChangesAsync();

        var createHandler = scope.ServiceProvider.GetRequiredService<CreateStocktakeHandler>();
        var created = await createHandler.HandleAsync(
            new CreateStocktakeCommand(Fixture.TestBranchId, IncludeAllProductsAtBranch: true, ProductIds: null),
            CancellationToken.None);
        Assert.True(created.IsSuccess);

        var stocktakeItems = await db.StocktakeItems.AsNoTracking()
            .Where(i => i.StocktakeId == created.Value.StocktakeId).ToListAsync();
        var shortageItem = stocktakeItems.Single(i => i.ProductId == shortageProduct.Id);
        var surplusItem = stocktakeItems.Single(i => i.ProductId == surplusProduct.Id);

        var recordHandler = scope.ServiceProvider.GetRequiredService<RecordStocktakeCountHandler>();
        await recordHandler.HandleAsync(
            new RecordStocktakeCountCommand(created.Value.StocktakeId, shortageItem.Id, CountedQuantity: 25m), CancellationToken.None); // نقص 5
        await recordHandler.HandleAsync(
            new RecordStocktakeCountCommand(created.Value.StocktakeId, surplusItem.Id, CountedQuantity: 15m), CancellationToken.None); // زيادة 5

        var completeHandler = scope.ServiceProvider.GetRequiredService<CompleteStocktakeHandler>();
        var completed = await completeHandler.HandleAsync(new CompleteStocktakeCommand(created.Value.StocktakeId), CancellationToken.None);
        Assert.True(completed.IsSuccess);

        var approveHandler = scope.ServiceProvider.GetRequiredService<ApproveStocktakeHandler>();
        var approved = await approveHandler.HandleAsync(new ApproveStocktakeCommand(created.Value.StocktakeId), CancellationToken.None);
        Assert.True(approved.IsSuccess);

        var now = DateTime.UtcNow;
        var statementHandler = scope.ServiceProvider.GetRequiredService<GetMonthlyProfitStatementHandler>();
        var statement = await statementHandler.HandleAsync(
            new GetMonthlyProfitStatementQuery(Fixture.TestBranchId, now.Year, now.Month), CancellationToken.None);

        Assert.True(statement.IsSuccess);
        Assert.Equal(15m, statement.Value.StocktakeShortageValue); // 5 × 3
        Assert.Equal(10m, statement.Value.StocktakeSurplusValue); // 5 × 2
        Assert.Equal(0, statement.Value.StocktakeMovementsExcludedNoCostHistory);
        Assert.Equal(
            statement.Value.GrossProfit - statement.Value.TotalExpenses + statement.Value.StocktakeSurplusValue
                - statement.Value.StocktakeShortageValue - statement.Value.WasteLossValue,
            statement.Value.NetProfit);
    }

    [Fact]
    public async Task التلف_خسارة_بكشف_الربح_إلا_المستبدَل_من_الشركة()
    {
        using var scope = CreateScope();
        await TestDataBuilder.ActAsAdminAsync(scope, Fixture);
        var db = CreateDbContext(scope);

        var (product, unit) = await TestDataBuilder.CreateActiveProductAsync(db, "منتج تلف بكشف الربح");
        await TestDataBuilder.CreateProductBranchAsync(db, product.Id, Fixture.TestBranchId, sellingPrice: 5m);
        await TestDataBuilder.SetStockAsync(db, product.Id, Fixture.TestBranchId, 20m);

        var (noCostProduct, noCostUnit) = await TestDataBuilder.CreateActiveProductAsync(db, "منتج تلف بلا تاريخ شراء");
        await TestDataBuilder.CreateProductBranchAsync(db, noCostProduct.Id, Fixture.TestBranchId, sellingPrice: 5m);
        await TestDataBuilder.SetStockAsync(db, noCostProduct.Id, Fixture.TestBranchId, 5m);

        var supplier = await TestDataBuilder.CreateSupplierAsync(db);
        var invoice = new PurchaseInvoice(Fixture.TestBranchId, supplier.Id, "PI-WASTE-LOSS", null);
        invoice.AddItem(product.Id, unit.Id, null, 20m, 4m); // تكلفة الوحدة = 4
        invoice.MarkReceived();
        db.PurchaseInvoices.Add(invoice);
        await db.SaveChangesAsync();

        var now = DateTime.UtcNow;
        var statementHandler = scope.ServiceProvider.GetRequiredService<GetMonthlyProfitStatementHandler>();
        var before = await statementHandler.HandleAsync(
            new GetMonthlyProfitStatementQuery(Fixture.TestBranchId, now.Year, now.Month), CancellationToken.None);
        Assert.True(before.IsSuccess);

        var wasteHandler = scope.ServiceProvider.GetRequiredService<RecordWasteIssueHandler>();
        var lost = await wasteHandler.HandleAsync(
            new RecordWasteIssueCommand(product.Id, unit.Id, Fixture.TestBranchId, 3m, WasteReason.Expired, null),
            CancellationToken.None);
        var replaced = await wasteHandler.HandleAsync(
            new RecordWasteIssueCommand(product.Id, unit.Id, Fixture.TestBranchId, 2m, WasteReason.Expired, null, IsReplacedBySupplier: true),
            CancellationToken.None);
        var noCost = await wasteHandler.HandleAsync(
            new RecordWasteIssueCommand(noCostProduct.Id, noCostUnit.Id, Fixture.TestBranchId, 1m, WasteReason.Broken, null),
            CancellationToken.None);
        Assert.True(lost.IsSuccess && replaced.IsSuccess && noCost.IsSuccess);

        // المستبدَل بينقص المخزون عادي - التلف فعلي، بس الشركة عوّضت.
        var stock = await db.Stocks.AsNoTracking().FirstAsync(s => s.ProductId == product.Id && s.BranchId == Fixture.TestBranchId);
        Assert.Equal(15m, stock.QuantityOnHand);

        var after = await statementHandler.HandleAsync(
            new GetMonthlyProfitStatementQuery(Fixture.TestBranchId, now.Year, now.Month), CancellationToken.None);
        Assert.True(after.IsSuccess);

        // فرق قبل/بعد (نفس الفرع والشهر مشترك مع اختبارات تانية): 3 × 4 بس.
        Assert.Equal(12m, after.Value.WasteLossValue - before.Value.WasteLossValue);
        Assert.Equal(1, after.Value.WasteMovementsExcludedNoCostHistory - before.Value.WasteMovementsExcludedNoCostHistory);
        Assert.Equal(-12m, after.Value.NetProfit - before.Value.NetProfit);
        Assert.Equal(
            after.Value.GrossProfit - after.Value.TotalExpenses + after.Value.StocktakeSurplusValue
                - after.Value.StocktakeShortageValue - after.Value.WasteLossValue,
            after.Value.NetProfit);
    }

    [Fact]
    public async Task فروقات_جرد_منتج_بلا_تاريخ_شراء_تُستبعد_من_صافي_فروقات_الجرد()
    {
        using var scope = CreateScope();
        await TestDataBuilder.ActAsAdminAsync(scope, Fixture);
        var db = CreateDbContext(scope);

        var (product, _) = await TestDataBuilder.CreateActiveProductAsync(db, "منتج جرد بلا تاريخ شراء");
        await TestDataBuilder.CreateProductBranchAsync(db, product.Id, Fixture.TestBranchId, sellingPrice: 5m);
        await TestDataBuilder.SetStockAsync(db, product.Id, Fixture.TestBranchId, 20m);

        var createHandler = scope.ServiceProvider.GetRequiredService<CreateStocktakeHandler>();
        var created = await createHandler.HandleAsync(
            new CreateStocktakeCommand(Fixture.TestBranchId, IncludeAllProductsAtBranch: true, ProductIds: null),
            CancellationToken.None);
        Assert.True(created.IsSuccess);

        var itemId = await db.StocktakeItems.AsNoTracking()
            .Where(i => i.StocktakeId == created.Value.StocktakeId).Select(i => i.Id).FirstAsync();

        var recordHandler = scope.ServiceProvider.GetRequiredService<RecordStocktakeCountHandler>();
        await recordHandler.HandleAsync(
            new RecordStocktakeCountCommand(created.Value.StocktakeId, itemId, CountedQuantity: 15m), CancellationToken.None); // نقص 5

        var completeHandler = scope.ServiceProvider.GetRequiredService<CompleteStocktakeHandler>();
        await completeHandler.HandleAsync(new CompleteStocktakeCommand(created.Value.StocktakeId), CancellationToken.None);

        var approveHandler = scope.ServiceProvider.GetRequiredService<ApproveStocktakeHandler>();
        await approveHandler.HandleAsync(new ApproveStocktakeCommand(created.Value.StocktakeId), CancellationToken.None);

        var now = DateTime.UtcNow;
        var statementHandler = scope.ServiceProvider.GetRequiredService<GetMonthlyProfitStatementHandler>();
        var statement = await statementHandler.HandleAsync(
            new GetMonthlyProfitStatementQuery(Fixture.TestBranchId, now.Year, now.Month), CancellationToken.None);

        Assert.True(statement.IsSuccess);
        Assert.Equal(0m, statement.Value.StocktakeShortageValue);
        Assert.Equal(0m, statement.Value.StocktakeSurplusValue);
        Assert.Equal(1, statement.Value.StocktakeMovementsExcludedNoCostHistory);
    }
}
