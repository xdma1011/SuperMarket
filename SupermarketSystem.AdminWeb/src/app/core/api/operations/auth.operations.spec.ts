import { AuthOperation, AuthSessionsOperation } from './auth.operations';

/**
 * ملفات operations هون كلها Enums لمسارات فرعية فقط، بلا أي دالة أو منطق
 * فعلي (استدعاء HTTP الفعلي يصير عبر ApiClient بالخدمات/المكوّنات، مُختبَر
 * هناك). الاختبار هون يثبّت القيم الحرفية لمنع تغيير مسار API بالخطأ
 * (typo) بلا ما ينكسر شي واضح أثناء البناء.
 */
describe('AuthOperation enum', () => {
  it('يحافظ على القيم الحرفية الصحيحة لكل عملية', () => {
    expect(AuthOperation.Login).toBe('login');
    expect(AuthOperation.Refresh).toBe('refresh');
    expect(AuthOperation.Logout).toBe('logout');
    expect(AuthOperation.PublicBranches).toBe('branches');
    expect(AuthOperation.MyPermissions).toBe('my-permissions');
  });
});

describe('AuthSessionsOperation enum', () => {
  it('يحافظ على القيم الحرفية الصحيحة لكل عملية', () => {
    expect(AuthSessionsOperation.List).toBe('');
    expect(AuthSessionsOperation.Revoke).toBe('{id}/revoke');
  });
});
