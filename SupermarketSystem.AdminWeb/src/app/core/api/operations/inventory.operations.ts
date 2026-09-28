/** ApiController.Inventory */
export enum InventoryOperation {
  RecordComplimentaryIssue = 'complimentary-issues',
  /** GET نفس المسار: سجل الضيافة (جدول صفحة الضيافة). */
  ComplimentaryLog = 'complimentary-issues',
  RecordWasteIssue = 'waste-issues',
  GetCurrentStock = 'current-stock'
}
