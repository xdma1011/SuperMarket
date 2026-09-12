import { BranchesOperation } from './branches.operations';

/** راجع تعليق auth.operations.spec.ts — Enum مسارات فقط، لا منطق HTTP هون. */
describe('BranchesOperation enum', () => {
  it('يحافظ على القيم الحرفية الصحيحة لكل عملية', () => {
    expect(BranchesOperation.Create).toBe('');
    expect(BranchesOperation.List).toBe('');
    expect(BranchesOperation.Update).toBe('{branchId}');
    expect(BranchesOperation.SetActive).toBe('{branchId}/active');
  });
});
