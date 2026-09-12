using Microsoft.EntityFrameworkCore;
using SupermarketSystem.Domain.Catalog;
using SupermarketSystem.Domain.Customers;
using SupermarketSystem.Domain.Inventory;
using SupermarketSystem.Domain.Purchasing;
using SupermarketSystem.Domain.Sales;
using SupermarketSystem.Infrastructure.Persistence;

namespace SupermarketSystem.IntegrationTests.Reporting;

/// <summary>
/// أدوات مساعدة مشتركة لاختبارات التقارير - كلها تستعلم عن بيانات معاملات
/// حقيقية (فواتير بيع/شراء/إرجاع) بجداول عادةً تُبنى عبر تدفّق بيع/شراء
/// كامل غير مغطّى بمجال هالدفعة من الاختبارات (Sales/Purchasing خارج
/// النطاق المطلوب). فبدل تكرار نفس منطق البناء بكل ملف تقرير، هذا الصنف
/// يبني الـaggregates مباشرة عبر مُنشئات الـDomain الحقيقية (لا SQL خام،
/// لا mocking) ثم يحفظها عبر AppDbContext - نفس مبدأ "استعلام حقيقي ضد
/// قاعدة بيانات حقيقية" المتبع بباقي الاختبارات.
///
/// تنبيه: CreatedAtUtc يُثبَّت تلقائيًا من AuditableEntitySaveChangesInterceptor
/// وقت الحفظ (لحظة تشغيل الاختبار الفعلية) - تعديله لتاريخ ماضٍ (لاختبار
/// فلاتر From/To) لازم تحديث مباشر بعد الحفظ عبر SQL خام، لأن الحقل بلا
/// setter عام قابل للاستدعاء بعد أول SaveChanges (العمود يتغيّر فقط على
/// إضافة صف جديد - راجع StampAuditFields بالـinterceptor).
/// </summary>
internal static class ReportingTestDataBuilder
{
    public static async Task<Guid> CreateCategoryAsync(AppDbContext db, string name)
    {
        var category = new ProductCategory(name, null);
        db.ProductCategories.Add(category);
        await db.SaveChangesAsync(CancellationToken.None);
        return category.Id;
    }

    public static async Task<(Guid ProductId, Guid UnitId)> CreateProductAsync(AppDbContext db, Guid categoryId, string name)
    {
        var product = new Product(name, categoryId, isBatchTracked: false, suggestedRetailPrice: null, expectedShelfLifeDays: null);
        var unit = product.AddUnit("قطعة", 1m, true);
        db.Products.Add(product);
        await db.SaveChangesAsync(CancellationToken.None);
        return (product.Id, unit.Id);
    }

    public static async Task<Guid> CreateProductBranchAsync(
        AppDbContext db, Guid productId, Guid branchId, decimal sellingPrice, decimal? minimumStock = null)
    {
        var productBranch = new ProductBranch(productId, branchId, sellingPrice);
        if (minimumStock is not null)
        {
            productBranch.SetStockThresholds(minimumStock, null);
        }

        db.ProductBranches.Add(productBranch);
        await db.SaveChangesAsync(CancellationToken.None);
        return productBranch.Id;
    }

    /// <summary>يضبط رصيد المخزون لمنتج/فرع، حتى لو سالبًا (Stock.Decrease الحقيقي يرفض السالب عمدًا - هذا تلاعب مباشر بالقيمة لاختبار تقرير "المخزون السالب" فقط).</summary>
    public static async Task<Guid> SetStockAsync(AppDbContext db, Guid productId, Guid branchId, decimal quantityOnHand)
    {
        var stock = new Stock(productId, branchId, null);
        if (quantityOnHand > 0)
        {
            stock.Increase(quantityOnHand);
        }

        db.Stocks.Add(stock);
        await db.SaveChangesAsync(CancellationToken.None);

        if (quantityOnHand < 0)
        {
            db.Entry(stock).Property(s => s.QuantityOnHand).CurrentValue = quantityOnHand;
            await db.SaveChangesAsync(CancellationToken.None);
        }

        return stock.Id;
    }

    public static async Task<Guid> CreateCustomerAsync(AppDbContext db, string fullName, string? phone = null)
    {
        var customer = new Customer(fullName, phone, null);
        db.Customers.Add(customer);
        await db.SaveChangesAsync(CancellationToken.None);
        return customer.Id;
    }

    public static async Task<Guid> CreateSupplierAsync(AppDbContext db, string name)
    {
        var supplier = new Supplier(name, null, null, null, null);
        db.Suppliers.Add(supplier);
        await db.SaveChangesAsync(CancellationToken.None);
        return supplier.Id;
    }

    /// <summary>فاتورة بيع مكتملة بسطر واحد - CreatedAtUtc تُضبط بعد الحفظ عبر SQL خام لو انعطى تاريخ صريح.</summary>
    public static async Task<SaleInvoice> CreateCompletedSaleAsync(
        AppDbContext db, Guid branchId, Guid productId, Guid productUnitId,
        decimal quantity, decimal unitPrice, Guid? customerId = null, DateTime? createdAtUtc = null)
    {
        var invoiceNumber = $"INV-{Guid.NewGuid():N}"[..15];
        var invoice = new SaleInvoice(branchId, invoiceNumber, Guid.NewGuid(), customerId, null, null);
        invoice.AddItem(productId, productUnitId, quantity, unitPrice, 0m, null);

        db.SaleInvoices.Add(invoice);
        await db.SaveChangesAsync(CancellationToken.None);

        if (createdAtUtc is { } date)
        {
            await BackdateCreatedAtAsync(db, "SaleInvoices", invoice.Id, date);
        }

        return invoice;
    }

    public static async Task VoidSaleAsync(AppDbContext db, SaleInvoice invoice, Guid voidedByUserId, VoidReason reason = VoidReason.CashierError)
    {
        var tracked = await db.SaleInvoices.FindAsync(invoice.Id) ?? throw new InvalidOperationException("Sale invoice not found.");
        tracked.Void(voidedByUserId, DateTime.UtcNow, reason, "اختبار");
        await db.SaveChangesAsync(CancellationToken.None);
    }

    public static async Task<ReturnInvoice> CreateReturnAsync(
        AppDbContext db, SaleInvoice originalSale, Guid productId, Guid productUnitId,
        decimal quantity, decimal unitPrice, ReturnReason reason, DateTime? createdAtUtc = null)
    {
        var saleItem = originalSale.Items.First();
        var invoiceNumber = $"RET-{Guid.NewGuid():N}"[..15];
        var returnInvoice = new ReturnInvoice(originalSale.BranchId, invoiceNumber, Guid.NewGuid(), originalSale.Id, reason, null);
        returnInvoice.AddItem(saleItem.Id, productId, productUnitId, quantity, unitPrice);

        db.ReturnInvoices.Add(returnInvoice);
        await db.SaveChangesAsync(CancellationToken.None);

        if (createdAtUtc is { } date)
        {
            await BackdateCreatedAtAsync(db, "ReturnInvoices", returnInvoice.Id, date);
        }

        return returnInvoice;
    }

    public static async Task<PurchaseInvoice> CreateReceivedPurchaseAsync(
        AppDbContext db, Guid branchId, Guid supplierId, Guid productId, Guid productUnitId,
        decimal quantity, decimal unitCost, DateTime? createdAtUtc = null)
    {
        var invoiceNumber = $"PUR-{Guid.NewGuid():N}"[..15];
        var invoice = new PurchaseInvoice(branchId, supplierId, invoiceNumber, null);
        invoice.AddItem(productId, productUnitId, null, quantity, unitCost);
        invoice.MarkReceived();

        db.PurchaseInvoices.Add(invoice);
        await db.SaveChangesAsync(CancellationToken.None);

        if (createdAtUtc is { } date)
        {
            await BackdateCreatedAtAsync(db, "PurchaseInvoices", invoice.Id, date);
        }

        return invoice;
    }

    /// <summary>
    /// اسم الجدول نص ثابت مُمرَّر داخليًا من هالصنف نفسه فقط (لا مدخل
    /// مستخدم) - يُدمج بالنص مباشرة، بينما التاريخ والمعرّف يمرّان
    /// كمعاملات حقيقية (ExecuteSqlRawAsync، لا تسلسل نصي) لمنع أي حقن.
    /// </summary>
    private static Task BackdateCreatedAtAsync(AppDbContext db, string table, Guid id, DateTime createdAtUtc)
    {
        var sql = "UPDATE [" + table + "] SET CreatedAtUtc = {0} WHERE Id = {1}";
        return db.Database.ExecuteSqlRawAsync(sql, createdAtUtc, id);
    }
}
