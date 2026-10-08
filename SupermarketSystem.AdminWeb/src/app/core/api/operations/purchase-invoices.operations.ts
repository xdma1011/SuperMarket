/** ApiController.PurchaseInvoices */
export enum PurchaseInvoicesOperation {
  Complete = '',
  List = '',
  RecordPayment = '{purchaseInvoiceId}/payments',
  SupplierDebts = 'supplier-debts',
  /** رصيد افتتاحي (بضاعة موجودة قبل تشغيل النظام) - Finance.Manage */
  OpeningBalance = 'opening-balance'
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
