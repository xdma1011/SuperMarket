import { Component, OnInit, signal } from '@angular/core';
import { CommonModule } from '@angular/common';
import { firstValueFrom } from 'rxjs';
import { ApiClient } from '../../core/api/api-client.service';
import { ApiController } from '../../core/api/api-controller.enum';
import { ReviewsOperation, ReturnsOperation } from '../../core/api/operations';

type PendingReviewType = 1 | 2 | 3 | 4;

interface PendingReviewItemDto {
  type: PendingReviewType;
  typeTitle: string;
  referenceId: string;
  title: string;
  detail: string;
  amount: number | null;
  branchId: string;
  occurredAtUtc: string;
}

interface VoidedSaleReviewDto {
  saleInvoiceId: string;
  invoiceNumber: string;
  voidReasonTitle: string;
  voidNotes: string | null;
  amount: number;
  voidedAtUtc: string;
  voidedByName: string;
  branchId: string;
}

interface GetPendingReviewsResponse {
  items: PendingReviewItemDto[];
  totalCount: number;
  voidedSales: VoidedSaleReviewDto[];
}

/**
 * صفحة واحدة تجمع كل شي بانتظار مراجعة إدارية — إرجاعات (D8)، تعديل
 * مخزون يدوي/ضيافة (AllowWithReview)، ارتفاع سعر شراء، شكاوى، وقسم
 * مستقل للمبيعات الملغاة (VoidSale) - كانت غايبة كليًا عن هالصفحة قبل
 * هالتعديل. كل نوع إله زر "تمت المراجعة" بيستدعي endpoint مختلف بحسب
 * Type، بس الواجهة موحّدة بصريًا لأنواع Items؛ VoidedSales قسم منفصل
 * بحقوله الخاصة (سبب الإلغاء ومنفّذه) - راجع تقرير التسليم.
 */
@Component({
  selector: 'app-reviews',
  standalone: true,
  imports: [CommonModule],
  templateUrl: './reviews.component.html',
  styleUrl: './reviews.component.css'
})
export class ReviewsComponent implements OnInit {
  readonly items = signal<PendingReviewItemDto[]>([]);
  readonly voidedSales = signal<VoidedSaleReviewDto[]>([]);
  readonly loading = signal(true);
  readonly errorMessage = signal<string | null>(null);
  readonly processingId = signal<string | null>(null);

  constructor(private readonly apiClient: ApiClient) {}

  ngOnInit(): void {
    this.load();
  }

  async load(): Promise<void> {
    this.loading.set(true);
    this.errorMessage.set(null);

    try {
      const result = await firstValueFrom(
        this.apiClient.get<GetPendingReviewsResponse>(ApiController.Reviews, ReviewsOperation.List)
      );
      this.items.set(result.items);
      this.voidedSales.set(result.voidedSales);
    } catch {
      this.errorMessage.set('تعذّر تحميل قائمة المراجعات.');
    } finally {
      this.loading.set(false);
    }
  }

  async markSaleInvoiceReviewed(voidedSale: VoidedSaleReviewDto): Promise<void> {
    this.processingId.set(voidedSale.saleInvoiceId);
    this.errorMessage.set(null);

    try {
      await firstValueFrom(
        this.apiClient.post(
          ApiController.Reviews,
          ReviewsOperation.MarkSaleInvoiceReviewed,
          {},
          { saleInvoiceId: voidedSale.saleInvoiceId }
        )
      );
      this.voidedSales.set(this.voidedSales().filter(v => v.saleInvoiceId !== voidedSale.saleInvoiceId));
    } catch {
      this.errorMessage.set(`تعذّر تعليم فاتورة "${voidedSale.invoiceNumber}" كمُراجَعة.`);
    } finally {
      this.processingId.set(null);
    }
  }

  isReturn(item: PendingReviewItemDto): boolean {
    return item.type === 1;
  }

  async markReviewed(item: PendingReviewItemDto): Promise<void> {
    this.processingId.set(item.referenceId);
    this.errorMessage.set(null);

    try {
      switch (item.type) {
        case 1:
          await firstValueFrom(
            this.apiClient.post(ApiController.Returns, ReturnsOperation.MarkReviewed, {}, { id: item.referenceId })
          );
          break;
        case 2:
          await firstValueFrom(
            this.apiClient.post(
              ApiController.Reviews,
              ReviewsOperation.MarkStockMovementReviewed,
              {},
              { stockMovementId: item.referenceId }
            )
          );
          break;
        case 3:
          await firstValueFrom(
            this.apiClient.post(
              ApiController.Reviews,
              ReviewsOperation.MarkPurchaseInvoiceItemReviewed,
              {},
              { purchaseInvoiceItemId: item.referenceId }
            )
          );
          break;
        case 4:
          await firstValueFrom(
            this.apiClient.post(ApiController.Reviews, ReviewsOperation.MarkComplaintReviewed, {}, { complaintId: item.referenceId })
          );
          break;
      }

      this.items.set(this.items().filter(i => i.referenceId !== item.referenceId));
    } catch {
      this.errorMessage.set(`تعذّر تعليم "${item.title}" كمُراجَعة.`);
    } finally {
      this.processingId.set(null);
    }
  }
}
