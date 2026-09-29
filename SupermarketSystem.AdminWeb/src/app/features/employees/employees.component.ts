import { Component, OnInit, computed, inject, signal } from '@angular/core';
import { CommonModule } from '@angular/common';
import { FormsModule } from '@angular/forms';
import { firstValueFrom } from 'rxjs';
import { ApiClient } from '../../core/api/api-client.service';
import { ApiController } from '../../core/api/api-controller.enum';
import { EmployeesOperation } from '../../core/api/operations';
import { AuthService, PublicBranchDto } from '../../core/services/auth.service';

export interface EmployeeDto {
  id: string;
  branchId: string;
  fullName: string;
  phone: string | null;
  monthlySalary: number;
  isActive: boolean;
  notes: string | null;
  outstandingAdvance: number;
  totalSalariesPaid: number;
  totalAdvancesGiven: number;
  lastSalaryYear: number | null;
  lastSalaryMonth: number | null;
}

export interface EmployeePaymentDto {
  id: string;
  employeeId: string;
  employeeName: string;
  typeCode: number;
  typeTitle: string;
  grossAmount: number;
  advanceDeducted: number;
  netPaid: number;
  periodYear: number | null;
  periodMonth: number | null;
  paidFromDrawer: boolean;
  occurredAtUtc: string;
  notes: string | null;
  recordedByName: string;
}

interface RecordPaymentResponse {
  netPaid: number;
  outstandingAdvance: number;
  paidBeforeForPeriod: number;
}

export type PaymentKind = 'Salary' | 'Advance';

const MONTH_NAMES = [
  'كانون الثاني', 'شباط', 'آذار', 'نيسان', 'أيار', 'حزيران',
  'تموز', 'آب', 'أيلول', 'تشرين الأول', 'تشرين الثاني', 'كانون الأول'
];

/**
 * الموظفين والرواتب والسلف (29/9/2026، صاحب المشروع: "كيف بدي اعطي راتب الموظف عندي؟"):
 *   - الراتب بينسجّل مصروف "رواتب" بكامله بكشف ربح شهره (حتى لو انخصم منه سلفة).
 *   - السلفة مش مصروف - دين على الموظف بينخصم من راتب جاي.
 *   - "من الصندوق" = بينقص الكاش المتوقع بالتقفيل باللي طلع فعليًا.
 */
@Component({
  selector: 'app-employees',
  standalone: true,
  imports: [CommonModule, FormsModule],
  templateUrl: './employees.component.html',
  styleUrl: './employees.component.css'
})
export class EmployeesComponent implements OnInit {
  private readonly apiClient = inject(ApiClient);
  private readonly auth = inject(AuthService);

  readonly branches = signal<PublicBranchDto[]>([]);
  branchId = '';
  showInactive = false;

  readonly errorMessage = signal<string | null>(null);
  readonly successMessage = signal<string | null>(null);
  readonly loading = signal(false);
  readonly submitting = signal(false);

  readonly employees = signal<EmployeeDto[]>([]);
  readonly totalMonthlySalaries = computed(() =>
    this.employees().filter(e => e.isActive).reduce((sum, e) => sum + e.monthlySalary, 0));
  readonly totalOutstandingAdvances = computed(() =>
    this.employees().reduce((sum, e) => sum + e.outstandingAdvance, 0));

  // نموذج الموظف
  readonly employeeFormOpen = signal(false);
  editingEmployee: EmployeeDto | null = null;
  formName = '';
  formPhone = '';
  formSalary: number | null = null;
  formNotes = '';

  // نموذج الصرف (راتب/سلفة)
  readonly payTarget = signal<EmployeeDto | null>(null);
  payKind: PaymentKind = 'Salary';
  payAmount: number | null = null;
  payDeduct: number | null = null;
  payMonth = EmployeesComponent.currentMonthValue();
  payFromDrawer = true;
  payNotes = '';
  /** مفتاح التكرار لنفس النموذج (§3.2) - كبستين على "صرف" = عملية وحدة. */
  private payRequestId = '';

  // سجل الصرف
  readonly history = signal<EmployeePaymentDto[]>([]);
  readonly historyEmployee = signal<EmployeeDto | null>(null);

  readonly monthNames = MONTH_NAMES;

  static currentMonthValue(): string {
    const now = new Date();
    return `${now.getFullYear()}-${String(now.getMonth() + 1).padStart(2, '0')}`;
  }

  async ngOnInit(): Promise<void> {
    try {
      const branches = await this.auth.getPublicBranches();
      this.branches.set(branches);
      this.branchId = this.auth.defaultBranchId(branches);
    } catch {
      this.branchId = this.auth.currentBranchId() ?? '';
    }
    await this.load();
  }

  async load(): Promise<void> {
    if (!this.branchId) return;
    this.loading.set(true);
    try {
      const [employees, history] = await Promise.all([
        firstValueFrom(this.apiClient.get<EmployeeDto[]>(ApiController.Employees, EmployeesOperation.List, undefined,
          { branchId: this.branchId, includeInactive: this.showInactive })),
        firstValueFrom(this.apiClient.get<EmployeePaymentDto[]>(ApiController.Employees, EmployeesOperation.Payments, undefined,
          { branchId: this.branchId, employeeId: this.historyEmployee()?.id }))
      ]);
      this.employees.set(employees);
      this.history.set(history);
    } catch (err) {
      this.errorMessage.set(this.errorDetail(err) ?? 'تعذّر تحميل الموظفين.');
    } finally {
      this.loading.set(false);
    }
  }

  async onBranchChange(): Promise<void> {
    this.historyEmployee.set(null);
    await this.load();
  }

  lastSalaryLabel(e: EmployeeDto): string {
    return e.lastSalaryYear && e.lastSalaryMonth ? `${MONTH_NAMES[e.lastSalaryMonth - 1]} ${e.lastSalaryYear}` : 'ولا مرة';
  }

  periodLabel(p: EmployeePaymentDto): string {
    return p.periodYear && p.periodMonth ? `${MONTH_NAMES[p.periodMonth - 1]} ${p.periodYear}` : '—';
  }

  // ===== الموظف =====

  openEmployeeForm(employee?: EmployeeDto): void {
    this.clearMessages();
    this.editingEmployee = employee ?? null;
    this.formName = employee?.fullName ?? '';
    this.formPhone = employee?.phone ?? '';
    this.formSalary = employee?.monthlySalary ?? null;
    this.formNotes = employee?.notes ?? '';
    this.employeeFormOpen.set(true);
  }

  closeEmployeeForm(): void {
    this.employeeFormOpen.set(false);
  }

  async saveEmployee(): Promise<void> {
    if (!this.formName.trim()) {
      this.errorMessage.set('اكتب اسم الموظف.');
      return;
    }
    if (this.formSalary === null || this.formSalary < 0) {
      this.errorMessage.set('اكتب الراتب الشهري (صفر أو أكتر).');
      return;
    }

    const body = {
      fullName: this.formName.trim(),
      phone: this.formPhone.trim() || null,
      monthlySalary: this.formSalary,
      notes: this.formNotes.trim() || null
    };

    await this.run(async () => {
      if (this.editingEmployee) {
        await firstValueFrom(this.apiClient.put(ApiController.Employees, EmployeesOperation.Update,
          { ...body, isActive: this.editingEmployee.isActive }, { id: this.editingEmployee.id }));
      } else {
        await firstValueFrom(this.apiClient.post(ApiController.Employees, EmployeesOperation.Create, { ...body, branchId: this.branchId }));
      }
      this.employeeFormOpen.set(false);
      this.successMessage.set(this.editingEmployee ? 'انحفظ التعديل.' : 'انضاف الموظف.');
    }, 'تعذّر حفظ الموظف.');
  }

  async toggleActive(employee: EmployeeDto): Promise<void> {
    if (employee.isActive && !confirm(`إيقاف ${employee.fullName}؟ سجل رواتبه بيضل، بس ما بيبين بالقائمة.`)) return;
    await this.run(async () => {
      await firstValueFrom(this.apiClient.put(ApiController.Employees, EmployeesOperation.Update, {
        fullName: employee.fullName, phone: employee.phone, monthlySalary: employee.monthlySalary,
        notes: employee.notes, isActive: !employee.isActive
      }, { id: employee.id }));
      this.successMessage.set(employee.isActive ? `انوقف ${employee.fullName}.` : `رجع ${employee.fullName} فعّال.`);
    }, 'تعذّر تغيير حالة الموظف.');
  }

  // ===== الصرف =====

  openPayForm(employee: EmployeeDto, kind: PaymentKind): void {
    this.clearMessages();
    this.payTarget.set(employee);
    this.payKind = kind;
    this.payAmount = kind === 'Salary' && employee.monthlySalary > 0 ? employee.monthlySalary : null;
    // السلفة المتبقية بتنخصم كلها افتراضيًا (بحدود الراتب) - صاحب المحل بيقدر يغيّرها.
    this.payDeduct = kind === 'Salary' && employee.outstandingAdvance > 0
      ? Math.min(employee.outstandingAdvance, employee.monthlySalary || employee.outstandingAdvance)
      : null;
    this.payMonth = EmployeesComponent.currentMonthValue();
    this.payFromDrawer = true;
    this.payNotes = '';
    this.payRequestId = crypto.randomUUID();
  }

  closePayForm(): void {
    this.payTarget.set(null);
  }

  get payNet(): number {
    return Math.max(0, (this.payAmount ?? 0) - (this.payKind === 'Salary' ? this.payDeduct ?? 0 : 0));
  }

  async submitPayment(): Promise<void> {
    const employee = this.payTarget();
    if (!employee) return;
    if (!this.payAmount || this.payAmount <= 0) {
      this.errorMessage.set('اكتب المبلغ (أكبر من صفر).');
      return;
    }
    const deduct = this.payKind === 'Salary' ? this.payDeduct ?? 0 : 0;
    if (deduct < 0 || deduct > this.payAmount) {
      this.errorMessage.set('خصم السلفة لازم يكون بين صفر والراتب.');
      return;
    }
    if (deduct > employee.outstandingAdvance + 0.0005) {
      this.errorMessage.set(`الخصم أكتر من السلف المتبقية (${employee.outstandingAdvance.toFixed(3)}).`);
      return;
    }
    const [year, month] = this.payMonth.split('-').map(Number);
    if (this.payKind === 'Salary' && (!year || !month)) {
      this.errorMessage.set('اختار شهر الراتب.');
      return;
    }

    await this.run(async () => {
      const result = await firstValueFrom(this.apiClient.post<RecordPaymentResponse>(ApiController.Employees, EmployeesOperation.RecordPayment, {
        type: this.payKind,
        amount: this.payAmount,
        advanceDeducted: deduct,
        periodYear: this.payKind === 'Salary' ? year : null,
        periodMonth: this.payKind === 'Salary' ? month : null,
        paidFromDrawer: this.payFromDrawer,
        notes: this.payNotes.trim() || null,
        clientRequestId: this.payRequestId
      }, { id: employee.id }));
      this.payTarget.set(null);
      const where = this.payFromDrawer ? 'من الصندوق' : 'من برّا الصندوق';
      let message = this.payKind === 'Salary'
        ? `انصرف راتب ${employee.fullName}: ${result.netPaid.toFixed(3)} د.أ ${where}، وانسجّل مصروف رواتب ${this.payAmount!.toFixed(3)}.`
        : `انصرفت سلفة ${this.payAmount!.toFixed(3)} د.أ لـ${employee.fullName} ${where}.`;
      if (result.paidBeforeForPeriod > 0) {
        message += ` انتبه: كان منصرف ${result.paidBeforeForPeriod.toFixed(3)} لنفس الشهر قبل.`;
      }
      if (result.outstandingAdvance > 0) {
        message += ` السلف المتبقية عليه: ${result.outstandingAdvance.toFixed(3)}.`;
      }
      this.successMessage.set(message);
    }, 'تعذّر تسجيل الصرف.');
  }

  // ===== السجل =====

  async showHistory(employee: EmployeeDto | null): Promise<void> {
    this.historyEmployee.set(employee);
    await this.load();
  }

  private async run(action: () => Promise<void>, fallback: string): Promise<void> {
    this.submitting.set(true);
    this.errorMessage.set(null);
    try {
      await action();
      await this.load();
    } catch (err) {
      this.errorMessage.set(this.errorDetail(err) ?? fallback);
    } finally {
      this.submitting.set(false);
    }
  }

  private clearMessages(): void {
    this.errorMessage.set(null);
    this.successMessage.set(null);
  }

  private errorDetail(err: unknown): string | null {
    return err && typeof err === 'object' && 'error' in err
      ? ((err as { error?: { detail?: string } }).error?.detail ?? null)
      : null;
  }
}
