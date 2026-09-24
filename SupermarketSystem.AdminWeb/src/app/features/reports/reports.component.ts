import { Component, OnInit, signal, computed, inject } from '@angular/core';
import { CommonModule } from '@angular/common';
import { FormsModule } from '@angular/forms';
import { firstValueFrom } from 'rxjs';
import { ApiClient } from '../../core/api/api-client.service';
import { ApiController } from '../../core/api/api-controller.enum';
import { ReportsOperation, PurchaseInvoicesOperation, SalesOperation, BranchesOperation, ProductsOperation } from '../../core/api/operations';
import { PaginationComponent } from '../../shared/components/pagination/pagination.component';
import { REPORT_CONFIGS, ReportConfig } from './report-configs';
import { AuthService } from '../../core/services/auth.service';

interface PagedResult<T> {
  items: T[];
  totalCount: number;
}

interface SalesSummaryPeriodDto {
  fromUtc: string;
  toUtc: string;
  invoiceCount: number;
  totalSales: number;
  totalDiscounts: number;
  totalReturnedAmount: number;
  netRevenue: number;
}

interface GetSalesSummaryResponse {
  period: SalesSummaryPeriodDto;
  comparisonPeriod: SalesSummaryPeriodDto | null;
  netRevenueChangePercent: number | null;
}

interface CapitalValueItemDto {
  productId: string;
  productName: string;
  quantityOnHand: number;
  weightedAverageCost: number;
  totalValue: number;
}

interface GetCurrentCapitalValueResponse {
  items: PagedResult<CapitalValueItemDto>;
  totalCapitalValue: number;
  productsExcludedNoCostHistory: number;
}

interface ProductMarginItemDto {
  productId: string;
  productName: string;
  quantitySold: number;
  netRevenue: number;
  cost: number;
  margin: number;
  marginPercent: number | null;
  linesExcludedNoCostHistory: number;
}

interface GetProductMarginReportResponse {
  items: PagedResult<ProductMarginItemDto>;
  totalNetRevenue: number;
  totalCost: number;
  totalMargin: number;
}

interface SupplierDebtDto {
  supplierId: string;
  supplierName: string;
  totalInvoiced: number;
  totalPaid: number;
  remainingDebt: number;
  unpaidInvoiceCount: number;
}

interface GetSupplierDebtsResponse {
  suppliers: SupplierDebtDto[];
  grandTotalDebt: number;
}

interface CustomerDebtDto {
  customerId: string;
  customerName: string;
  totalInvoiced: number;
  totalPaid: number;
  remainingDebt: number;
  unpaidInvoiceCount: number;
}

interface GetCustomerDebtsResponse {
  customers: CustomerDebtDto[];
  grandTotalDebt: number;
}

type SpecialReportId = 'sales-summary' | 'capital-value' | 'supplier-debts' | 'product-margin' | 'customer-debts';

/**
 * كل تقرير عادي بيرندر من REPORT_CONFIGS بلا أي كود خاص. الخمسة الخاصة
 * (ملخّص المبيعات، رأس المال، ديون الموردين، هامش الربح لكل منتج، ديون
 * الزبائن) شكلهم مختلف كليًا، فمعالجان بمنطق منفصل بنفس المكوّن.
 */
@Component({
  selector: 'app-reports',
  standalone: true,
  imports: [CommonModule, FormsModule, PaginationComponent],
  templateUrl: './reports.component.html',
  styleUrl: './reports.component.css'
})
export class ReportsComponent implements OnInit {
  private readonly auth = inject(AuthService);

  readonly standardReports = REPORT_CONFIGS;
  readonly activeReportId = signal<string>(REPORT_CONFIGS[0].id);
  private readonly SPECIAL_IDS: SpecialReportId[] = ['sales-summary', 'capital-value', 'supplier-debts', 'product-margin', 'customer-debts'];
  readonly isSpecial = computed(() => this.SPECIAL_IDS.includes(this.activeReportId() as SpecialReportId));

  readonly loading = signal(false);
  readonly errorMessage = signal<string | null>(null);

  readonly rows = signal<Record<string, unknown>[]>([]);
  readonly totalCount = signal(0);
  readonly pageNumber = signal(1);
  readonly pageSize = signal(20);

  fromDate = this.defaultFromDate();
  toDate = this.defaultToDate();

  readonly branches = signal<{ id: string; name: string }[]>([]);
  selectedBranchId = '';

  /** لتقرير مقارنة أسعار الموردين - كان بلا أي طريقة تختار منتج، فكان دايمًا يفشل. */
  readonly products = signal<{ id: string; name: string }[]>([]);
  selectedProductId = '';

  readonly salesSummary = signal<GetSalesSummaryResponse | null>(null);
  readonly capitalValue = signal<GetCurrentCapitalValueResponse | null>(null);
  readonly supplierDebts = signal<GetSupplierDebtsResponse | null>(null);
  readonly productMargin = signal<GetProductMarginReportResponse | null>(null);
  readonly customerDebts = signal<GetCustomerDebtsResponse | null>(null);

  constructor(private readonly apiClient: ApiClient) {}

  ngOnInit(): void {
    this.loadBranches();
    this.loadActiveReport();
  }

  private async loadBranches(): Promise<void> {
    try {
      const result = await firstValueFrom(
        this.apiClient.get<{ items: { id: string; name: string }[] }>(ApiController.Branches, BranchesOperation.List, undefined, { pageSize: 500 })
      );
      this.branches.set(result.items);
      if (result.items.length > 0) {
        this.selectedBranchId = this.auth.defaultBranchId(result.items);
      }
    } catch {
      /* فشل تحميل الفروع - التقارير اللي تحتاج فرع بترجّع رسالة خطأ واضحة عند التحميل. */
    }
  }

  get activeConfig(): ReportConfig | undefined {
    return this.standardReports.find(r => r.id === this.activeReportId());
  }

  selectReport(id: string): void {
    this.activeReportId.set(id);
    this.pageNumber.set(1);
    this.loadActiveReport();
  }

  async loadActiveReport(): Promise<void> {
    const id = this.activeReportId();

    if (id === 'sales-summary') return this.loadSalesSummary();
    if (id === 'capital-value') return this.loadCapitalValue();
    if (id === 'supplier-debts') return this.loadSupplierDebts();
    if (id === 'product-margin') return this.loadProductMargin();
    if (id === 'customer-debts') return this.loadCustomerDebts();
    return this.loadStandardReport();
  }

  private async loadStandardReport(): Promise<void> {
    const config = this.activeConfig;
    if (!config) return;

    this.loading.set(true);
    this.errorMessage.set(null);

    try {
      const queryParams: Record<string, string | number> = {
        pageNumber: this.pageNumber(),
        pageSize: this.pageSize()
      };
      if (config.requiresDateRange) {
        queryParams['fromUtc'] = this.rangeStartUtc();
        queryParams['toUtc'] = this.rangeEndUtc();
      }
      if (config.requiresProduct) {
        if (!this.selectedProductId) {
          await this.ensureProductsLoaded();
          this.errorMessage.set('اختر منتجًا لعرض مقارنة أسعار الموردين.');
          this.rows.set([]);
          this.totalCount.set(0);
          this.loading.set(false);
          return;
        }
        queryParams['productId'] = this.selectedProductId;
      }
      if (config.requiresBranch) {
        if (!this.selectedBranchId) {
          this.errorMessage.set('هذا التقرير يحتاج تحديد فرع أولًا.');
          this.rows.set([]);
          this.totalCount.set(0);
          this.loading.set(false);
          return;
        }
        queryParams['branchId'] = this.selectedBranchId;
      }

      const result = await firstValueFrom(
        this.apiClient.get<PagedResult<Record<string, unknown>>>(
          ApiController.Reports, config.operation, undefined, queryParams
        )
      );

      this.rows.set(result.items);
      this.totalCount.set(result.totalCount);
    } catch {
      this.errorMessage.set('تعذّر تحميل التقرير.');
      this.rows.set([]);
      this.totalCount.set(0);
    } finally {
      this.loading.set(false);
    }
  }

  private async loadSalesSummary(): Promise<void> {
    this.loading.set(true);
    this.errorMessage.set(null);

    try {
      const result = await firstValueFrom(
        this.apiClient.get<GetSalesSummaryResponse>(ApiController.Reports, ReportsOperation.SalesSummary, undefined, {
          fromUtc: this.rangeStartUtc(),
          toUtc: this.rangeEndUtc()
        })
      );
      this.salesSummary.set(result);
    } catch {
      this.errorMessage.set('تعذّر تحميل ملخّص المبيعات.');
      this.salesSummary.set(null);
    } finally {
      this.loading.set(false);
    }
  }

  private async loadCapitalValue(): Promise<void> {
    this.loading.set(true);
    this.errorMessage.set(null);

    try {
      const result = await firstValueFrom(
        this.apiClient.get<GetCurrentCapitalValueResponse>(
          ApiController.Reports, ReportsOperation.CurrentCapitalValue, undefined,
          { pageNumber: this.pageNumber(), pageSize: this.pageSize() }
        )
      );
      this.capitalValue.set(result);
    } catch {
      this.errorMessage.set('تعذّر تحميل تقرير رأس المال.');
      this.capitalValue.set(null);
    } finally {
      this.loading.set(false);
    }
  }

  private async loadProductMargin(): Promise<void> {
    if (!this.selectedBranchId) {
      this.errorMessage.set('هذا التقرير يحتاج تحديد فرع أولًا.');
      this.productMargin.set(null);
      return;
    }

    this.loading.set(true);
    this.errorMessage.set(null);

    try {
      const result = await firstValueFrom(
        this.apiClient.get<GetProductMarginReportResponse>(
          ApiController.Reports, ReportsOperation.ProductMargin, undefined,
          {
            pageNumber: this.pageNumber(),
            pageSize: this.pageSize(),
            branchId: this.selectedBranchId,
            fromUtc: this.rangeStartUtc(),
            toUtc: this.rangeEndUtc()
          }
        )
      );
      this.productMargin.set(result);
    } catch {
      this.errorMessage.set('تعذّر تحميل تقرير هامش الربح لكل منتج.');
      this.productMargin.set(null);
    } finally {
      this.loading.set(false);
    }
  }

  private async loadSupplierDebts(): Promise<void> {
    this.loading.set(true);
    this.errorMessage.set(null);

    try {
      const result = await firstValueFrom(
        this.apiClient.get<GetSupplierDebtsResponse>(
          ApiController.PurchaseInvoices, PurchaseInvoicesOperation.SupplierDebts
        )
      );
      this.supplierDebts.set(result);
    } catch {
      this.errorMessage.set('تعذّر تحميل تقرير ديون الموردين.');
      this.supplierDebts.set(null);
    } finally {
      this.loading.set(false);
    }
  }

  private async loadCustomerDebts(): Promise<void> {
    this.loading.set(true);
    this.errorMessage.set(null);

    try {
      const result = await firstValueFrom(
        this.apiClient.get<GetCustomerDebtsResponse>(
          ApiController.Sales, SalesOperation.CustomerDebts
        )
      );
      this.customerDebts.set(result);
    } catch {
      this.errorMessage.set('تعذّر تحميل تقرير ديون الزبائن.');
      this.customerDebts.set(null);
    } finally {
      this.loading.set(false);
    }
  }

  onPageChanged(event: { pageNumber: number; pageSize: number }): void {
    this.pageNumber.set(event.pageNumber);
    this.pageSize.set(event.pageSize);
    this.loadActiveReport();
  }

  onDateRangeChanged(): void {
    this.pageNumber.set(1);
    this.loadActiveReport();
  }

  onBranchChanged(): void {
    this.pageNumber.set(1);
    this.loadActiveReport();
  }

  onProductChanged(): void {
    this.pageNumber.set(1);
    this.loadActiveReport();
  }

  private async ensureProductsLoaded(): Promise<void> {
    if (this.products().length > 0) return;
    try {
      const result = await firstValueFrom(
        this.apiClient.get<{ items: { id: string; name: string }[] }>(ApiController.Products, ProductsOperation.List, undefined, { pageSize: 500 })
      );
      this.products.set(result.items);
    } catch {
      /* فشل تحميل المنتجات - رسالة "اختر منتجًا" بتضل ظاهرة. */
    }
  }

  formatCell(value: unknown, column: { type: string; enumMap?: Record<string, string> }): string {
    if (value === null || value === undefined) return '—';

    if (column.type === 'boolean') {
      return value ? 'نعم' : 'لا';
    }
    if (column.type === 'enum' && column.enumMap) {
      return column.enumMap[String(value)] ?? String(value);
    }
    if (column.type === 'currency') {
      return Number(value).toLocaleString('en-US', { minimumFractionDigits: 2, maximumFractionDigits: 2 });
    }
    if (column.type === 'number') {
      return Number(value).toLocaleString('en-US');
    }
    if (column.type === 'date') {
      return new Date(value as string).toLocaleString('en-GB', { year: 'numeric', month: '2-digit', day: '2-digit', hour: '2-digit', minute: '2-digit' });
    }
    return String(value);
  }

  /**
   * "من"/"إلى" أيام تقويم محلية (توقيت الجهاز)، والفترة بتشمل يوم "إلى" كامل. كانت
   * new Date('yyyy-mm-dd') = منتصف ليل UTC بداية اليوم، فالافتراضي (إلى = اليوم) كان
   * يقطع كل عمليات اليوم - إرجاع أو إلغاء صار الصبح ما كان يبين بالتقارير لحد بكرة.
   */
  rangeStartUtc(): string {
    return this.parseLocalDate(this.fromDate).toISOString();
  }

  rangeEndUtc(): string {
    const end = this.parseLocalDate(this.toDate);
    end.setDate(end.getDate() + 1);
    end.setMilliseconds(end.getMilliseconds() - 1);
    return end.toISOString();
  }

  private parseLocalDate(value: string): Date {
    const [year, month, day] = value.split('-').map(Number);
    return new Date(year, month - 1, day);
  }

  private formatLocalDate(date: Date): string {
    const pad = (n: number) => String(n).padStart(2, '0');
    return `${date.getFullYear()}-${pad(date.getMonth() + 1)}-${pad(date.getDate())}`;
  }

  private defaultFromDate(): string {
    const d = new Date();
    d.setDate(d.getDate() - 30);
    return this.formatLocalDate(d);
  }

  private defaultToDate(): string {
    return this.formatLocalDate(new Date());
  }
}
