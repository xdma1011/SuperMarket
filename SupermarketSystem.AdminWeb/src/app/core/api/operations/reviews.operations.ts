/** ApiController.Reviews */
export enum ReviewsOperation {
  List = '',
  MarkStockMovementReviewed = 'stock-movements/{stockMovementId}/mark-reviewed',
  MarkPurchaseInvoiceItemReviewed = 'purchase-invoice-items/{purchaseInvoiceItemId}/mark-reviewed',
  MarkComplaintReviewed = 'complaints/{complaintId}/mark-reviewed'
}
