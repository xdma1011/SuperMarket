import { StocktakesOperation } from './stocktakes.operations';

/** راجع تعليق auth.operations.spec.ts — Enum مسارات فقط، لا منطق HTTP هون. */
describe('StocktakesOperation enum', () => {
  it('يحافظ على القيم الحرفية الصحيحة لكل عملية (الجرد بـ4 مراحل)', () => {
    expect(StocktakesOperation.Create).toBe('');
    expect(StocktakesOperation.List).toBe('');
    expect(StocktakesOperation.GetById).toBe('{id}');
    expect(StocktakesOperation.RecordCount).toBe('{id}/items/{itemId}/count');
    expect(StocktakesOperation.Complete).toBe('{id}/complete');
    expect(StocktakesOperation.Approve).toBe('{id}/approve');
  });
});
