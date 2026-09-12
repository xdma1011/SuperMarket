import { SuppliersOperation } from './suppliers.operations';

/** راجع تعليق auth.operations.spec.ts — Enum مسارات فقط، لا منطق HTTP هون. */
describe('SuppliersOperation enum', () => {
  it('يحافظ على القيم الحرفية الصحيحة لكل عملية', () => {
    expect(SuppliersOperation.Create).toBe('');
    expect(SuppliersOperation.List).toBe('');
    expect(SuppliersOperation.Update).toBe('{supplierId}');
  });
});
