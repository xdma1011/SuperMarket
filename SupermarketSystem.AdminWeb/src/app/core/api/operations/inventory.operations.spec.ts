import { InventoryOperation } from './inventory.operations';

/** راجع تعليق auth.operations.spec.ts — Enum مسارات فقط، لا منطق HTTP هون. */
describe('InventoryOperation enum', () => {
  it('يحافظ على القيم الحرفية الصحيحة لكل عملية', () => {
    expect(InventoryOperation.RecordComplimentaryIssue).toBe('complimentary-issues');
    expect(InventoryOperation.GetCurrentStock).toBe('current-stock');
  });
});
