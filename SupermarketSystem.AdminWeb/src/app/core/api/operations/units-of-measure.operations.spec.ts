import { UnitsOfMeasureOperation } from './units-of-measure.operations';

/** راجع تعليق auth.operations.spec.ts — Enum مسارات فقط، لا منطق HTTP هون. */
describe('UnitsOfMeasureOperation enum', () => {
  it('يحافظ على القيم الحرفية الصحيحة لكل عملية', () => {
    expect(UnitsOfMeasureOperation.List).toBe('');
    expect(UnitsOfMeasureOperation.Create).toBe('');
    expect(UnitsOfMeasureOperation.SetActive).toBe('{unitOfMeasureId}/active');
  });
});
