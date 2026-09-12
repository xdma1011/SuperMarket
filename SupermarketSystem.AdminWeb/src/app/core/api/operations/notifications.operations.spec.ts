import { NotificationsOperation } from './notifications.operations';

/** راجع تعليق auth.operations.spec.ts — Enum مسارات فقط، لا منطق HTTP هون. */
describe('NotificationsOperation enum', () => {
  it('يحافظ على القيم الحرفية الصحيحة لكل عملية', () => {
    expect(NotificationsOperation.List).toBe('');
  });
});
