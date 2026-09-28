/** ApiController.Sales - صفحة التلفون (ضرب باركود + سحب بسعر التكلفة). */
export enum SaleAssistOperation {
  ScanLookup = 'scan-lookup',
  AtCostQuote = 'at-cost/quote',
  AtCostComplete = 'at-cost'
}

/** ApiController.PreparedOrders - طلبات مساعد الكاشير. */
export enum PreparedOrdersOperation {
  Create = '',
  ListOpen = '',
  Cancel = '{id}/cancel'
}
