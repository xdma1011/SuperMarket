import { OrdersOperation } from './orders.operations';

/** راجع تعليق auth.operations.spec.ts — Enum مسارات فقط، لا منطق HTTP هون. */
describe('OrdersOperation enum', () => {
  it('يحافظ على القيم الحرفية الصحيحة لكل عملية', () => {
    expect(OrdersOperation.List).toBe('');
    expect(OrdersOperation.GetById).toBe('{orderId}');
    expect(OrdersOperation.Accept).toBe('{orderId}/accept');
    expect(OrdersOperation.Reject).toBe('{orderId}/reject');
    expect(OrdersOperation.Complete).toBe('{orderId}/complete');
    expect(OrdersOperation.AssignDriver).toBe('{orderId}/assign-driver');
    expect(OrdersOperation.GetDrivers).toBe('drivers');
  });
});
