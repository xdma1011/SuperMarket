using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Storage;
using SupermarketSystem.Application.Common.Interfaces;
using SupermarketSystem.Application.Common.Results;
using SupermarketSystem.Infrastructure.Persistence;

namespace SupermarketSystem.Infrastructure.Services;

/// <summary>
/// حارس كمية الإرجاع الذري — راجع ISaleInvoiceOperations لتفاصيل ليش
/// لازم يكون SQL شرطي مباشر.
/// </summary>
public sealed class SaleInvoiceOperations : ISaleInvoiceOperations
{
    private readonly AppDbContext _context;

    public SaleInvoiceOperations(AppDbContext context)
    {
        _context = context;
    }

    public async Task<bool> TryRecordReturnedQuantityAsync(
        Guid saleInvoiceItemId, decimal returnQuantity, CancellationToken cancellationToken)
    {
        if (returnQuantity <= 0)
        {
            throw new ArgumentOutOfRangeException(nameof(returnQuantity), "كمية الإرجاع لازم تكون موجبة.");
        }

        // الشرط (QuantityReturned + returnQuantity <= Quantity) بينتقل
        // لجملة الـWHERE نفسها — الفحص والكتابة بيتقيّموا ذريًا بنفس
        // القفل. صفر صفوف = الكمية بتتجاوز المُباع (أو السطر مش موجود).
        var rowsAffected = await _context.SaleInvoiceItems
            .Where(i => i.Id == saleInvoiceItemId && i.QuantityReturned + returnQuantity <= i.Quantity)
            .ExecuteUpdateAsync(
                setters => setters.SetProperty(i => i.QuantityReturned, i => i.QuantityReturned + returnQuantity),
                cancellationToken);

        return rowsAffected == 1;
    }
}

/// <summary>
/// التطبيق الفعلي للخصم الذري. راجع IStockOperations لتفاصيل ليش هذا
/// لازم يبقى SQL مباشر، مش تحميل-تعديل-حفظ.
/// </summary>
public sealed class StockOperations : IStockOperations
{
    private readonly AppDbContext _context;

    public StockOperations(AppDbContext context)
    {
        _context = context;
    }

    public async Task<StockDecrementOutcome> TryDecreaseAsync(
        Guid productId,
        Guid branchId,
        Guid? productBatchId,
        decimal quantityBase,
        bool allowNegative,
        CancellationToken cancellationToken)
    {
        if (quantityBase <= 0)
        {
            throw new ArgumentOutOfRangeException(nameof(quantityBase), "كمية الخصم لازم تكون موجبة.");
        }

        // === المحاولة الأولى: نفس الخصم الذري الأصلي بلا أي تغيير ===
        // شرط "الرصيد كافٍ" (QuantityOnHand >= quantityBase) داخل جملة الـ
        // WHERE نفسها، يعني الفحص والكتابة بيصيروا سوا بنفس القفل. هذا
        // المسار اللي بيغطي أغلبية البيعات (رصيد كافٍ، ما في داعي للسالب).
        var rowsAffected = await _context.Stocks
            .Where(s => s.ProductId == productId
                        && s.BranchId == branchId
                        && s.ProductBatchId == productBatchId
                        && s.QuantityOnHand >= quantityBase)
            .ExecuteUpdateAsync(
                setters => setters.SetProperty(s => s.QuantityOnHand, s => s.QuantityOnHand - quantityBase),
                cancellationToken);

        if (rowsAffected == 1)
        {
            return StockDecrementOutcome.Succeeded;
        }

        // === المخزون غير كافٍ (أو الصف غير موجود إطلاقًا) ===
        // لو الإعداد مطفي، هذا هو السلوك الأصلي: نرفض العملية بلا أي تغيير.
        if (!allowNegative)
        {
            return StockDecrementOutcome.Failed;
        }

        // === الإعداد مفعّل: نعيد المحاولة بلا شرط "الرصيد كافٍ" ===
        // نفس آلية الذرية (UPDATE واحدة)، بس بلا الحد الأدنى — الرصيد ممكن
        // يصير سالب، وهذا مقبول ومقصود.
        var forcedRows = await _context.Stocks
            .Where(s => s.ProductId == productId && s.BranchId == branchId && s.ProductBatchId == productBatchId)
            .ExecuteUpdateAsync(
                setters => setters.SetProperty(s => s.QuantityOnHand, s => s.QuantityOnHand - quantityBase),
                cancellationToken);

        if (forcedRows == 1)
        {
            return StockDecrementOutcome.SucceededWentNegative;
        }

        // === ولا صف موجود إطلاقًا لهاد المنتج بهاد الفرع/الدفعة ===
        // (مثلًا: منتج جديد كليًا، ما انباع ولا اتشرى بهاد الفرع قط).
        // ننشئ صف جديد برصيد سالب مباشرة عبر INSERT ذري.
        var inserted = await InsertNegativeStockRowIfMissingAsync(
            productId, branchId, productBatchId, quantityBase, cancellationToken);

        return inserted ? StockDecrementOutcome.SucceededWentNegative : StockDecrementOutcome.Failed;
    }

    /// <summary>
    /// ينشئ صف Stock جديد برصيد سالب، بس لو ما في صف موجود أصلًا لنفس
    /// المفتاح (ProductId + BranchId + ProductBatchId). جملة
    /// "INSERT ... WHERE NOT EXISTS" بتمنع تكرار الصف بأغلب الحالات حتى
    /// تحت تزامن — بس القفل النهائي والفعلي هو الـfiltered unique index
    /// الموجود أصلًا على جدول Stocks (من تصميم D6): لو صارت مصادفة نادرة
    /// وطلبين اتسابقوا بالضبط بنفس اللحظة، قاعدة البيانات نفسها بترفض
    /// الصف الثاني، ووسيط معالجة الأخطاء العام (ExceptionHandlingMiddleware)
    /// أصلًا بيترجم هذا لـ409 بدل خطأ 500 غامض. يعني هذا "دفاع بعمق
    /// طبقتين"، مش اعتماد كامل على جملة النفي هون لحالها.
    /// </summary>
    private async Task<bool> InsertNegativeStockRowIfMissingAsync(
        Guid productId,
        Guid branchId,
        Guid? productBatchId,
        decimal quantityBase,
        CancellationToken cancellationToken)
    {
        var newId = Guid.NewGuid();
        var negativeQuantity = -quantityBase;

        var rowsInserted = await _context.Database.ExecuteSqlInterpolatedAsync($@"
            INSERT INTO [Stocks] ([Id], [ProductId], [BranchId], [ProductBatchId], [QuantityOnHand])
            SELECT {newId}, {productId}, {branchId}, {productBatchId}, {negativeQuantity}
            WHERE NOT EXISTS (
                SELECT 1 FROM [Stocks]
                WHERE [ProductId] = {productId} AND [BranchId] = {branchId}
                  AND (
                        ([ProductBatchId] IS NULL AND {productBatchId} IS NULL)
                        OR [ProductBatchId] = {productBatchId}
                      )
            )", cancellationToken);

        return rowsInserted == 1;
    }
}

/// <summary>
/// Transaction boundary implementation.
/// </summary>
public sealed class TransactionalExecutor : ITransactionalExecutor
{
    private readonly AppDbContext _context;

    public TransactionalExecutor(AppDbContext context)
    {
        _context = context;
    }

    public async Task<Result<TValue>> ExecuteAsync<TValue>(
        Func<CancellationToken, Task<Result<TValue>>> operation,
        CancellationToken cancellationToken)
    {
        // If a transaction is already in flight (nested call), join it rather
        // than opening a second one — committing the inner one would
        // prematurely publish the outer one's partial work.
        if (_context.Database.CurrentTransaction is not null)
        {
            return await operation(cancellationToken);
        }

        // EnableRetryOnFailure is configured (see DependencyInjection), and a
        // retrying execution strategy refuses to run user-initiated
        // transactions unless the whole unit is wrapped in the strategy —
        // otherwise a mid-transaction retry would replay only part of the
        // work. CreateExecutionStrategy().ExecuteAsync is the supported way
        // to give the strategy the entire retriable block.
        var strategy = _context.Database.CreateExecutionStrategy();

        return await strategy.ExecuteAsync(async () =>
        {
            await using var transaction = await _context.Database.BeginTransactionAsync(cancellationToken);

            try
            {
                var result = await operation(cancellationToken);

                if (result.IsSuccess)
                {
                    await transaction.CommitAsync(cancellationToken);
                }
                else
                {
                    // A business failure (insufficient stock, over-payment)
                    // must undo the partial work exactly as an exception
                    // would — Result-based control flow does not mean
                    // weaker transactional guarantees.
                    await transaction.RollbackAsync(cancellationToken);
                }

                return result;
            }
            catch
            {
                await transaction.RollbackAsync(cancellationToken);
                throw;
            }
        });
    }
}
