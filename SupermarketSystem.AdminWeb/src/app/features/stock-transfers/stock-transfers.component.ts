import { Component, OnInit, signal } from '@angular/core';
import { CommonModule } from '@angular/common';
import { FormsModule } from '@angular/forms';
import { firstValueFrom } from 'rxjs';
import { ApiClient } from '../../core/api/api-client.service';
import { ApiController } from '../../core/api/api-controller.enum';
import { StockTransfersOperation, BranchesOperation, ProductsOperation } from '../../core/api/operations';
import { PaginationComponent } from '../../shared/components/pagination/pagination.component';

interface BranchDto {
  id: string;
  name: string;
}

interface ProductDto {
  id: string;
  name: string;
  isBatchTracked: boolean;
}

interface ProductUnitDto {
  id: string;
  unitName: string;
  isBaseUnit: boolean;
}

interface ProductBatchWithStockDto {
  productBatchId: string;
  batchNumber: string;
  expiryDate: string | null;
  quantityOnHand: number;
}

interface StockTransferListItemDto {
  id: string;
  transferNumber: string;
  sourceBranchName: string;
  destinationBranchName: string;
  statusCode: number;
  statusTitle: string;
  dispatchedAtUtc: string;
  receivedAtUtc: string | null;
  itemCount: number;
}

interface StockTransferDetailItemDto {
  productId: string;
  productName: string;
  unitName: string;
  quantityBase: number;
  batchNumber: string | null;
}

interface StockTransferDetailDto {
  id: string;
  transferNumber: string;
  sourceBranchName: string;
  destinationBranchName: string;
  statusTitle: string;
  items: StockTransferDetailItemDto[];
}

interface PagedResult<T> {
  items: T[];
  totalCount: number;
}

interface TransferLine {
  productId: string;
  productName: string;
  isBatchTracked: boolean;
  unitId: string;
  units: ProductUnitDto[];
  quantity: number;
  batches: ProductBatchWithStockDto[];
  selectedBatchId: string;
}

/** كانت مفقودة بالكامل - نقل بضاعة بين فرعين على خطوتين حقيقيتين (إرسال ثم استلام منفصل). */
@Component({
  selector: 'app-stock-transfers',
  standalone: true,
  imports: [CommonModule, FormsModule, PaginationComponent],
  templateUrl: './stock-transfers.component.html',
  styleUrl: './stock-transfers.component.css'
})
export class StockTransfersComponent implements OnInit {
  readonly transfers = signal<StockTransferListItemDto[]>([]);
  readonly totalCount = signal(0);
  readonly pageNumber = signal(1);
  readonly pageSize = signal(20);
  readonly loading = signal(true);
  readonly errorMessage = signal<string | null>(null);

  readonly branches = signal<BranchDto[]>([]);
  readonly products = signal<ProductDto[]>([]);

  readonly formOpen = signal(false);
  readonly submitting = signal(false);
  readonly formError = signal<string | null>(null);

  sourceBranchId = '';
  destinationBranchId = '';
  lines: TransferLine[] = [];
  newLineProductId = '';

  readonly receiveModalOpen = signal(false);
  readonly receiveDetail = signal<StockTransferDetailDto | null>(null);
  readonly receiveSubmitting = signal(false);
  readonly receiveError = signal<string | null>(null);
  private receivingTransferId = '';

  constructor(private readonly apiClient: ApiClient) {}

  ngOnInit(): void {
    this.loadAll();
  }

  private async loadAll(): Promise<void> {
    this.loading.set(true);
    this.errorMessage.set(null);

    try {
      const [transfersResult, branchesResult, productsResult] = await Promise.all([
        firstValueFrom(this.apiClient.get<PagedResult<StockTransferListItemDto>>(
          ApiController.StockTransfers, StockTransfersOperation.List, undefined,
          { pageNumber: this.pageNumber(), pageSize: this.pageSize(), sortDirection: 'desc' })),
        firstValueFrom(this.apiClient.get<PagedResult<BranchDto>>(ApiController.Branches, BranchesOperation.List, undefined, { pageSize: 500 })),
        firstValueFrom(this.apiClient.get<PagedResult<ProductDto>>(ApiController.Products, ProductsOperation.List, undefined, { pageSize: 500 }))
      ]);

      this.transfers.set(transfersResult.items);
      this.totalCount.set(transfersResult.totalCount);
      this.branches.set(branchesResult.items);
      this.products.set(productsResult.items);

      if (branchesResult.items.length > 0) {
        this.sourceBranchId = branchesResult.items[0].id;
        this.destinationBranchId = branchesResult.items.length > 1 ? branchesResult.items[1].id : branchesResult.items[0].id;
      }
      if (productsResult.items.length > 0) {
        this.newLineProductId = productsResult.items[0].id;
      }
    } catch {
      this.errorMessage.set('تعذّر تحميل عمليات النقل.');
    } finally {
      this.loading.set(false);
    }
  }

  onPageChanged(event: { pageNumber: number; pageSize: number }): void {
    this.pageNumber.set(event.pageNumber);
    this.pageSize.set(event.pageSize);
    this.loadAll();
  }

  openForm(): void {
    this.formOpen.set(true);
    this.formError.set(null);
    this.lines = [];
  }

  closeForm(): void {
    this.formOpen.set(false);
  }

  async addLine(): Promise<void> {
    if (!this.newLineProductId || !this.sourceBranchId) return;

    const product = this.products().find(p => p.id === this.newLineProductId);
    if (!product) return;

    try {
      const units = await firstValueFrom(
        this.apiClient.get<ProductUnitDto[]>(ApiController.Products, ProductsOperation.GetUnits, { productId: product.id })
      );

      if (units.length === 0) {
        this.formError.set(`المنتج "${product.name}" ليس له وحدة معرَّفة.`);
        return;
      }

      const baseUnit = units.find(u => u.isBaseUnit) ?? units[0];

      let batches: ProductBatchWithStockDto[] = [];
      if (product.isBatchTracked) {
        batches = await firstValueFrom(
          this.apiClient.get<ProductBatchWithStockDto[]>(ApiController.StockTransfers, StockTransfersOperation.ProductBatches, undefined, {
            productId: product.id,
            branchId: this.sourceBranchId
          })
        );

        if (batches.length === 0) {
          this.formError.set(`المنتج "${product.name}" يتتبّع دفعات - لا يوجد أي دفعة فيها رصيد بالفرع المصدر المختار.`);
          return;
        }
      }

      this.lines = [
        ...this.lines,
        {
          productId: product.id,
          productName: product.name,
          isBatchTracked: product.isBatchTracked,
          unitId: baseUnit.id,
          units,
          quantity: 1,
          batches,
          selectedBatchId: batches.length > 0 ? batches[0].productBatchId : ''
        }
      ];
    } catch {
      this.formError.set('تعذّر جلب بيانات المنتج.');
    }
  }

  removeLine(index: number): void {
    this.lines = this.lines.filter((_, i) => i !== index);
  }

  async submit(): Promise<void> {
    if (!this.sourceBranchId || !this.destinationBranchId) {
      this.formError.set('حدّد الفرع المصدر والوجهة.');
      return;
    }
    if (this.sourceBranchId === this.destinationBranchId) {
      this.formError.set('الفرع المصدر والوجهة لازم يكونوا مختلفين.');
      return;
    }
    if (this.lines.length === 0) {
      this.formError.set('أضف سطرًا واحدًا على الأقل.');
      return;
    }
    if (this.lines.some(l => l.quantity <= 0)) {
      this.formError.set('كل سطر يحتاج كمية موجبة.');
      return;
    }
    if (this.lines.some(l => l.isBatchTracked && !l.selectedBatchId)) {
      this.formError.set('حدّد الدفعة لكل صنف يتتبّع دفعات.');
      return;
    }

    this.submitting.set(true);
    this.formError.set(null);

    try {
      await firstValueFrom(
        this.apiClient.post(ApiController.StockTransfers, StockTransfersOperation.Create, {
          sourceBranchId: this.sourceBranchId,
          destinationBranchId: this.destinationBranchId,
          items: this.lines.map(l => ({
            productId: l.productId,
            productUnitId: l.unitId,
            quantity: l.quantity,
            sourceProductBatchId: l.isBatchTracked ? l.selectedBatchId : null
          }))
        })
      );

      this.closeForm();
      this.pageNumber.set(1);
      await this.loadAll();
    } catch (err: unknown) {
      const message =
        err && typeof err === 'object' && 'error' in err
          ? (err as { error?: { detail?: string } }).error?.detail
          : null;
      this.formError.set(message ?? 'تعذّر إرسال النقل.');
    } finally {
      this.submitting.set(false);
    }
  }

  async openReceiveModal(transfer: StockTransferListItemDto): Promise<void> {
    this.receivingTransferId = transfer.id;
    this.receiveModalOpen.set(true);
    this.receiveError.set(null);
    this.receiveDetail.set(null);

    try {
      const detail = await firstValueFrom(
        this.apiClient.get<StockTransferDetailDto>(ApiController.StockTransfers, StockTransfersOperation.Detail, { stockTransferId: transfer.id })
      );
      this.receiveDetail.set(detail);
    } catch {
      this.receiveError.set('تعذّر تحميل تفاصيل عملية النقل.');
    }
  }

  closeReceiveModal(): void {
    this.receiveModalOpen.set(false);
    this.receivingTransferId = '';
  }

  async confirmReceive(): Promise<void> {
    this.receiveSubmitting.set(true);
    this.receiveError.set(null);

    try {
      await firstValueFrom(
        this.apiClient.post(ApiController.StockTransfers, StockTransfersOperation.Receive, {}, { stockTransferId: this.receivingTransferId })
      );
      this.closeReceiveModal();
      await this.loadAll();
    } catch (err: unknown) {
      const message =
        err && typeof err === 'object' && 'error' in err
          ? (err as { error?: { detail?: string } }).error?.detail
          : null;
      this.receiveError.set(message ?? 'تعذّر تأكيد الاستلام.');
    } finally {
      this.receiveSubmitting.set(false);
    }
  }
}
