import { SalesOperation } from './sales.operations';

/** راجع تعليق auth.operations.spec.ts — Enum مسارات فقط، لا منطق HTTP هون. */
describe('SalesOperation enum', () => {
  it('يحافظ على القيم الحرفية الصحيحة لكل عملية', () => {
    expect(SalesOperation.Complete).toBe('');
    expect(SalesOperation.Void).toBe('{id}/void');
    expect(SalesOperation.List).toBe('');
    expect(SalesOperation.GetById).toBe('{id}');
  });
});
