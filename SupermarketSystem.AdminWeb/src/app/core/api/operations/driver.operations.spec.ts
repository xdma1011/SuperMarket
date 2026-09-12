import { DriverOperation } from './driver.operations';

/** راجع تعليق auth.operations.spec.ts — Enum مسارات فقط، لا منطق HTTP هون. */
describe('DriverOperation enum', () => {
  it('يحافظ على القيم الحرفية الصحيحة لكل عملية', () => {
    expect(DriverOperation.MyDeliveries).toBe('my-deliveries');
    expect(DriverOperation.Complete).toBe('deliveries/{orderId}/complete');
  });
});
