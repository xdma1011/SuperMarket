import { Component, OnInit, signal } from '@angular/core';
import { CommonModule } from '@angular/common';
import { FormsModule } from '@angular/forms';
import { firstValueFrom } from 'rxjs';
import { ApiClient } from '../../core/api/api-client.service';
import { ApiController } from '../../core/api/api-controller.enum';
import { ProductsOperation, BranchesOperation, InventoryOperation } from '../../core/api/operations';

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

/**
 * صفحة بسيطة عمدًا — بلا جدول سجل سابق (StockMovement هو السجل نفسه،
 * لو احتجنا عرضه لاحقًا فهو استعلام تقرير منفصل). الهدف الوحيد هون:
 * تسجيل خروج بضاعة بسرعة، بلا أي قيد مالي.
 */
@Component({
  selector: 'app-complimentary',
  standalone: true,
  imports: [CommonModule, FormsModule],
  templateUrl: './complimentary.component.html',
  styleUrl: './complimentary.component.css'
})
export class ComplimentaryComponent implements OnInit {
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

  constructor(private readonly apiClient: ApiClient) {}

  ngOnInit(): void {
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

      if (branchesResult.items.length > 0) this.selectedBranchId = branchesResult.items[0].id;
      if (productsResult.items.length > 0) {
        this.selectedProductId = productsResult.items[0].id;
        await this.onProductChange();
      }
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
      const units = await firstValueFrom(
        this.apiClient.get<ProductUnitDto[]>(ApiController.Products, ProductsOperation.GetUnits, { productId: this.selectedProductId })
      );
      this.units.set(units);
      const baseUnit = units.find(u => u.isBaseUnit) ?? units[0];
      if (baseUnit) this.selectedUnitId = baseUnit.id;
    } catch {
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
}
