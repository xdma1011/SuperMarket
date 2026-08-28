import { Component, OnInit, signal } from '@angular/core';
import { CommonModule } from '@angular/common';
import { FormsModule } from '@angular/forms';
import { ActivatedRoute, Router } from '@angular/router';
import { firstValueFrom } from 'rxjs';
import { ApiClient } from '../../core/api/api-client.service';
import { ApiController } from '../../core/api/api-controller.enum';
import { StocktakesOperation } from '../../core/api/operations';
import { PermissionsService } from '../../core/services/permissions.service';

interface StocktakeItemDetailDto {
  stocktakeItemId: string;
  productId: string;
  productName: string;
  productBatchId: string | null;
  expectedQuantity: number;
  countedQuantity: number | null;
  variance: number | null;
  countedByUserId: string | null;
  countedAtUtc: string | null;
}

interface StocktakeDetailResponse {
  stocktakeId: string;
  stocktakeNumber: string;
  branchId: string;
  status: number;
  completedAtUtc: string | null;
  approvedAtUtc: string | null;
  items: StocktakeItemDetailDto[];
}

interface ApproveStocktakeResponse {
  stocktakeId: string;
  stocktakeNumber: string;
  appliedCorrections: { productId: string; variance: number; wentNegative: boolean }[];
}

const STATUS_DRAFT = 1;
const STATUS_IN_PROGRESS = 2;
const STATUS_COMPLETED = 3;
const STATUS_APPROVED = 4;
const STATUS_CANCELLED = 5;

/**
 * شاشة واحدة تغطي المراحل الأربعة كلها، بحالة واحدة تفاعلية بدل صفحات
 * منفصلة لكل مرحلة — الحالة الفعلية من الباك إند هي اللي تقرر شو
 * يظهر: عدّ تفاعلي (InProgress)، مراجعة فروقات + زر اعتماد (Completed)،
 * أو عرض نهائي بلا تعديل (Approved).
 */
@Component({
  selector: 'app-stocktake-detail',
  standalone: true,
  imports: [CommonModule, FormsModule],
  templateUrl: './stocktake-detail.component.html',
  styleUrl: './stocktake-detail.component.css'
})
export class StocktakeDetailComponent implements OnInit {
  readonly stocktake = signal<StocktakeDetailResponse | null>(null);
  readonly loading = signal(true);
  readonly errorMessage = signal<string | null>(null);

  readonly savingItemId = signal<string | null>(null);
  readonly completing = signal(false);
  readonly approving = signal(false);

  private stocktakeId = '';

  readonly STATUS_DRAFT = STATUS_DRAFT;
  readonly STATUS_IN_PROGRESS = STATUS_IN_PROGRESS;
  readonly STATUS_COMPLETED = STATUS_COMPLETED;
  readonly STATUS_APPROVED = STATUS_APPROVED;
  readonly STATUS_CANCELLED = STATUS_CANCELLED;

  constructor(
    private readonly apiClient: ApiClient,
    private readonly route: ActivatedRoute,
    private readonly router: Router,
    readonly permissions: PermissionsService
  ) {}

  ngOnInit(): void {
    this.stocktakeId = this.route.snapshot.paramMap.get('id') ?? '';
    this.load();
  }

  async load(): Promise<void> {
    this.loading.set(true);
    this.errorMessage.set(null);

    try {
      const result = await firstValueFrom(
        this.apiClient.get<StocktakeDetailResponse>(ApiController.Stocktakes, StocktakesOperation.GetById, { id: this.stocktakeId })
      );
      this.stocktake.set(result);
    } catch {
      this.errorMessage.set('تعذّر تحميل الجرد - تأكد إنه موجود.');
    } finally {
      this.loading.set(false);
    }
  }

  canCount(): boolean {
    const s = this.stocktake();
    return s !== null && (s.status === STATUS_DRAFT || s.status === STATUS_IN_PROGRESS);
  }

  async saveCount(item: StocktakeItemDetailDto, value: number | null): Promise<void> {
    if (value === null || value < 0) {
      return;
    }

    this.savingItemId.set(item.stocktakeItemId);
    this.errorMessage.set(null);

    try {
      const result = await firstValueFrom(
        this.apiClient.post<{ expectedQuantity: number; countedQuantity: number; variance: number }>(
          ApiController.Stocktakes,
          StocktakesOperation.RecordCount,
          { countedQuantity: value },
          { id: this.stocktakeId, itemId: item.stocktakeItemId }
        )
      );

      item.countedQuantity = result.countedQuantity;
      item.variance = result.variance;
    } catch {
      this.errorMessage.set(`تعذّر حفظ عدّ "${item.productName}".`);
    } finally {
      this.savingItemId.set(null);
    }
  }

  get countedItemsCount(): number {
    return this.stocktake()?.items.filter(i => i.countedQuantity !== null).length ?? 0;
  }

  get totalItemsCount(): number {
    return this.stocktake()?.items.length ?? 0;
  }

  async completeStocktake(): Promise<void> {
    this.completing.set(true);
    this.errorMessage.set(null);

    try {
      await firstValueFrom(
        this.apiClient.post(ApiController.Stocktakes, StocktakesOperation.Complete, {}, { id: this.stocktakeId })
      );
      await this.load();
    } catch (err: unknown) {
      const message =
        err && typeof err === 'object' && 'error' in err
          ? (err as { error?: { detail?: string } }).error?.detail
          : null;
      this.errorMessage.set(message ?? 'تعذّر إكمال الجرد - تأكد إنه كل الأصناف معدودة.');
    } finally {
      this.completing.set(false);
    }
  }

  async approveStocktake(): Promise<void> {
    this.approving.set(true);
    this.errorMessage.set(null);

    try {
      await firstValueFrom(
        this.apiClient.post<ApproveStocktakeResponse>(ApiController.Stocktakes, StocktakesOperation.Approve, {}, { id: this.stocktakeId })
      );
      await this.load();
    } catch (err: unknown) {
      const message =
        err && typeof err === 'object' && 'error' in err
          ? (err as { error?: { detail?: string } }).error?.detail
          : null;
      this.errorMessage.set(message ?? 'تعذّر اعتماد الجرد.');
    } finally {
      this.approving.set(false);
    }
  }

  backToList(): void {
    this.router.navigate(['/stocktakes']);
  }

  varianceClass(variance: number | null): string {
    if (variance === null || variance === 0) return '';
    return variance > 0 ? 'variance-positive' : 'variance-negative';
  }
}
