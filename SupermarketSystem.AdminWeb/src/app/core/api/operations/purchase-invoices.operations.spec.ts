import { PurchaseInvoicesOperation, PurchaseInvoiceDraftsOperation } from './purchase-invoices.operations';

/** راجع تعليق auth.operations.spec.ts — Enum مسارات فقط، لا منطق HTTP هون. */
describe('PurchaseInvoicesOperation enum', () => {
  it('يحافظ على القيم الحرفية الصحيحة لكل عملية', () => {
    expect(PurchaseInvoicesOperation.Complete).toBe('');
    expect(PurchaseInvoicesOperation.List).toBe('');
    expect(PurchaseInvoicesOperation.RecordPayment).toBe('{purchaseInvoiceId}/payments');
    expect(PurchaseInvoicesOperation.SupplierDebts).toBe('supplier-debts');
  });
});

describe('PurchaseInvoiceDraftsOperation enum', () => {
  it('يحافظ على القيم الحرفية الصحيحة لكل عملية (تحت مسار drafts فرعي)', () => {
    expect(PurchaseInvoiceDraftsOperation.CreateFromImage).toBe('drafts/from-image');
    expect(PurchaseInvoiceDraftsOperation.List).toBe('drafts');
    expect(PurchaseInvoiceDraftsOperation.GetById).toBe('drafts/{draftId}');
    expect(PurchaseInvoiceDraftsOperation.GetImage).toBe('drafts/{draftId}/image');
    expect(PurchaseInvoiceDraftsOperation.Update).toBe('drafts/{draftId}');
    expect(PurchaseInvoiceDraftsOperation.Complete).toBe('drafts/{draftId}/complete');
    expect(PurchaseInvoiceDraftsOperation.Discard).toBe('drafts/{draftId}');
  });
});
