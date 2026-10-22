using Microsoft.Data.SqlClient;
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
        var rowsAffected = await DecrementAsync(productId, branchId, productBatchId, quantityBase, requireSufficientBalance: true, cancellationToken);

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
        var forcedRows = await DecrementAsync(productId, branchId, productBatchId, quantityBase, requireSufficientBalance: false, cancellationToken);

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


    // أربع صيغ literal (بلا نص ديناميكي): بلا دفعة / بدفعة × مع شرط الرصيد / بلا شرط. كل وحدة بـindex hint على الفهرس الفريد المناسب.
    private const string NoBatchSufficient = @"
        UPDATE s SET s.QuantityOnHand = s.QuantityOnHand - @q
        FROM [Stocks] AS s WITH (INDEX(IX_Stocks_ProductId_BranchId_NoBatch), ROWLOCK)
        WHERE s.ProductId = @productId AND s.BranchId = @branchId AND s.ProductBatchId IS NULL
          AND (@cross = 1 OR s.BranchId = @currentBranch) AND s.QuantityOnHand >= @q";

    private const string NoBatchForced = @"
        UPDATE s SET s.QuantityOnHand = s.QuantityOnHand - @q
        FROM [Stocks] AS s WITH (INDEX(IX_Stocks_ProductId_BranchId_NoBatch), ROWLOCK)
        WHERE s.ProductId = @productId AND s.BranchId = @branchId AND s.ProductBatchId IS NULL
          AND (@cross = 1 OR s.BranchId = @currentBranch)";

    private const string BatchSufficient = @"
        UPDATE s SET s.QuantityOnHand = s.QuantityOnHand - @q
        FROM [Stocks] AS s WITH (INDEX(IX_Stocks_ProductId_BranchId_ProductBatchId), ROWLOCK)
        WHERE s.ProductId = @productId AND s.BranchId = @branchId AND s.ProductBatchId IS NOT NULL AND s.ProductBatchId = @batchId
          AND (@cross = 1 OR s.BranchId = @currentBranch) AND s.QuantityOnHand >= @q";

    private const string BatchForced = @"
        UPDATE s SET s.QuantityOnHand = s.QuantityOnHand - @q
        FROM [Stocks] AS s WITH (INDEX(IX_Stocks_ProductId_BranchId_ProductBatchId), ROWLOCK)
        WHERE s.ProductId = @productId AND s.BranchId = @branchId AND s.ProductBatchId IS NOT NULL AND s.ProductBatchId = @batchId
          AND (@cross = 1 OR s.BranchId = @currentBranch)";

    /// <summary>
    /// الخصم الذري بجملة SQL خام (§3.3) مع index hint على الفهرس الفريد (صنف، فرع[، دفعة]) = seek مضمون لصف واحد بغض النظر عن حجم الجدول.
    /// ليش مش ExecuteUpdate: شرط الدفعة المولَّد (<c>ProductBatchId = @p OR (IS NULL AND @p IS NULL)</c>) ما بيطابق الفهارس الفريدة المفلترة،
    /// فالمحسّن بيمسح (scan) ويحط U lock على صفوف الأصناف التانية بالترتيب - بيعتين متزامنتين لصنفين مختلفين بيتقفلوا ببعض (deadlock) حتى مع
    /// ترتيب قفل ثابت. انمسك بعاصفة 8 كاشيرات (22/10/2026) وأكّدت الخطة: Clustered Index Scan. فلتر الفرع (الـquery filter) محفوظ يدويًا بالشرط.
    /// </summary>
    private Task<int> DecrementAsync(
        Guid productId, Guid branchId, Guid? productBatchId, decimal quantityBase, bool requireSufficientBalance, CancellationToken cancellationToken)
    {
        var sql = (productBatchId is null, requireSufficientBalance) switch
        {
            (true, true) => NoBatchSufficient,
            (true, false) => NoBatchForced,
            (false, true) => BatchSufficient,
            (false, false) => BatchForced
        };

        return ExecuteStockSqlAsync(sql, productId, branchId, productBatchId, quantityBase, cancellationToken);
    }

    private Task<int> ExecuteStockSqlAsync(
        string sql, Guid productId, Guid branchId, Guid? productBatchId, decimal quantityBase, CancellationToken cancellationToken)
    {
        var parameters = new List<SqlParameter>
        {
            new("@q", System.Data.SqlDbType.Decimal) { Precision = 18, Scale = 4, Value = quantityBase },
            new("@productId", productId),
            new("@branchId", branchId),
            new("@cross", _context.IsCrossBranchAccessAllowed),
            new("@currentBranch", System.Data.SqlDbType.UniqueIdentifier) { Value = (object?)_context.CurrentBranchId ?? DBNull.Value }
        };
        if (productBatchId is { } batchId)
        {
            parameters.Add(new SqlParameter("@batchId", batchId));
        }

        return _context.Database.ExecuteSqlRawAsync(sql, parameters, cancellationToken);
    }

    private const string NoBatchIncrease = @"
        UPDATE s SET s.QuantityOnHand = s.QuantityOnHand + @q
        FROM [Stocks] AS s WITH (INDEX(IX_Stocks_ProductId_BranchId_NoBatch), ROWLOCK)
        WHERE s.ProductId = @productId AND s.BranchId = @branchId AND s.ProductBatchId IS NULL
          AND (@cross = 1 OR s.BranchId = @currentBranch)";

    private const string BatchIncrease = @"
        UPDATE s SET s.QuantityOnHand = s.QuantityOnHand + @q
        FROM [Stocks] AS s WITH (INDEX(IX_Stocks_ProductId_BranchId_ProductBatchId), ROWLOCK)
        WHERE s.ProductId = @productId AND s.BranchId = @branchId AND s.ProductBatchId IS NOT NULL AND s.ProductBatchId = @batchId
          AND (@cross = 1 OR s.BranchId = @currentBranch)";

    public async Task IncreaseAsync(
        Guid productId, Guid branchId, Guid? productBatchId, decimal quantityBase, CancellationToken cancellationToken)
    {
        if (quantityBase <= 0)
        {
            throw new ArgumentOutOfRangeException(nameof(quantityBase), "كمية الزيادة لازم تكون موجبة.");
        }

        // الصف موجود بالغالب = UPDATE وحدة. غير هيك: INSERT ذري، ولو سبقنا طلب تاني بإنشائه نعيد الـUPDATE مرة (الصف صار موجود أكيد).
        for (var attempt = 0; attempt < 2; attempt++)
        {
            if (await ExecuteStockSqlAsync(productBatchId is null ? NoBatchIncrease : BatchIncrease, productId, branchId, productBatchId, quantityBase, cancellationToken) == 1)
            {
                return;
            }

            if (await InsertStockRowIfMissingAsync(productId, branchId, productBatchId, quantityBase, cancellationToken))
            {
                return;
            }
        }

        throw new InvalidOperationException($"تعذّرت زيادة المخزون للصنف {productId} بالفرع {branchId} (الصف مش موجود ولا قابل للإنشاء).");
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
        Guid productId, Guid branchId, Guid? productBatchId, decimal quantityBase, CancellationToken cancellationToken) =>
        await InsertStockRowIfMissingAsync(productId, branchId, productBatchId, -quantityBase, cancellationToken);

    private const string InsertNoBatchIfMissing = @"
        INSERT INTO [Stocks] ([Id], [ProductId], [BranchId], [ProductBatchId], [QuantityOnHand])
        SELECT @id, @productId, @branchId, NULL, @q
        WHERE NOT EXISTS (
            SELECT 1 FROM [Stocks] WITH (INDEX(IX_Stocks_ProductId_BranchId_NoBatch))
            WHERE [ProductId] = @productId AND [BranchId] = @branchId AND [ProductBatchId] IS NULL)";

    private const string InsertBatchIfMissing = @"
        INSERT INTO [Stocks] ([Id], [ProductId], [BranchId], [ProductBatchId], [QuantityOnHand])
        SELECT @id, @productId, @branchId, @batchId, @q
        WHERE NOT EXISTS (
            SELECT 1 FROM [Stocks] WITH (INDEX(IX_Stocks_ProductId_BranchId_ProductBatchId))
            WHERE [ProductId] = @productId AND [BranchId] = @branchId AND [ProductBatchId] IS NOT NULL AND [ProductBatchId] = @batchId)";

    /// <summary>INSERT ذري لصف مخزون بكمية ابتدائية (موجبة لزيادة، سالبة لبيع بلا صف)، بس لو ما في صف بنفس المفتاح - seek على الفهرس الفريد بدل مسح.</summary>
    private async Task<bool> InsertStockRowIfMissingAsync(
        Guid productId, Guid branchId, Guid? productBatchId, decimal initialQuantity, CancellationToken cancellationToken)
    {
        var parameters = new List<SqlParameter>
        {
            new("@id", Guid.NewGuid()),
            new("@q", System.Data.SqlDbType.Decimal) { Precision = 18, Scale = 4, Value = initialQuantity },
            new("@productId", productId),
            new("@branchId", branchId)
        };
        if (productBatchId is { } batchId)
        {
            parameters.Add(new SqlParameter("@batchId", batchId));
        }

        var rows = await _context.Database.ExecuteSqlRawAsync(productBatchId is null ? InsertNoBatchIfMissing : InsertBatchIfMissing, parameters, cancellationToken);
        return rows == 1;
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
