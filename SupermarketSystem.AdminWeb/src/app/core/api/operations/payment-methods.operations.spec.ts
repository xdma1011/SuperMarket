import { PaymentMethodsOperation } from './payment-methods.operations';

/** راجع تعليق auth.operations.spec.ts — Enum مسارات فقط، لا منطق HTTP هون. */
describe('PaymentMethodsOperation enum', () => {
  it('يحافظ على القيم الحرفية الصحيحة لكل عملية', () => {
    expect(PaymentMethodsOperation.List).toBe('');
  });
});
