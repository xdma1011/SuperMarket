import { Component, OnInit, signal } from '@angular/core';
import { CommonModule } from '@angular/common';
import { FormsModule } from '@angular/forms';
import { Router } from '@angular/router';
import { firstValueFrom } from 'rxjs';
import { ApiClient } from '../../core/api/api-client.service';
import { ApiController } from '../../core/api/api-controller.enum';
import { StocktakesOperation, BranchesOperation } from '../../core/api/operations';
import { PaginationComponent } from '../../shared/components/pagination/pagination.component';

interface StocktakeListItemDto {
  stocktakeId: string;
  stocktakeNumber: string;
  branchId: string;
  branchName: string;
  statusCode: number;
  statusTitle: string;
  itemCount: number;
  createdAtUtc: string;
  completedAtUtc: string | null;
  approvedAtUtc: string | null;
}

interface BranchDto {
  id: string;
  name: string;
}

interface PagedResult<T> {
  items: T[];
  totalCount: number;
}

interface CreateStocktakeResponse {
  stocktakeId: string;
  stocktakeNumber: string;
  itemCount: number;
}

/**
 * كانت الصفحة كلها ناقصة — الباك إند جاهز من جلسة سابقة بمراحله
 * الأربعة، بس صفر واجهة تستخدمه.
 */
@Component({
  selector: 'app-stocktakes',
  standalone: true,
  imports: [CommonModule, FormsModule, PaginationComponent],
  templateUrl: './stocktakes.component.html',
  styleUrl: './stocktakes.component.css'
})
export class StocktakesComponent implements OnInit {
  readonly stocktakes = signal<StocktakeListItemDto[]>([]);
  readonly totalCount = signal(0);
  readonly pageNumber = signal(1);
  readonly pageSize = signal(20);

  readonly branches = signal<BranchDto[]>([]);
  readonly loading = signal(true);
  readonly errorMessage = signal<string | null>(null);

  readonly formOpen = signal(false);
  readonly submitting = signal(false);
  readonly formError = signal<string | null>(null);
  selectedBranchId = '';

  constructor(
    private readonly apiClient: ApiClient,
    private readonly router: Router
  ) {}

  ngOnInit(): void {
    this.loadBranches();
    this.load();
  }

  private async loadBranches(): Promise<void> {
    try {
      const result = await firstValueFrom(
        this.apiClient.get<PagedResult<BranchDto>>(ApiController.Branches, BranchesOperation.List, undefined, { pageSize: 500 })
      );
      this.branches.set(result.items);
      if (result.items.length > 0) {
        this.selectedBranchId = result.items[0].id;
      }
    } catch {
      /* فشل تحميل الفروع لا يمنع عرض القائمة. */
    }
  }

  async load(): Promise<void> {
    this.loading.set(true);
    this.errorMessage.set(null);

    try {
      const result = await firstValueFrom(
        this.apiClient.get<PagedResult<StocktakeListItemDto>>(ApiController.Stocktakes, StocktakesOperation.List, undefined, {
          pageNumber: this.pageNumber(),
          pageSize: this.pageSize()
        })
      );
      this.stocktakes.set(result.items);
      this.totalCount.set(result.totalCount);
    } catch {
      this.errorMessage.set('تعذّر تحميل عمليات الجرد.');
    } finally {
      this.loading.set(false);
    }
  }

  onPageChanged(event: { pageNumber: number; pageSize: number }): void {
    this.pageNumber.set(event.pageNumber);
    this.pageSize.set(event.pageSize);
    this.load();
  }

  openCreateForm(): void {
    this.formOpen.set(true);
    this.formError.set(null);
  }

  closeForm(): void {
    this.formOpen.set(false);
  }

  /** بدون اختيار أصناف حاليًا عمدًا - "الفرع كامل" بس، أبسط نقطة انطلاق تغطي الاستخدام الأشيع. */
  async submitCreate(): Promise<void> {
    if (!this.selectedBranchId) {
      this.formError.set('اختر فرع.');
      return;
    }

    this.submitting.set(true);
    this.formError.set(null);

    try {
      const response = await firstValueFrom(
        this.apiClient.post<CreateStocktakeResponse>(ApiController.Stocktakes, StocktakesOperation.Create, {
          branchId: this.selectedBranchId,
          includeAllProductsAtBranch: true,
          productIds: null
        })
      );

      this.closeForm();
      await this.router.navigate(['/stocktakes', response.stocktakeId]);
    } catch (err: unknown) {
      const message =
        err && typeof err === 'object' && 'error' in err
          ? (err as { error?: { detail?: string } }).error?.detail
          : null;
      this.formError.set(message ?? 'تعذّر إنشاء الجرد.');
    } finally {
      this.submitting.set(false);
    }
  }

  openStocktake(stocktakeId: string): void {
    this.router.navigate(['/stocktakes', stocktakeId]);
  }

  statusTone(statusCode: number): 'green' | 'accent' | 'red' {
    if (statusCode === 4) return 'green';
    if (statusCode === 5) return 'red';
    return 'accent';
  }
}
