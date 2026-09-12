import { ReturnsOperation } from './returns.operations';

/** راجع تعليق auth.operations.spec.ts — Enum مسارات فقط، لا منطق HTTP هون. */
describe('ReturnsOperation enum', () => {
  it('يحافظ على القيم الحرفية الصحيحة لكل عملية', () => {
    expect(ReturnsOperation.Process).toBe('');
    expect(ReturnsOperation.MarkReviewed).toBe('{id}/mark-reviewed');
  });
});
