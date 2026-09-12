import { ProductCategoriesOperation, ProductsOperation, PriceChangeRequestsOperation } from './catalog.operations';

/** راجع تعليق auth.operations.spec.ts — Enum مسارات فقط، لا منطق HTTP هون. */
describe('ProductCategoriesOperation enum', () => {
  it('يحافظ على القيم الحرفية الصحيحة لكل عملية', () => {
    expect(ProductCategoriesOperation.Create).toBe('');
    expect(ProductCategoriesOperation.List).toBe('');
    expect(ProductCategoriesOperation.Update).toBe('{categoryId}');
  });
});

describe('ProductsOperation enum', () => {
  it('يحافظ على القيم الحرفية الصحيحة لكل عملية', () => {
    expect(ProductsOperation.Create).toBe('');
    expect(ProductsOperation.List).toBe('');
    expect(ProductsOperation.Update).toBe('{productId}');
    expect(ProductsOperation.AddUnit).toBe('{productId}/units');
    expect(ProductsOperation.GetUnits).toBe('{productId}/units');
    expect(ProductsOperation.UpdateUnitBarcode).toBe('{productId}/units/{unitId}/barcode');
    expect(ProductsOperation.GetByBarcode).toBe('by-barcode/{barcodeValue}');
    expect(ProductsOperation.SetComplimentaryAllowed).toBe('{productId}/complimentary-allowed');
    expect(ProductsOperation.GetBranches).toBe('{productId}/branches');
    expect(ProductsOperation.AddBranch).toBe('{productId}/branches');
    expect(ProductsOperation.SetBranchAvailability).toBe('{productId}/branches/{productBranchId}/availability');
    expect(ProductsOperation.RequestPriceChange).toBe('{productId}/branches/{productBranchId}/price-change-requests');
  });
});

describe('PriceChangeRequestsOperation enum', () => {
  it('يحافظ على القيم الحرفية الصحيحة لكل عملية (مسار مستقل عن Products)', () => {
    expect(PriceChangeRequestsOperation.List).toBe('');
    expect(PriceChangeRequestsOperation.Approve).toBe('{requestId}/approve');
    expect(PriceChangeRequestsOperation.Reject).toBe('{requestId}/reject');
  });
});
