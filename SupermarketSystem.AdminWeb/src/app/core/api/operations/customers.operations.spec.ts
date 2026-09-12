import { CustomersOperation } from './customers.operations';

/** راجع تعليق auth.operations.spec.ts — Enum مسارات فقط، لا منطق HTTP هون. */
describe('CustomersOperation enum', () => {
  it('يحافظ على القيم الحرفية الصحيحة لكل عملية', () => {
    expect(CustomersOperation.List).toBe('');
    expect(CustomersOperation.Block).toBe('{customerId}/block');
    expect(CustomersOperation.Unblock).toBe('{customerId}/unblock');
  });
});
