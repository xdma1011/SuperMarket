import { Component, OnInit, signal } from '@angular/core';
import { CommonModule } from '@angular/common';
import { FormsModule } from '@angular/forms';
import { firstValueFrom } from 'rxjs';
import { ApiClient } from '../../core/api/api-client.service';
import { ApiController } from '../../core/api/api-controller.enum';
import { SalesOperation, ReportsOperation, PaymentMethodsOperation } from '../../core/api/operations';
import { PaginationComponent } from '../../shared/components/pagination/pagination.component';

interface SaleInvoiceListItemDto {
  id: string;
  invoiceNumber: string;
  statusCode: number;
  statusTitle: string;
  totalAmount: number;
  totalPaidAmount: number;
  totalReturnedAmount: number;
  createdAtUtc: string;
  customerName: string | null;
  customerPhone: string | null;
}

interface PaymentMethodDto {
  id: string;
  name: string;
}

interface PagedResult<T> {
  items: T[];
  totalCount: number;
}

interface SalesSummaryPeriodDto {
  invoiceCount: number;
  totalSales: number;
  totalDiscounts: number;
  totalReturnedAmount: number;
  netRevenue: number;
}

interface GetSalesSummaryResponse {
  period: SalesSummaryPeriodDto;
}

/**
 * أُعيد بناؤها بالكامل لتتصل بالباك إند الفعلي — كانت بيانات ثابتة
 * (mock) تعرض كاشير/عدد أصناف/طريقة دفع، حقول غير موجودة أصلًا بـ
 * SaleInvoiceListItemDto الحقيقي. عُرضت هون بس الحقول الحقيقية المتاحة؛
 * لو احتجنا لاحقًا عرض الكاشير/طريقة الدفع، هذا يعني توسيع الاستعلام
 * بالباك إند أول، لا اختلاق بيانات بالواجهة.
 */
@Component({
  selector: 'app-sales',
  standalone: true,
  imports: [CommonModule, FormsModule, PaginationComponent],
  templateUrl: './sales.component.html',
  styleUrl: './sales.component.css'
})
export class SalesComponent implements OnInit {
  readonly invoices = signal<SaleInvoiceListItemDto[]>([]);
  readonly totalCount = signal(0);
  readonly pageNumber = signal(1);
  readonly pageSize = signal(20);
  readonly searchQuery = signal('');

  readonly summary = signal<SalesSummaryPeriodDto | null>(null);
  readonly loading = signal(true);
  readonly errorMessage = signal<string | null>(null);

  readonly paymentMethods = signal<PaymentMethodDto[]>([]);
  readonly paymentModalOpen = signal(false);
  readonly paymentSubmitting = signal(false);
  readonly paymentError = signal<string | null>(null);
  paymentTargetInvoice: SaleInvoiceListItemDto | null = null;
  paymentAmount: number | null = null;
  paymentMethodId = '';

  constructor(private readonly apiClient: ApiClient) {}

  ngOnInit(): void {
    this.loadSummary();
    this.loadInvoices();
    this.loadPaymentMethods();
  }

  private async loadPaymentMethods(): Promise<void> {
    try {
      const result = await firstValueFrom(
        this.apiClient.get<PaymentMethodDto[]>(ApiController.PaymentMethods, PaymentMethodsOperation.List)
      );
      this.paymentMethods.set(result);
      if (result.length > 0) this.paymentMethodId = result[0].id;
    } catch {
      /* فشل تحميل طرق الدفع لا يمنع عرض جدول الفواتير - بس تسديد دين ما رح يشتغل. */
    }
  }

  private async loadSummary(): Promise<void> {
    try {
      const to = new Date();
      const from = new Date();
      from.setDate(from.getDate() - 30);

      const result = await firstValueFrom(
        this.apiClient.get<GetSalesSummaryResponse>(ApiController.Reports, ReportsOperation.SalesSummary, undefined, {
          fromUtc: from.toISOString(),
          toUtc: to.toISOString()
        })
      );
      this.summary.set(result.period);
    } catch {
      /* فشل تحميل الملخّص لا يمنع عرض جدول الفواتير. */
    }
  }

  async loadInvoices(): Promise<void> {
    this.loading.set(true);
    this.errorMessage.set(null);

    try {
      const result = await firstValueFrom(
        this.apiClient.get<PagedResult<SaleInvoiceListItemDto>>(ApiController.Sales, SalesOperation.List, undefined, {
          pageNumber: this.pageNumber(),
          pageSize: this.pageSize(),
          search: this.searchQuery() || undefined
        })
      );
      this.invoices.set(result.items);
      this.totalCount.set(result.totalCount);
    } catch {
      this.errorMessage.set('تعذّر تحميل الفواتير.');
    } finally {
      this.loading.set(false);
    }
  }

  onSearchChange(value: string): void {
    this.searchQuery.set(value);
    this.pageNumber.set(1);
    this.loadInvoices();
  }

  onPageChanged(event: { pageNumber: number; pageSize: number }): void {
    this.pageNumber.set(event.pageNumber);
    this.pageSize.set(event.pageSize);
    this.loadInvoices();
  }

  statusTone(statusCode: number): 'green' | 'accent' | 'red' {
    // 1=Completed, 2=Voided, 3=PartiallyReturned, 4=FullyReturned
    if (statusCode === 2) return 'red';
    if (statusCode === 3 || statusCode === 4) return 'accent';
    return 'green';
  }

  remainingDebt(invoice: SaleInvoiceListItemDto): number {
    return invoice.totalAmount - invoice.totalPaidAmount;
  }

  openPaymentModal(invoice: SaleInvoiceListItemDto): void {
    this.paymentTargetInvoice = invoice;
    this.paymentAmount = this.remainingDebt(invoice);
    this.paymentError.set(null);
    this.paymentModalOpen.set(true);
  }

  closePaymentModal(): void {
    this.paymentModalOpen.set(false);
    this.paymentTargetInvoice = null;
    this.paymentAmount = null;
  }

  async submitPayment(): Promise<void> {
    const invoice = this.paymentTargetInvoice;
    if (!invoice || !this.paymentAmount || this.paymentAmount <= 0 || !this.paymentMethodId) {
      this.paymentError.set('حدّد مبلغًا موجبًا وطريقة دفع.');
      return;
    }

    if (this.paymentAmount > this.remainingDebt(invoice)) {
      this.paymentError.set('المبلغ أكبر من الدين المتبقي على هذه الفاتورة.');
      return;
    }

    this.paymentSubmitting.set(true);
    this.paymentError.set(null);

    try {
      await firstValueFrom(
        this.apiClient.post(
          ApiController.Sales,
          SalesOperation.RecordPayment,
          {
            paymentMethodId: this.paymentMethodId,
            amount: this.paymentAmount,
            externalReference: null,
            clientRequestId: crypto.randomUUID()
          },
          { id: invoice.id }
        )
      );

      this.closePaymentModal();
      await this.loadInvoices();
    } catch (err: unknown) {
      const message =
        err && typeof err === 'object' && 'error' in err
          ? (err as { error?: { detail?: string } }).error?.detail
          : null;
      this.paymentError.set(message ?? 'تعذّر تسجيل الدفعة.');
    } finally {
      this.paymentSubmitting.set(false);
    }
  }
}
