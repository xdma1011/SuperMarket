import { Component, OnInit, signal } from '@angular/core';
import { CommonModule } from '@angular/common';
import { firstValueFrom } from 'rxjs';
import { ApiClient } from '../../core/api/api-client.service';
import { ApiController } from '../../core/api/api-controller.enum';
import { ReviewsOperation, ReturnsOperation } from '../../core/api/operations';

type PendingReviewType = 1 | 2 | 3;

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

interface GetPendingReviewsResponse {
  items: PendingReviewItemDto[];
  totalCount: number;
}

/**
 * صفحة واحدة تجمع كل شي بانتظار مراجعة إدارية — إرجاعات (D8) وضيافة
 * تجاوزت الحد اليومي (AllowWithReview). كل نوع إله زر "تمت المراجعة"
 * بيستدعي endpoint مختلف بحسب Type، بس الواجهة موحّدة بصريًا.
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
    } catch {
      this.errorMessage.set('تعذّر تحميل قائمة المراجعات.');
    } finally {
      this.loading.set(false);
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
      }

      this.items.set(this.items().filter(i => i.referenceId !== item.referenceId));
    } catch {
      this.errorMessage.set(`تعذّر تعليم "${item.title}" كمُراجَعة.`);
    } finally {
      this.processingId.set(null);
    }
  }
}
