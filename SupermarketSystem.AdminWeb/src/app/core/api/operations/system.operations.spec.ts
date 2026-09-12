import { SystemOperation } from './system.operations';

/** راجع تعليق auth.operations.spec.ts — Enum مسارات فقط، لا منطق HTTP هون. */
describe('SystemOperation enum', () => {
  it('يحافظ على القيم الحرفية الصحيحة لكل عملية', () => {
    expect(SystemOperation.GetAdminSettings).toBe('admin-settings');
    expect(SystemOperation.UpdateAdminSetting).toBe('admin-settings');
    expect(SystemOperation.GetSecretSettings).toBe('secrets');
    expect(SystemOperation.UpdateSecretSetting).toBe('secrets');
  });
});
