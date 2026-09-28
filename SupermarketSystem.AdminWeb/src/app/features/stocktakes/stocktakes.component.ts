import { Component, OnInit, signal, inject } from '@angular/core';
import { CommonModule } from '@angular/common';
import { FormsModule } from '@angular/forms';
import { Router } from '@angular/router';
import { firstValueFrom } from 'rxjs';
import { ApiClient } from '../../core/api/api-client.service';
import { ApiController } from '../../core/api/api-controller.enum';
import { StocktakesOperation, BranchesOperation, ProductsOperation } from '../../core/api/operations';
import { PaginationComponent } from '../../shared/components/pagination/pagination.component';
import { AuthService } from '../../core/services/auth.service';

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

/** مادة إلها إرجاع بعد آخر مرة انعدّت (ضد الإرجاع الوهمي) - GET /stocktakes/returned-pending. */
export interface ReturnedPendingItemDto {
  branchId: string;
  branchName: string;
  productId: string;
  productName: string;
  quantityReturnedBase: number;
  returnCount: number;
  lastReturnAtUtc: string;
  lastCountedAtUtc: string | null;
  returns: { returnInvoiceNumber: string; originalSaleInvoiceNumber: string; returnedAtUtc: string; quantityBase: number; cashierName: string }[];
}

interface ProductOption {
  id: string;
  name: string;
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
  private readonly auth = inject(AuthService);

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

  /** نطاق الجرد الجديد: الفرع كامل (الشهري) أو مواد مختارة (الجزئي السريع). */
  readonly scope = signal<'all' | 'selected'>('all');
  readonly selectedProducts = signal<ProductOption[]>([]);
  readonly productResults = signal<ProductOption[]>([]);
  productSearch = '';
  private productSearchHandle: ReturnType<typeof setTimeout> | null = null;

  /** مواد مرتجعة بانتظار جرد للفرع المختار - بتنقترح أول شي بالجرد الجزئي. */
  readonly returnedPending = signal<ReturnedPendingItemDto[]>([]);
  readonly expandedPendingId = signal<string | null>(null);

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
        this.selectedBranchId = this.auth.defaultBranchId(result.items);
      }
    } catch {
      /* فشل تحميل الفروع لا يمنع عرض القائمة. */
    }
    await this.loadReturnedPending();
  }

  async loadReturnedPending(): Promise<void> {
    try {
      const params: Record<string, string> = {};
      if (this.selectedBranchId) params['branchId'] = this.selectedBranchId;
      const items = await firstValueFrom(this.apiClient.get<ReturnedPendingItemDto[]>(
        ApiController.Stocktakes, StocktakesOperation.ReturnedPending, undefined, params));
      this.returnedPending.set(Array.isArray(items) ? items : []);
    } catch {
      this.returnedPending.set([]);
    }
  }

  onBranchChanged(): void {
    void this.loadReturnedPending();
  }

  togglePendingDetails(productId: string): void {
    this.expandedPendingId.update(id => (id === productId ? null : productId));
  }

  /** "جرد المواد المرتجعة": بيفتح جرد جزئي والمواد محطوطة جاهزة. */
  openReturnedPendingStocktake(): void {
    this.openCreateForm();
    this.scope.set('selected');
    this.selectedProducts.set(this.returnedPending().map(p => ({ id: p.productId, name: p.productName })));
  }

  onProductSearchChanged(): void {
    if (this.productSearchHandle) clearTimeout(this.productSearchHandle);
    const term = this.productSearch.trim();
    if (!term) {
      this.productResults.set([]);
      return;
    }
    this.productSearchHandle = setTimeout(() => void this.searchProducts(term), 250);
  }

  async searchProducts(term: string): Promise<void> {
    try {
      const result = await firstValueFrom(this.apiClient.get<PagedResult<ProductOption>>(
        ApiController.Products, ProductsOperation.List, undefined, { search: term, pageSize: 8 }));
      const chosen = new Set(this.selectedProducts().map(p => p.id));
      this.productResults.set(result.items.filter(p => !chosen.has(p.id)).map(p => ({ id: p.id, name: p.name })));
    } catch {
      this.productResults.set([]);
    }
  }

  addProduct(product: ProductOption): void {
    if (!this.selectedProducts().some(p => p.id === product.id)) {
      this.selectedProducts.update(list => [...list, product]);
    }
    this.productSearch = '';
    this.productResults.set([]);
  }

  removeProduct(productId: string): void {
    this.selectedProducts.update(list => list.filter(p => p.id !== productId));
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
    this.scope.set('all');
    this.selectedProducts.set([]);
    this.productSearch = '';
    this.productResults.set([]);
  }

  closeForm(): void {
    this.formOpen.set(false);
  }

  /** الفرع كامل (الجرد الشهري) أو مواد مختارة (جرد جزئي - مثلًا المواد المرتجعة بانتظار جرد). */
  async submitCreate(): Promise<void> {
    if (!this.selectedBranchId) {
      this.formError.set('اختر فرع.');
      return;
    }
    const partial = this.scope() === 'selected';
    if (partial && this.selectedProducts().length === 0) {
      this.formError.set('اختر مادة وحدة عالأقل للجرد الجزئي.');
      return;
    }

    this.submitting.set(true);
    this.formError.set(null);

    try {
      const response = await firstValueFrom(
        this.apiClient.post<CreateStocktakeResponse>(ApiController.Stocktakes, StocktakesOperation.Create, {
          branchId: this.selectedBranchId,
          includeAllProductsAtBranch: !partial,
          productIds: partial ? this.selectedProducts().map(p => p.id) : null
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
