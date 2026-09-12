import { ReviewsOperation } from './reviews.operations';

/** راجع تعليق auth.operations.spec.ts — Enum مسارات فقط، لا منطق HTTP هون. */
describe('ReviewsOperation enum', () => {
  it('يحافظ على القيم الحرفية الصحيحة لكل عملية', () => {
    expect(ReviewsOperation.List).toBe('');
    expect(ReviewsOperation.MarkStockMovementReviewed).toBe('stock-movements/{stockMovementId}/mark-reviewed');
    expect(ReviewsOperation.MarkPurchaseInvoiceItemReviewed).toBe(
      'purchase-invoice-items/{purchaseInvoiceItemId}/mark-reviewed'
    );
    expect(ReviewsOperation.MarkComplaintReviewed).toBe('complaints/{complaintId}/mark-reviewed');
  });
});
