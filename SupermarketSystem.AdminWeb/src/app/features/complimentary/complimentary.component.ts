import { Component, OnInit, signal, inject } from '@angular/core';
import { CommonModule } from '@angular/common';
import { FormsModule } from '@angular/forms';
import { firstValueFrom } from 'rxjs';
import { ApiClient } from '../../core/api/api-client.service';
import { isRequestCancelled, latestRequest } from '../../core/api/latest-request';
import { ApiController } from '../../core/api/api-controller.enum';
import { ProductsOperation, BranchesOperation, InventoryOperation } from '../../core/api/operations';
import { AuthService } from '../../core/services/auth.service';
import { BusinessTimeService } from '../../core/services/business-time.service';

interface ProductDto {
  id: string;
  name: string;
}

interface BranchDto {
  id: string;
  name: string;
}

interface ProductUnitDto {
  id: string;
  unitName: string;
  isBaseUnit: boolean;
}

interface PagedResult<T> {
  items: T[];
  totalCount: number;
}

interface ComplimentaryLogItem {
  stockMovementId: string;
  productName: string;
  quantityBase: number;
  notes: string | null;
  needsReview: boolean;
  occurredAtUtc: string;
  username: string;
}

export interface ComplimentaryMonthlySummary {
  year: number;
  month: number;
  issueCount: number;
  needsReviewCount: number;
  totalCostValue: number;
  totalSellingValue: number;
  excludedNoCostHistory: number;
  excludedNoSellingPrice: number;
  previousMonthCostValue: number;
  byProduct: { productId: string; productName: string; quantityBase: number; issueCount: number; costValue: number | null; sellingValue: number | null }[];
  byUser: { userId: string; fullName: string; issueCount: number; costValue: number }[];
  last12Months: { year: number; month: number; issueCount: number; costValue: number }[];
}

const LOG_PAGE_SIZE = 20;

const ARABIC_MONTHS = ['كانون الثاني', 'شباط', 'آذار', 'نيسان', 'أيار', 'حزيران', 'تموز', 'آب', 'أيلول', 'تشرين الأول', 'تشرين الثاني', 'كانون الأول'];

/**
 * تسجيل خروج بضاعة كضيافة بسرعة (بلا أي قيد مالي)، وتحته جدول بكل الضيافات المسجّلة بالفرع (شو، قديش،
 * مين، إمتى، وهل انعلّمت للمراجعة لأنها فوق الحد اليومي) - طلب صاحب المشروع 28/9/2026.
 */
@Component({
  selector: 'app-complimentary',
  standalone: true,
  imports: [CommonModule, FormsModule],
  templateUrl: './complimentary.component.html',
  styleUrl: './complimentary.component.css'
})
export class ComplimentaryComponent implements OnInit {
  private readonly unitsRequest = latestRequest();
  private readonly summaryRequest = latestRequest();
  private readonly logRequest = latestRequest();
  private readonly auth = inject(AuthService);
  private readonly businessTime = inject(BusinessTimeService);

  // لوحة مجموع الضيافة الشهرية كمال (30/9/2026) - نفس رقم "الضيافة" بكشف الربح.
  summaryYear = 0;
  summaryMonthNumber = 0;
  readonly monthOptions = ARABIC_MONTHS.map((name, i) => ({ value: i + 1, name }));
  yearOptions: number[] = [];
  readonly summary = signal<ComplimentaryMonthlySummary | null>(null);
  readonly loadingSummary = signal(false);
  readonly summaryError = signal<string | null>(null);
  readonly showTrendTable = signal(false);

  readonly products = signal<ProductDto[]>([]);
  readonly branches = signal<BranchDto[]>([]);
  readonly units = signal<ProductUnitDto[]>([]);
  readonly loading = signal(true);
  readonly loadingUnits = signal(false);

  readonly submitting = signal(false);
  readonly errorMessage = signal<string | null>(null);
  readonly successMessage = signal<string | null>(null);

  selectedProductId = '';
  selectedBranchId = '';
  selectedUnitId = '';
  quantity: number | null = null;
  reason = '';

  readonly log = signal<ComplimentaryLogItem[]>([]);
  readonly logTotal = signal(0);
  readonly logPage = signal(1);
  readonly loadingLog = signal(false);
  readonly logError = signal<string | null>(null);
  readonly logPageSize = LOG_PAGE_SIZE;

  get logPageCount(): number {
    return Math.max(1, Math.ceil(this.logTotal() / LOG_PAGE_SIZE));
  }

  constructor(private readonly apiClient: ApiClient) {}

  ngOnInit(): void {
    const { year, month } = this.businessTime.localYearMonth();
    this.summaryYear = year;
    this.summaryMonthNumber = month;
    this.yearOptions = [year - 2, year - 1, year];
    this.loadAll();
  }

  private async loadAll(): Promise<void> {
    this.loading.set(true);
    this.errorMessage.set(null);

    try {
      const [productsResult, branchesResult] = await Promise.all([
        firstValueFrom(this.apiClient.get<PagedResult<ProductDto>>(ApiController.Products, ProductsOperation.List, undefined, { pageSize: 500 })),
        firstValueFrom(this.apiClient.get<PagedResult<BranchDto>>(ApiController.Branches, BranchesOperation.List, undefined, { pageSize: 500 }))
      ]);

      this.products.set(productsResult.items);
      this.branches.set(branchesResult.items);

      if (branchesResult.items.length > 0) this.selectedBranchId = this.auth.defaultBranchId(branchesResult.items);
      if (productsResult.items.length > 0) {
        this.selectedProductId = productsResult.items[0].id;
        await this.onProductChange();
      }
      await Promise.all([this.loadLog(1), this.loadSummary()]);
    } catch {
      this.errorMessage.set('تعذّر تحميل البيانات الأساسية.');
    } finally {
      this.loading.set(false);
    }
  }

  async onProductChange(): Promise<void> {
    if (!this.selectedProductId) {
      this.units.set([]);
      return;
    }

    this.loadingUnits.set(true);
    this.units.set([]);
    this.selectedUnitId = '';

    try {
      const units = await this.unitsRequest.run(
        this.apiClient.get<ProductUnitDto[]>(ApiController.Products, ProductsOperation.GetUnits, { productId: this.selectedProductId })
      );
      this.units.set(units);
      const baseUnit = units.find(u => u.isBaseUnit) ?? units[0];
      if (baseUnit) this.selectedUnitId = baseUnit.id;
    } catch (err) {
      if (isRequestCancelled(err)) return;
      this.errorMessage.set('تعذّر جلب وحدات هذا المنتج.');
    } finally {
      this.loadingUnits.set(false);
    }
  }

  async submit(): Promise<void> {
    if (!this.selectedProductId || !this.selectedBranchId || !this.selectedUnitId || !this.quantity || this.quantity <= 0) {
      this.errorMessage.set('عبّي كل الحقول المطلوبة (المنتج، الفرع، الوحدة، كمية موجبة).');
      return;
    }

    this.submitting.set(true);
    this.errorMessage.set(null);
    this.successMessage.set(null);

    try {
      await firstValueFrom(
        this.apiClient.post(ApiController.Inventory, InventoryOperation.RecordComplimentaryIssue, {
          productId: this.selectedProductId,
          productUnitId: this.selectedUnitId,
          branchId: this.selectedBranchId,
          quantity: this.quantity,
          reason: this.reason.trim() || null
        })
      );

      this.successMessage.set('تم تسجيل الضيافة بنجاح، ونقص المخزون فورًا.');
      this.quantity = null;
      this.reason = '';
      await Promise.all([this.loadLog(1), this.loadSummary()]);
    } catch (err: unknown) {
      const message =
        err && typeof err === 'object' && 'error' in err
          ? (err as { error?: { detail?: string } }).error?.detail
          : null;
      this.errorMessage.set(message ?? 'تعذّر تسجيل الضيافة.');
    } finally {
      this.submitting.set(false);
    }
  }

  async onBranchChange(): Promise<void> {
    await Promise.all([this.loadLog(1), this.loadSummary()]);
  }

  async loadSummary(): Promise<void> {
    const year = Number(this.summaryYear);
    const month = Number(this.summaryMonthNumber);
    if (!this.selectedBranchId || !year || !month) {
      return;
    }

    this.loadingSummary.set(true);
    this.summaryError.set(null);
    try {
      this.summary.set(await this.summaryRequest.run(
        this.apiClient.get<ComplimentaryMonthlySummary>(ApiController.Inventory, InventoryOperation.ComplimentaryMonthlySummary, undefined, {
          branchId: this.selectedBranchId, year, month
        })
      ));
    } catch (err) {
      if (isRequestCancelled(err)) return;
      this.summaryError.set('تعذّر تحميل مجموع الضيافة الشهري.');
    } finally {
      this.loadingSummary.set(false);
    }
  }

  monthName(year: number, month: number): string {
    return `${ARABIC_MONTHS[month - 1]} ${year}`;
  }

  shortMonthName(month: number): string {
    return ARABIC_MONTHS[month - 1];
  }

  /** نسبة التغيّر عن الشهر اللي قبله - null لو الشهر اللي قبله صفر (ما في أساس للمقارنة). */
  changeVsPrevious(s: ComplimentaryMonthlySummary): number | null {
    return s.previousMonthCostValue > 0
      ? Math.round(((s.totalCostValue - s.previousMonthCostValue) / s.previousMonthCostValue) * 100)
      : null;
  }

  /** ارتفاع العمود كنسبة من أعلى شهر (أقل ارتفاع مرئي 2% عشان الشهر اللي فيه ضيافة ما يختفي). */
  barHeight(value: number, s: ComplimentaryMonthlySummary): number {
    const max = Math.max(...s.last12Months.map(m => m.costValue));
    if (max <= 0 || value <= 0) return 0;
    return Math.max(2, (value / max) * 100);
  }

  async selectTrendMonth(year: number, month: number): Promise<void> {
    this.summaryYear = year;
    this.summaryMonthNumber = month;
    if (!this.yearOptions.includes(year)) this.yearOptions = [year, ...this.yearOptions];
    await this.loadSummary();
  }

  /** عدد مرات ضيافة الشهر اللي قبل المختار (من الاتجاه) - للتمييز بين "ما في ضيافة" و"ضيافة بلا تكلفة معروفة". */
  previousMonthIssueCount(s: ComplimentaryMonthlySummary): number {
    return s.last12Months.length >= 2 ? s.last12Months[s.last12Months.length - 2].issueCount : 0;
  }

  async loadLog(page: number): Promise<void> {
    if (!this.selectedBranchId) {
      return;
    }

    this.loadingLog.set(true);
    this.logError.set(null);
    try {
      const result = await this.logRequest.run(
        this.apiClient.get<PagedResult<ComplimentaryLogItem>>(ApiController.Inventory, InventoryOperation.ComplimentaryLog, undefined, {
          branchId: this.selectedBranchId, pageNumber: page, pageSize: LOG_PAGE_SIZE
        })
      );
      this.log.set(result.items);
      this.logTotal.set(result.totalCount);
      this.logPage.set(page);
    } catch (err) {
      if (isRequestCancelled(err)) return;
      this.logError.set('تعذّر تحميل سجل الضيافة.');
    } finally {
      this.loadingLog.set(false);
    }
  }
}
