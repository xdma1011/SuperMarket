import { BackupsOperation } from './backups.operations';

/** راجع تعليق auth.operations.spec.ts — Enum مسارات فقط، لا منطق HTTP هون. */
describe('BackupsOperation enum', () => {
  it('يحافظ على القيم الحرفية الصحيحة لكل عملية', () => {
    expect(BackupsOperation.Trigger).toBe('');
    expect(BackupsOperation.TriggerAndDownload).toBe('download');
    expect(BackupsOperation.List).toBe('');
    expect(BackupsOperation.Download).toBe('{id}/download');
    expect(BackupsOperation.Delete).toBe('{id}');
  });
});
