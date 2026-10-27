using Microsoft.EntityFrameworkCore;
using SupermarketSystem.Domain.Inventory;
using SupermarketSystem.Domain.Payments;
using SupermarketSystem.Infrastructure.Persistence;

namespace SupermarketSystem.IntegrationTests.Chaos;

/// <summary>
/// مدقّق ثوابت مستقل (16/10/2026): بيمشي على القاعدة نفسها بعد أي فوضى (تزامن، محاكاة، فز) وبيجمع كل خرق لثوابت النظام البنيوية - بلا ما يعرف
/// شو "المفروض" يصير بالسيناريو (عكس حكم المحاكاة اللي بيحسب المتوقع): هون بس "القاعدة متسقة مع نفسها؟".
/// </summary>
internal static class InvariantAuditor
{
    private static decimal SignedDelta(MovementType type, decimal quantityBase) => type switch
    {
        MovementType.PurchaseIn or MovementType.ReturnIn or MovementType.VoidReversal or MovementType.AdjustmentIn
            or MovementType.TransferIn or MovementType.StocktakeCorrectionIncrease => Math.Abs(quantityBase),
        _ => -Math.Abs(quantityBase)
    };

    public static async Task<List<string>> AuditAsync(AppDbContext db, IReadOnlyDictionary<Guid, decimal>? openingStockByProduct = null)
    {
        var violations = new List<string>();

        // 1) رصيد المخزون = مجموع حركاته الموقّعة (+ افتتاحي معروف للاختبارات اللي بتبذر مخزون مباشرة).
        var stocks = await db.Stocks.IgnoreQueryFilters().AsNoTracking()
            .Select(s => new { s.ProductId, s.BranchId, s.ProductBatchId, s.QuantityOnHand }).ToListAsync();
        var movements = await db.StockMovements.IgnoreQueryFilters().AsNoTracking()
            .Select(m => new { m.ProductId, m.BranchId, m.ProductBatchId, m.MovementType, m.QuantityBase }).ToListAsync();
        var movementSums = movements.GroupBy(m => (m.ProductId, m.BranchId, m.ProductBatchId))
            .ToDictionary(g => g.Key, g => g.Sum(m => SignedDelta(m.MovementType, m.QuantityBase)));
        foreach (var s in stocks)
        {
            var opening = s.ProductBatchId is null && openingStockByProduct?.GetValueOrDefault(s.ProductId) is { } o ? o : 0m;
            var expected = movementSums.GetValueOrDefault((s.ProductId, s.BranchId, s.ProductBatchId)) + opening;
            if (expected != s.QuantityOnHand)
                violations.Add($"مخزون ≠ مجموع الحركات: صنف {s.ProductId} فرع {s.BranchId} دفعة {s.ProductBatchId} -> رصيد {s.QuantityOnHand} متوقع {expected} " +
                    "[" + string.Join("، ", movements.Where(m => m.ProductId == s.ProductId && m.BranchId == s.BranchId && m.ProductBatchId == s.ProductBatchId)
                        .GroupBy(m => m.MovementType).Select(g => $"{g.Key}:{g.Sum(m => m.QuantityBase):0.###}")) + "]");
        }

        foreach (var key in movementSums.Keys.Where(k => !stocks.Any(s => (s.ProductId, s.BranchId, s.ProductBatchId) == k)))
            if (movementSums[key] != 0) violations.Add($"حركات بلا صف مخزون: {key}");

        // 2) مدفوع الفاتورة = مجموع دفعاتها المكتملة، ولا يتجاوز إجماليها.
        var invoices = await db.SaleInvoices.IgnoreQueryFilters().AsNoTracking()
            .Select(i => new { i.Id, i.InvoiceNumber, i.TotalAmount, i.TotalPaidAmount, i.TotalReturnedAmount }).ToListAsync();
        var paid = (await db.SaleInvoicePayments.IgnoreQueryFilters().AsNoTracking().Where(p => p.Status == PaymentStatus.Completed)
            .GroupBy(p => p.SaleInvoiceId).Select(g => new { Id = g.Key, Sum = g.Sum(p => p.Amount) }).ToListAsync()).ToDictionary(x => x.Id, x => x.Sum);
        foreach (var i in invoices)
        {
            var sum = paid.GetValueOrDefault(i.Id);
            if (sum != i.TotalPaidAmount) violations.Add($"فاتورة {i.InvoiceNumber}: مدفوع {i.TotalPaidAmount} ≠ مجموع الدفعات المكتملة {sum}");
            if (i.TotalPaidAmount > i.TotalAmount) violations.Add($"فاتورة {i.InvoiceNumber}: مدفوع {i.TotalPaidAmount} أكبر من الإجمالي {i.TotalAmount}");
            if (i.TotalPaidAmount < 0 || i.TotalAmount < 0 || i.TotalReturnedAmount < 0) violations.Add($"فاتورة {i.InvoiceNumber}: مبلغ سالب");
        }

        if (await db.SaleInvoicePayments.IgnoreQueryFilters().AnyAsync(p => p.Amount <= 0)) violations.Add("دفعة بيع بمبلغ ≤ 0");

        // 3) المرتجع: إجمالي الفاتورة وكمية كل سطر = مجموع إرجاعاته، والكمية المرتجعة ≤ المباعة.
        var returns = (await db.ReturnInvoices.IgnoreQueryFilters().AsNoTracking()
            .GroupBy(r => r.OriginalSaleInvoiceId).Select(g => new { Id = g.Key, Sum = g.Sum(r => r.TotalAmount) }).ToListAsync()).ToDictionary(x => x.Id, x => x.Sum);
        foreach (var i in invoices)
        {
            var sum = returns.GetValueOrDefault(i.Id);
            if (sum != i.TotalReturnedAmount) violations.Add($"فاتورة {i.InvoiceNumber}: مرتجع {i.TotalReturnedAmount} ≠ مجموع فواتير الإرجاع {sum}");
        }

        var returnedByItem = (await db.ReturnInvoiceItems.IgnoreQueryFilters().AsNoTracking()
            .GroupBy(r => r.SaleInvoiceItemId).Select(g => new { Id = g.Key, Sum = g.Sum(r => r.Quantity) }).ToListAsync()).ToDictionary(x => x.Id, x => x.Sum);
        var items = await db.SaleInvoiceItems.IgnoreQueryFilters().AsNoTracking().Select(i => new { i.Id, i.Quantity, i.QuantityReturned }).ToListAsync();
        foreach (var it in items)
        {
            if (it.Quantity <= 0) violations.Add($"سطر بيع {it.Id} بكمية ≤ 0");
            if (it.QuantityReturned > it.Quantity) violations.Add($"سطر {it.Id}: مرتجع {it.QuantityReturned} أكبر من المباع {it.Quantity}");
            var sum = returnedByItem.GetValueOrDefault(it.Id);
            if (sum != it.QuantityReturned) violations.Add($"سطر {it.Id}: كمية مرتجعة {it.QuantityReturned} ≠ مجموع بنود الإرجاع {sum}");
        }

        // 4) أرقام المستندات فريدة (الفهرس الفريد بالقاعدة هو الحارس، هون تأكيد إضافي).
        var dupSales = invoices.GroupBy(i => i.InvoiceNumber).Where(g => g.Count() > 1).Select(g => g.Key).ToList();
        if (dupSales.Count > 0) violations.Add("أرقام فواتير بيع مكررة: " + string.Join(',', dupSales));

        // 5) إعادة استثمار الشريك: كل إعادة إلها إيداع رأس مال بنفس المبلغ والشريك والفرع، وما بتتجاوز رصيده (أنصبة − سحوبات − بضاعة بسعر التكلفة) بالمجموع.
        var reinvestments = await db.PartnerReinvestments.IgnoreQueryFilters().AsNoTracking()
            .Select(r => new { r.Id, r.PartnerId, r.BranchId, r.Amount, r.CapitalTransactionId }).ToListAsync();
        var capitalIds = reinvestments.Select(r => r.CapitalTransactionId).ToList();
        var capitals = (await db.CapitalTransactions.IgnoreQueryFilters().AsNoTracking().Where(c => capitalIds.Contains(c.Id))
            .Select(c => new { c.Id, c.PartnerId, c.BranchId, c.Amount, c.Type }).ToListAsync()).ToDictionary(c => c.Id);
        foreach (var r in reinvestments)
        {
            if (!capitals.TryGetValue(r.CapitalTransactionId, out var c)
                || c.Type != SupermarketSystem.Domain.Finance.CapitalTransactionType.Deposit || c.Amount != r.Amount || c.PartnerId != r.PartnerId || c.BranchId != r.BranchId)
                violations.Add($"إعادة استثمار {r.Id}: إيداع رأس المال المرتبط ناقص أو غير مطابق");
        }

        // 6) تراجع إعادة الاستثمار: لكل تراجع إعادة أصلية بنفس المبلغ والشريك، وسحب رأس مال مطابق، وما بيزيد المنعكس عن المستثمر (رصيد إعادة الاستثمار ≥ 0).
        var reversals = await db.PartnerReinvestmentReversals.IgnoreQueryFilters().AsNoTracking()
            .Select(r => new { r.Id, r.ReinvestmentId, r.PartnerId, r.BranchId, r.Amount, r.CapitalTransactionId }).ToListAsync();
        var originals = reinvestments.ToDictionary(r => r.Id);
        var reversalCapitalIds = reversals.Select(r => r.CapitalTransactionId).ToList();
        var reversalCapitals = (await db.CapitalTransactions.IgnoreQueryFilters().AsNoTracking().Where(c => reversalCapitalIds.Contains(c.Id))
            .Select(c => new { c.Id, c.PartnerId, c.BranchId, c.Amount, c.Type }).ToListAsync()).ToDictionary(c => c.Id);
        foreach (var r in reversals)
        {
            if (!originals.TryGetValue(r.ReinvestmentId, out var original) || original.Amount != r.Amount || original.PartnerId != r.PartnerId)
                violations.Add($"تراجع {r.Id}: الإعادة الأصلية ناقصة أو غير مطابقة");
            if (!reversalCapitals.TryGetValue(r.CapitalTransactionId, out var c)
                || c.Type != SupermarketSystem.Domain.Finance.CapitalTransactionType.Withdrawal || c.Amount != r.Amount || c.PartnerId != r.PartnerId || c.BranchId != r.BranchId)
                violations.Add($"تراجع {r.Id}: سحب رأس المال المرتبط ناقص أو غير مطابق");
        }

        if (reversals.GroupBy(r => r.ReinvestmentId).Any(g => g.Count() > 1)) violations.Add("إعادة استثمار انعكست أكتر من مرة");

        // 7) ترحيل خسارة المضارب: لكل سطر مضارب المرحَّل الخارج = max(0, الداخل − صافي الكشف)، ما بينزل تحت الصفر، ونصيبه ≥ 0 وبلا نصيب ببداية خسارة.
        var speculativeLines = await (
                from l in db.PartnerStatementLines.AsNoTracking()
                join s in db.PartnerMonthlyStatements.IgnoreQueryFilters().AsNoTracking() on l.StatementId equals s.Id
                where l.PartnerType == SupermarketSystem.Domain.Partners.PartnerType.Speculative
                select new { l.PartnerId, s.Year, s.Month, s.NetProfit, l.LossCarriedIn, l.LossCarriedOut, l.ShareAmount })
            .ToListAsync();
        foreach (var l in speculativeLines)
        {
            if (l.LossCarriedIn < 0 || l.LossCarriedOut < 0) violations.Add($"مضارب {l.PartnerId} {l.Month}/{l.Year}: خسارة مرحّلة سالبة");
            if (l.LossCarriedOut != Math.Max(0m, l.LossCarriedIn - l.NetProfit))
                violations.Add($"مضارب {l.PartnerId} {l.Month}/{l.Year}: مرحَّل خارج {l.LossCarriedOut} ≠ max(0, {l.LossCarriedIn} − {l.NetProfit})");
            if (l.ShareAmount < 0 || (l.NetProfit <= l.LossCarriedIn && l.ShareAmount != 0m))
                violations.Add($"مضارب {l.PartnerId} {l.Month}/{l.Year}: نصيب {l.ShareAmount} رغم إنّ الربح {l.NetProfit} ما غطّى المرحَّل {l.LossCarriedIn}");
        }

        return violations;
    }
}
