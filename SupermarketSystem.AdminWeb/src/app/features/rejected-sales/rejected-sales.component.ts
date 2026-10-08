import { Component, OnInit, signal } from '@angular/core';
import { CommonModule } from '@angular/common';
import { firstValueFrom } from 'rxjs';
import { ApiClient } from '../../core/api/api-client.service';
import { isRequestCancelled, latestRequest } from '../../core/api/latest-request';
import { ApiController } from '../../core/api/api-controller.enum';
import { SalesOperation } from '../../core/api/operations';

interface RejectedSaleDto {
  id: string;
  clientRequestId: string;
  branchId: string;
  branchName: string;
  cashierName: string | null;
  errorCode: string;
  errorMessage: string;
  paidAmountHint: number;
  itemCount: number;
  attemptCount: number;
  firstAttemptAtUtc: string;
  lastAttemptAtUtc: string;
  isResolved: boolean;
  resolvedAtUtc: string | null;
  resolvedByName: string | null;
  resolutionNote: string | null;
  resolvedAutomatically: boolean;
}

interface RejectedSaleLineDto {
  productName: string;
  unitName: string | null;
  quantity: number;
  manualDiscountAmount: number;
}

interface RejectedSalePaymentDto {
  paymentMethodName: string;
  amount: number;
}

interface RejectedSaleDetailsDto {
  summary: RejectedSaleDto;
  invoiceLevelDiscountAmount: number;
  allowCreditSale: boolean;
  customerPhone: string | null;
  lines: RejectedSaleLineDto[];
  payments: RejectedSalePaymentDto[];
}

interface PagedResult<T> {
  items: T[];
  totalCount: number;
  pageNumber: number;
  pageSize: number;
}

/**
 * بيعات رفضها السيرفر نهائيًا (بند 24): الكاشير بيحفظ البيعة محليًا ويعيد إرسالها، ولو انرفضت نهائيًا كانت
 * بتضل عالقة بطابوره والإدارة ما بتعرف (مصاري بالدرج بلا فاتورة وبلا خصم مخزون). هون بتبين مع السبب والمحتوى،
 * و"تمت المعالجة" علامة + ملاحظة بلا أي أثر مالي (الإدخال اليدوي للبيعة بيتم من صفحة المبيعات/المخزون حسب الحالة).
 */
@Component({
  selector: 'app-rejected-sales',
  standalone: true,
  imports: [CommonModule],
  templateUrl: './rejected-sales.component.html',
  styleUrl: './rejected-sales.component.css'
})
export class RejectedSalesComponent implements OnInit {
  private readonly listRequest = latestRequest();
  private readonly detailsRequest = latestRequest();

  readonly items = signal<RejectedSaleDto[]>([]);
  readonly totalCount = signal(0);
  readonly includeResolved = signal(false);
  readonly loading = signal(true);
  readonly errorMessage = signal<string | null>(null);

  readonly expandedId = signal<string | null>(null);
  readonly details = signal<RejectedSaleDetailsDto | null>(null);
  readonly detailsLoading = signal(false);

  readonly resolvingId = signal<string | null>(null);
  readonly resolveNote = signal('');
  readonly processingId = signal<string | null>(null);

  constructor(private readonly apiClient: ApiClient) {}

  ngOnInit(): void {
    this.load();
  }

  async load(): Promise<void> {
    this.loading.set(true);
    this.errorMessage.set(null);
    try {
      const result = await this.listRequest.run(
        this.apiClient.get<PagedResult<RejectedSaleDto>>(ApiController.Sales, SalesOperation.RejectedList, undefined, {
          pageSize: 100,
          includeResolved: this.includeResolved()
        })
      );
      this.items.set(result.items);
      this.totalCount.set(result.totalCount);
    } catch (err) {
      if (isRequestCancelled(err)) return;
      this.errorMessage.set('تعذّر تحميل البيعات المرفوضة.');
    } finally {
      this.loading.set(false);
    }
  }

  toggleIncludeResolved(): void {
    this.includeResolved.set(!this.includeResolved());
    this.load();
  }

  async toggleDetails(item: RejectedSaleDto): Promise<void> {
    if (this.expandedId() === item.id) {
      this.expandedId.set(null);
      this.details.set(null);
      this.detailsRequest.cancel();
      return;
    }

    this.expandedId.set(item.id);
    this.details.set(null);
    this.detailsLoading.set(true);
    try {
      const result = await this.detailsRequest.run(
        this.apiClient.get<RejectedSaleDetailsDto>(ApiController.Sales, SalesOperation.RejectedById, { id: item.id })
      );
      if (this.expandedId() === item.id) this.details.set(result);
    } catch (err) {
      if (isRequestCancelled(err)) return;
      this.errorMessage.set('تعذّر تحميل تفاصيل البيعة.');
    } finally {
      this.detailsLoading.set(false);
    }
  }

  startResolve(item: RejectedSaleDto): void {
    this.resolvingId.set(item.id);
    this.resolveNote.set('');
  }

  cancelResolve(): void {
    this.resolvingId.set(null);
  }

  onNoteInput(event: Event): void {
    this.resolveNote.set((event.target as HTMLInputElement).value);
  }

  async confirmResolve(item: RejectedSaleDto): Promise<void> {
    this.processingId.set(item.id);
    this.errorMessage.set(null);
    try {
      await firstValueFrom(
        this.apiClient.post(ApiController.Sales, SalesOperation.ResolveRejected, { note: this.resolveNote().trim() || null }, { id: item.id })
      );
      this.resolvingId.set(null);
      await this.load();
    } catch {
      this.errorMessage.set('تعذّر تعليم البيعة كمعالَجة.');
    } finally {
      this.processingId.set(null);
    }
  }
}
