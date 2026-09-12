import { ReportsOperation } from './reports.operations';

/** راجع تعليق auth.operations.spec.ts — Enum مسارات فقط، لا منطق HTTP هون. */
describe('ReportsOperation enum', () => {
  it('يحافظ على القيم الحرفية الصحيحة لكل التقارير الـ14', () => {
    expect(ReportsOperation.RecentReturns).toBe('returns/recent');
    expect(ReportsOperation.VoidedSales).toBe('sales/voided');
    expect(ReportsOperation.ReturnFrequencyByProduct).toBe('returns/frequency-by-product');
    expect(ReportsOperation.ManualDiscounts).toBe('discounts/manual');
    expect(ReportsOperation.NegativeStock).toBe('inventory/negative-stock');
    expect(ReportsOperation.SalesSummary).toBe('sales/summary');
    expect(ReportsOperation.BestCashiers).toBe('cashiers/best');
    expect(ReportsOperation.BestCustomers).toBe('customers/best');
    expect(ReportsOperation.StagnantProducts).toBe('products/stagnant');
    expect(ReportsOperation.ReorderNeededProducts).toBe('products/reorder-needed');
    expect(ReportsOperation.SupplierPriceComparison).toBe('suppliers/price-comparison');
    expect(ReportsOperation.RecentReturnedItems).toBe('returns/recent-items');
    expect(ReportsOperation.CurrentCapitalValue).toBe('inventory/capital-value');
    expect(ReportsOperation.ConsumptionLevels).toBe('products/consumption-levels');
  });

  it('كل قيم الـEnum فريدة (لا تعارض مسارات بين تقريرين)', () => {
    const values = Object.values(ReportsOperation);
    const uniqueValues = new Set(values);
    expect(uniqueValues.size).toBe(values.length);
  });
});
