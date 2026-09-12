import { StockTransfersOperation } from './stock-transfers.operations';

/** راجع تعليق auth.operations.spec.ts — Enum مسارات فقط، لا منطق HTTP هون. */
describe('StockTransfersOperation enum', () => {
  it('يحافظ على القيم الحرفية الصحيحة لكل عملية', () => {
    expect(StockTransfersOperation.List).toBe('');
    expect(StockTransfersOperation.Create).toBe('');
    expect(StockTransfersOperation.Detail).toBe('{stockTransferId}');
    expect(StockTransfersOperation.Receive).toBe('{stockTransferId}/receive');
    expect(StockTransfersOperation.ProductBatches).toBe('product-batches');
  });
});
