import { UsersOperation } from './users.operations';

/** راجع تعليق auth.operations.spec.ts — Enum مسارات فقط، لا منطق HTTP هون. */
describe('UsersOperation enum', () => {
  it('يحافظ على القيم الحرفية الصحيحة لكل عملية', () => {
    expect(UsersOperation.Create).toBe('');
    expect(UsersOperation.List).toBe('');
    expect(UsersOperation.Update).toBe('{userId}');
    expect(UsersOperation.ListRoles).toBe('roles');
  });
});
