import { Component, OnInit, computed, signal, inject } from '@angular/core';
import { CommonModule } from '@angular/common';
import { FormsModule } from '@angular/forms';
import { firstValueFrom } from 'rxjs';
import { ApiClient } from '../../core/api/api-client.service';
import { ApiController } from '../../core/api/api-controller.enum';
import { BranchesOperation, FinanceOperation, PartnersOperation } from '../../core/api/operations';
import { PaginationComponent } from '../../shared/components/pagination/pagination.component';
import { AuthService } from '../../core/services/auth.service';
import { BusinessTimeService } from '../../core/services/business-time.service';

interface BranchDto {
  id: string;
  name: string;
}

interface PagedResult<T> {
  items: T[];
  totalCount: number;
}

/** قيم الإرسال (أرقام) - الباك إند بيقبلها، بس بيرجّع الأسماء بالردود (JsonStringEnumConverter عام). */
type CapitalTransactionType = 1 | 2;
type ExpenseCategoryName = 'Rent' | 'Electricity' | 'Water' | 'Salary' | 'Other';
type CapitalTransactionTypeName = 'Deposit' | 'Withdrawal';

interface ExpenseListItemDto {
  id: string;
  branchId: string;
  category: ExpenseCategoryName;
  amount: number;
  paymentDateUtc: string;
  periodYear: number;
  periodMonth: number;
  notes: string | null;
  recordedByUsername: string;
  /** 29/9/2026: النوع اللي بيعرّفه صاحب المحل، "من الصندوق"، والموظف لو المصروف راتب من صفحة الموظفين. */
  expenseTypeId?: string | null;
  expenseTypeName?: string;
  paidFromDrawer?: boolean;
  employeeName?: string | null;
}

export interface ExpenseTypeDto {
  id: string;
  name: string;
  isActive: boolean;
  sortOrder: number;
  isBuiltIn: boolean;
  expenseCount: number;
}

interface ExpenseByTypeDto {
  expenseTypeId: string | null;
  typeName: string;
  amount: number;
}

interface CapitalTransactionListItemDto {
  id: string;
  branchId: string;
  type: CapitalTransactionTypeName;
  amount: number;
  occurredAtUtc: string;
  notes: string | null;
  recordedByUsername: string;
  /** رأس مال شريك (وحدة الشركاء) - أساس نسبته من الربح. */
  partnerId?: string | null;
  partnerName?: string | null;
}

interface CapitalPartnerOption {
  id: string;
  fullName: string;
  typeCode: number;
  isActive: boolean;
}

interface ExpenseByCategoryDto {
  category: ExpenseCategoryName;
  amount: number;
}

interface GetMonthlyProfitStatementResponse {
  branchId: string;
  year: number;
  month: number;
  totalSales: number;
  totalReturnedAmount: number;
  netRevenue: number;
  costOfGoodsSold: number;
  itemsExcludedNoCostHistory: number;
  grossProfit: number;
  totalExpenses: number;
  expensesByCategory: ExpenseByCategoryDto[];
  stocktakeSurplusValue: number;
  stocktakeShortageValue: number;
  stocktakeMovementsExcludedNoCostHistory: number;
  wasteLossValue: number;
  wasteMovementsExcludedNoCostHistory: number;
  netProfit: number;
  expensesByType?: ExpenseByTypeDto[];
  /** تكلفة الضيافة بالشهر (29/9/2026) - بتنطرح من صافي الربح. */
  complimentaryCostValue?: number;
  complimentaryMovementsExcludedNoCostHistory?: number;
}

const EXPENSE_CATEGORY_LABELS: Record<ExpenseCategoryName, string> = {
  Rent: 'إيجار',
  Electricity: 'كهرباء',
  Water: 'ماء',
  Salary: 'راتب',
  Other: 'أخرى'
};

const CAPITAL_TYPE_LABELS: Record<CapitalTransactionTypeName, string> = {
  Deposit: 'إضافة',
  Withdrawal: 'سحب'
};

type FinanceTab = 'statement' | 'expenses' | 'types' | 'capital';

const MONTH_NAMES = [
  'كانون الثاني', 'شباط', 'آذار', 'نيسان', 'أيار', 'حزيران',
  'تموز', 'آب', 'أيلول', 'تشرين الأول', 'تشرين الثاني', 'كانون الأول'
];

/**
 * مصاريف تشغيلية + حركات رأس مال + كشف الربح الشهري - Finance.Manage
 * حصرًا (Master Admin افتراضيًا)، طلب صاحب المشروع المباشر (15-17/9/2026):
 * "بدقة شديدة". راجع تعليق GetMonthlyProfitStatementQuery.cs بالباك إند
 * لتعريف كل رقم هون بالضبط.
 */
@Component({
  selector: 'app-finance',
  standalone: true,
  imports: [CommonModule, FormsModule, PaginationComponent],
  templateUrl: './finance.component.html',
  styleUrl: './finance.component.css'
})
export class FinanceComponent implements OnInit {
  private readonly auth = inject(AuthService);
  private readonly businessTime = inject(BusinessTimeService);

  readonly branches = signal<BranchDto[]>([]);
  readonly errorMessage = signal<string | null>(null);

  readonly activeTab = signal<FinanceTab>('statement');

  // --- كشف الربح الشهري ---
  readonly statement = signal<GetMonthlyProfitStatementResponse | null>(null);
  readonly statementLoading = signal(false);
  readonly statementError = signal<string | null>(null);
  statementBranchId = '';
  statementYear = this.businessTime.localYearMonth().year;
  statementMonth = this.businessTime.localYearMonth().month;

  // --- المصاريف ---
  readonly expenses = signal<ExpenseListItemDto[]>([]);
  readonly expensesTotalCount = signal(0);
  readonly expensesPageNumber = signal(1);
  readonly expensesPageSize = signal(20);
  readonly expensesLoading = signal(true);
  filterExpenseBranchId = '';
  filterExpenseTypeId = '';

  // --- أنواع المصاريف (صاحب المحل بيعرّفها) ---
  readonly expenseTypes = signal<ExpenseTypeDto[]>([]);
  readonly activeExpenseTypes = computed(() => this.expenseTypes().filter(t => t.isActive));
  readonly typesError = signal<string | null>(null);
  readonly typesBusy = signal(false);
  newTypeName = '';
  /** تعديل اسم نوع بمكانه: id → الاسم الجديد. */
  readonly editingTypeId = signal<string | null>(null);
  editingTypeName = '';

  readonly expenseFormOpen = signal(false);
  readonly expenseSubmitting = signal(false);
  readonly expenseFormError = signal<string | null>(null);
  newExpenseBranchId = '';
  newExpenseTypeId = '';
  newExpensePaidFromDrawer = false;
  newExpenseAmount: number | null = null;
  newExpensePaymentDate = this.businessTime.localDate();
  newExpensePeriodYear = this.businessTime.localYearMonth().year;
  newExpensePeriodMonth = this.businessTime.localYearMonth().month;
  newExpenseNotes = '';

  // --- حركات رأس المال ---
  readonly capitalTransactions = signal<CapitalTransactionListItemDto[]>([]);
  readonly capitalTotalCount = signal(0);
  readonly capitalPageNumber = signal(1);
  readonly capitalPageSize = signal(20);
  readonly capitalLoading = signal(true);
  filterCapitalBranchId = '';

  readonly capitalFormOpen = signal(false);
  readonly capitalSubmitting = signal(false);
  readonly capitalFormError = signal<string | null>(null);
  newCapitalBranchId = '';
  newCapitalType: CapitalTransactionType = 1;
  newCapitalAmount: number | null = null;
  newCapitalOccurredAt = this.businessTime.localDate();
  newCapitalNotes = '';
  /** شركاء رأس المال بفرع النموذج - اختياري (فاضي = حركة عامة للفرع). */
  readonly capitalPartners = signal<CapitalPartnerOption[]>([]);
  newCapitalPartnerId = '';

  readonly categoryLabels = EXPENSE_CATEGORY_LABELS;
  readonly typeLabels = CAPITAL_TYPE_LABELS;
  readonly monthNames = MONTH_NAMES;

  constructor(private readonly apiClient: ApiClient) {}

  ngOnInit(): void {
    this.loadBranches();
    this.loadExpenses();
    this.loadCapitalTransactions();
    void this.loadExpenseTypes();
  }

  branchName(branchId: string): string {
    return this.branches().find(b => b.id === branchId)?.name ?? branchId;
  }

  private async loadBranches(): Promise<void> {
    try {
      const result = await firstValueFrom(
        this.apiClient.get<PagedResult<BranchDto>>(ApiController.Branches, BranchesOperation.List, undefined, { pageSize: 500 })
      );
      this.branches.set(result.items);
      if (result.items.length > 0) {
        this.statementBranchId ||= this.auth.defaultBranchId(result.items);
        this.newExpenseBranchId ||= this.auth.defaultBranchId(result.items);
        this.newCapitalBranchId ||= this.auth.defaultBranchId(result.items);
      }
    } catch {
      this.errorMessage.set('تعذّر تحميل الفروع.');
    }
  }

  setTab(tab: FinanceTab): void {
    this.activeTab.set(tab);
  }

  // === كشف الربح الشهري ===

  async loadStatement(): Promise<void> {
    if (!this.statementBranchId) return;

    this.statementLoading.set(true);
    this.statementError.set(null);

    try {
      const result = await firstValueFrom(
        this.apiClient.get<GetMonthlyProfitStatementResponse>(ApiController.Finance, FinanceOperation.GetProfitStatement, undefined, {
          branchId: this.statementBranchId,
          year: this.statementYear,
          month: this.statementMonth
        })
      );
      this.statement.set(result);
    } catch (err: unknown) {
      this.statement.set(null);
      this.statementError.set(this.extractErrorMessage(err) ?? 'تعذّر جلب كشف الربح لهذه الفترة.');
    } finally {
      this.statementLoading.set(false);
    }
  }

  // === المصاريف ===

  async loadExpenses(): Promise<void> {
    this.expensesLoading.set(true);

    try {
      const result = await firstValueFrom(
        this.apiClient.get<PagedResult<ExpenseListItemDto>>(ApiController.Finance, FinanceOperation.GetExpenses, undefined, {
          pageNumber: this.expensesPageNumber(),
          pageSize: this.expensesPageSize(),
          branchId: this.filterExpenseBranchId || undefined,
          expenseTypeId: this.filterExpenseTypeId || undefined
        })
      );
      this.expenses.set(result.items);
      this.expensesTotalCount.set(result.totalCount);
    } catch {
      this.errorMessage.set('تعذّر تحميل قائمة المصاريف.');
    } finally {
      this.expensesLoading.set(false);
    }
  }

  onExpensesFilterChange(): void {
    this.expensesPageNumber.set(1);
    this.loadExpenses();
  }

  onExpensesPageChanged(event: { pageNumber: number; pageSize: number }): void {
    this.expensesPageNumber.set(event.pageNumber);
    this.expensesPageSize.set(event.pageSize);
    this.loadExpenses();
  }

  openExpenseForm(): void {
    this.expenseFormOpen.set(true);
    this.expenseFormError.set(null);
    this.newExpenseBranchId ||= this.branches()[0]?.id ?? '';
    this.newExpenseTypeId = '';
    this.newExpensePaidFromDrawer = false;
    this.newExpenseAmount = null;
    this.newExpensePaymentDate = this.businessTime.localDate();
    this.newExpensePeriodYear = this.businessTime.localYearMonth().year;
    this.newExpensePeriodMonth = this.businessTime.localYearMonth().month;
    this.newExpenseNotes = '';
  }

  closeExpenseForm(): void {
    this.expenseFormOpen.set(false);
  }

  async submitExpense(): Promise<void> {
    if (!this.newExpenseBranchId || this.newExpenseAmount === null || this.newExpenseAmount <= 0) {
      this.expenseFormError.set('عبّي الفرع والمبلغ (لازم يكون أكبر من صفر).');
      return;
    }

    if (!this.newExpenseTypeId) {
      this.expenseFormError.set('اختار نوع المصروف.');
      return;
    }

    this.expenseSubmitting.set(true);
    this.expenseFormError.set(null);

    try {
      await firstValueFrom(
        this.apiClient.post(ApiController.Finance, FinanceOperation.CreateExpense, {
          branchId: this.newExpenseBranchId,
          expenseTypeId: this.newExpenseTypeId,
          paidFromDrawer: this.newExpensePaidFromDrawer,
          amount: this.newExpenseAmount,
          paymentDateUtc: this.newExpensePaymentDate,
          periodYear: this.newExpensePeriodYear,
          periodMonth: this.newExpensePeriodMonth,
          notes: this.newExpenseNotes || null
        })
      );

      this.closeExpenseForm();
      this.expensesPageNumber.set(1);
      await this.loadExpenses();
    } catch (err: unknown) {
      this.expenseFormError.set(this.extractErrorMessage(err) ?? 'تعذّر تسجيل المصروف.');
    } finally {
      this.expenseSubmitting.set(false);
    }
  }

  // === أنواع المصاريف ===

  expenseTypeLabel(e: ExpenseListItemDto): string {
    return e.expenseTypeName || this.categoryLabels[e.category] || '—';
  }

  async loadExpenseTypes(): Promise<void> {
    try {
      const result = await firstValueFrom(
        this.apiClient.get<ExpenseTypeDto[]>(ApiController.Finance, FinanceOperation.ExpenseTypes, undefined, { includeInactive: true })
      );
      this.expenseTypes.set(Array.isArray(result) ? result : []);
    } catch {
      this.typesError.set('تعذّر تحميل أنواع المصاريف.');
    }
  }

  async addExpenseType(): Promise<void> {
    const name = this.newTypeName.trim();
    if (!name) {
      this.typesError.set('اكتب اسم النوع.');
      return;
    }

    await this.runTypeAction(async () => {
      await firstValueFrom(this.apiClient.post(ApiController.Finance, FinanceOperation.CreateExpenseType, { name }));
      this.newTypeName = '';
    }, 'تعذّر إضافة النوع.');
  }

  startRenameType(type: ExpenseTypeDto): void {
    this.editingTypeId.set(type.id);
    this.editingTypeName = type.name;
  }

  cancelRenameType(): void {
    this.editingTypeId.set(null);
  }

  async saveRenameType(type: ExpenseTypeDto): Promise<void> {
    const name = this.editingTypeName.trim();
    if (!name) {
      this.typesError.set('اكتب اسم النوع.');
      return;
    }

    await this.runTypeAction(async () => {
      await firstValueFrom(this.apiClient.put(ApiController.Finance, FinanceOperation.UpdateExpenseType, { name, isActive: type.isActive }, { id: type.id }));
      this.editingTypeId.set(null);
    }, 'تعذّر تعديل النوع.');
  }

  async toggleTypeActive(type: ExpenseTypeDto): Promise<void> {
    await this.runTypeAction(() =>
      firstValueFrom(this.apiClient.put(ApiController.Finance, FinanceOperation.UpdateExpenseType, { name: type.name, isActive: !type.isActive }, { id: type.id })),
      'تعذّر تغيير حالة النوع.');
  }

  async moveType(type: ExpenseTypeDto, up: boolean): Promise<void> {
    await this.runTypeAction(() =>
      firstValueFrom(this.apiClient.post(ApiController.Finance, FinanceOperation.MoveExpenseType, { up }, { id: type.id })),
      'تعذّر تغيير الترتيب.');
  }

  private async runTypeAction(action: () => Promise<unknown>, fallback: string): Promise<void> {
    this.typesBusy.set(true);
    this.typesError.set(null);
    try {
      await action();
      await this.loadExpenseTypes();
    } catch (err: unknown) {
      this.typesError.set(this.extractErrorMessage(err) ?? fallback);
    } finally {
      this.typesBusy.set(false);
    }
  }

  // === حركات رأس المال ===

  async loadCapitalTransactions(): Promise<void> {
    this.capitalLoading.set(true);

    try {
      const result = await firstValueFrom(
        this.apiClient.get<PagedResult<CapitalTransactionListItemDto>>(
          ApiController.Finance,
          FinanceOperation.GetCapitalTransactions,
          undefined,
          {
            pageNumber: this.capitalPageNumber(),
            pageSize: this.capitalPageSize(),
            branchId: this.filterCapitalBranchId || undefined
          }
        )
      );
      this.capitalTransactions.set(result.items);
      this.capitalTotalCount.set(result.totalCount);
    } catch {
      this.errorMessage.set('تعذّر تحميل سجل حركات رأس المال.');
    } finally {
      this.capitalLoading.set(false);
    }
  }

  onCapitalFilterChange(): void {
    this.capitalPageNumber.set(1);
    this.loadCapitalTransactions();
  }

  onCapitalPageChanged(event: { pageNumber: number; pageSize: number }): void {
    this.capitalPageNumber.set(event.pageNumber);
    this.capitalPageSize.set(event.pageSize);
    this.loadCapitalTransactions();
  }

  openCapitalForm(): void {
    this.capitalFormOpen.set(true);
    this.capitalFormError.set(null);
    this.newCapitalBranchId ||= this.branches()[0]?.id ?? '';
    this.newCapitalType = 1;
    this.newCapitalAmount = null;
    this.newCapitalOccurredAt = this.businessTime.localDate();
    this.newCapitalNotes = '';
    this.newCapitalPartnerId = '';
    void this.loadCapitalPartners();
  }

  /** شركاء رأس المال الفعّالين بالفرع المختار (Partners.Manage - لو ما في صلاحية، الخانة ما بتبين). */
  async loadCapitalPartners(): Promise<void> {
    this.capitalPartners.set([]);
    if (!this.newCapitalBranchId) return;
    try {
      const partners = await firstValueFrom(
        this.apiClient.get<CapitalPartnerOption[]>(ApiController.Partners, PartnersOperation.List, undefined, { branchId: this.newCapitalBranchId })
      );
      this.capitalPartners.set(partners.filter(p => p.typeCode === 1 && p.isActive));
    } catch {
      this.capitalPartners.set([]);
    }
  }

  onCapitalBranchChange(): void {
    this.newCapitalPartnerId = '';
    void this.loadCapitalPartners();
  }

  closeCapitalForm(): void {
    this.capitalFormOpen.set(false);
  }

  async submitCapitalTransaction(): Promise<void> {
    if (!this.newCapitalBranchId || this.newCapitalAmount === null || this.newCapitalAmount <= 0) {
      this.capitalFormError.set('عبّي الفرع والمبلغ (لازم يكون أكبر من صفر).');
      return;
    }

    this.capitalSubmitting.set(true);
    this.capitalFormError.set(null);

    try {
      await firstValueFrom(
        this.apiClient.post(ApiController.Finance, FinanceOperation.CreateCapitalTransaction, {
          branchId: this.newCapitalBranchId,
          type: this.newCapitalType,
          amount: this.newCapitalAmount,
          occurredAtUtc: this.newCapitalOccurredAt,
          notes: this.newCapitalNotes || null,
          partnerId: this.newCapitalPartnerId || null
        })
      );

      this.closeCapitalForm();
      this.capitalPageNumber.set(1);
      await this.loadCapitalTransactions();
    } catch (err: unknown) {
      this.capitalFormError.set(this.extractErrorMessage(err) ?? 'تعذّر تسجيل حركة رأس المال.');
    } finally {
      this.capitalSubmitting.set(false);
    }
  }

  private extractErrorMessage(err: unknown): string | null {
    return err && typeof err === 'object' && 'error' in err
      ? ((err as { error?: { detail?: string } }).error?.detail ?? null)
      : null;
  }
}
