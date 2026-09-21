import { Component, OnInit, signal } from '@angular/core';
import { CommonModule } from '@angular/common';
import { FormsModule } from '@angular/forms';
import { firstValueFrom } from 'rxjs';
import { ApiClient } from '../../core/api/api-client.service';
import { ApiController } from '../../core/api/api-controller.enum';
import { BranchesOperation, FinanceOperation } from '../../core/api/operations';
import { PaginationComponent } from '../../shared/components/pagination/pagination.component';

interface BranchDto {
  id: string;
  name: string;
}

interface PagedResult<T> {
  items: T[];
  totalCount: number;
}

type ExpenseCategory = 1 | 2 | 3 | 4 | 5;
type CapitalTransactionType = 1 | 2;

interface ExpenseListItemDto {
  id: string;
  branchId: string;
  category: ExpenseCategory;
  amount: number;
  paymentDateUtc: string;
  periodYear: number;
  periodMonth: number;
  notes: string | null;
  recordedByUsername: string;
}

interface CapitalTransactionListItemDto {
  id: string;
  branchId: string;
  type: CapitalTransactionType;
  amount: number;
  occurredAtUtc: string;
  notes: string | null;
  recordedByUsername: string;
}

interface ExpenseByCategoryDto {
  category: ExpenseCategory;
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
  netProfit: number;
}

const EXPENSE_CATEGORY_LABELS: Record<ExpenseCategory, string> = {
  1: 'إيجار',
  2: 'كهرباء',
  3: 'ماء',
  4: 'راتب',
  5: 'أخرى'
};

const CAPITAL_TYPE_LABELS: Record<CapitalTransactionType, string> = {
  1: 'إضافة',
  2: 'سحب'
};

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
  readonly branches = signal<BranchDto[]>([]);
  readonly errorMessage = signal<string | null>(null);

  readonly activeTab = signal<'statement' | 'expenses' | 'capital'>('statement');

  // --- كشف الربح الشهري ---
  readonly statement = signal<GetMonthlyProfitStatementResponse | null>(null);
  readonly statementLoading = signal(false);
  readonly statementError = signal<string | null>(null);
  statementBranchId = '';
  statementYear = new Date().getFullYear();
  statementMonth = new Date().getMonth() + 1;

  // --- المصاريف ---
  readonly expenses = signal<ExpenseListItemDto[]>([]);
  readonly expensesTotalCount = signal(0);
  readonly expensesPageNumber = signal(1);
  readonly expensesPageSize = signal(20);
  readonly expensesLoading = signal(true);
  filterExpenseBranchId = '';

  readonly expenseFormOpen = signal(false);
  readonly expenseSubmitting = signal(false);
  readonly expenseFormError = signal<string | null>(null);
  newExpenseBranchId = '';
  newExpenseCategory: ExpenseCategory = 1;
  newExpenseAmount: number | null = null;
  newExpensePaymentDate = new Date().toISOString().slice(0, 10);
  newExpensePeriodYear = new Date().getFullYear();
  newExpensePeriodMonth = new Date().getMonth() + 1;
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
  newCapitalOccurredAt = new Date().toISOString().slice(0, 10);
  newCapitalNotes = '';

  readonly categoryLabels = EXPENSE_CATEGORY_LABELS;
  readonly typeLabels = CAPITAL_TYPE_LABELS;
  readonly monthNames = MONTH_NAMES;

  constructor(private readonly apiClient: ApiClient) {}

  ngOnInit(): void {
    this.loadBranches();
    this.loadExpenses();
    this.loadCapitalTransactions();
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
        this.statementBranchId ||= result.items[0].id;
        this.newExpenseBranchId ||= result.items[0].id;
        this.newCapitalBranchId ||= result.items[0].id;
      }
    } catch {
      this.errorMessage.set('تعذّر تحميل الفروع.');
    }
  }

  setTab(tab: 'statement' | 'expenses' | 'capital'): void {
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
          branchId: this.filterExpenseBranchId || undefined
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
    this.newExpenseCategory = 1;
    this.newExpenseAmount = null;
    this.newExpensePaymentDate = new Date().toISOString().slice(0, 10);
    this.newExpensePeriodYear = new Date().getFullYear();
    this.newExpensePeriodMonth = new Date().getMonth() + 1;
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

    this.expenseSubmitting.set(true);
    this.expenseFormError.set(null);

    try {
      await firstValueFrom(
        this.apiClient.post(ApiController.Finance, FinanceOperation.CreateExpense, {
          branchId: this.newExpenseBranchId,
          category: this.newExpenseCategory,
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
    this.newCapitalOccurredAt = new Date().toISOString().slice(0, 10);
    this.newCapitalNotes = '';
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
          notes: this.newCapitalNotes || null
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
