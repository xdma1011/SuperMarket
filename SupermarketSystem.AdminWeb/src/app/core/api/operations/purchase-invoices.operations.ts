/** ApiController.PurchaseInvoices */
export enum PurchaseInvoicesOperation {
  Complete = '',
  List = '',
  RecordPayment = '{purchaseInvoiceId}/payments',
  SupplierDebts = 'supplier-debts'
}

/** ApiController.PurchaseInvoices - PurchaseInvoiceDraftEndpoints (نفس المسار الأساسي، مسار فرعي drafts/...) */
export enum PurchaseInvoiceDraftsOperation {
  CreateFromImage = 'drafts/from-image',
  List = 'drafts',
  GetById = 'drafts/{draftId}',
  GetImage = 'drafts/{draftId}/image',
  Update = 'drafts/{draftId}',
  Complete = 'drafts/{draftId}/complete',
  Discard = 'drafts/{draftId}'
}
