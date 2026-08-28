/** ApiController.PurchaseInvoices (يشمل InvoiceOcrEndpoints - نفس المسار الأساسي بالباك إند) */
export enum PurchaseInvoicesOperation {
  Complete = '',
  List = '',
  ExtractFromImage = 'extract-from-image',
  RecordPayment = '{purchaseInvoiceId}/payments',
  SupplierDebts = 'supplier-debts'
}
