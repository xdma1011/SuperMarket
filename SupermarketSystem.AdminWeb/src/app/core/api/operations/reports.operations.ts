/** ApiController.Reports */
export enum ReportsOperation {
  RecentReturns = 'returns/recent',
  VoidedSales = 'sales/voided',
  ReturnFrequencyByProduct = 'returns/frequency-by-product',
  ManualDiscounts = 'discounts/manual',
  NegativeStock = 'inventory/negative-stock',
  SalesSummary = 'sales/summary',
  BestCashiers = 'cashiers/best',
  BestCustomers = 'customers/best',
  StagnantProducts = 'products/stagnant',
  ReorderNeededProducts = 'products/reorder-needed',
  SupplierPriceComparison = 'suppliers/price-comparison',
  RecentReturnedItems = 'returns/recent-items',
  CurrentCapitalValue = 'inventory/capital-value',
  ConsumptionLevels = 'products/consumption-levels'
}
