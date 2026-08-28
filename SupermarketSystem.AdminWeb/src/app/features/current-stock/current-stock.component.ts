import { Component, OnInit, signal } from '@angular/core';
import { CommonModule } from '@angular/common';
import { FormsModule } from '@angular/forms';
import { firstValueFrom } from 'rxjs';
import { ApiClient } from '../../core/api/api-client.service';
import { ApiController } from '../../core/api/api-controller.enum';
import { InventoryOperation, BranchesOperation } from '../../core/api/operations';
import { PaginationComponent } from '../../shared/components/pagination/pagination.component';

interface CurrentStockItemDto {
  productId: string;
  productName: string;
  categoryName: string;
  branchId: string;
  branchName: string;
  quantityOnHand: number;
  baseUnitName: string;
}

interface BranchDto {
  id: string;
  name: string;
}

interface PagedResult<T> {
  items: T[];
  totalCount: number;
}

@Component({
  selector: 'app-current-stock',
  standalone: true,
  imports: [CommonModule, FormsModule, PaginationComponent],
  templateUrl: './current-stock.component.html',
  styleUrl: './current-stock.component.css'
})
export class CurrentStockComponent implements OnInit {
  readonly items = signal<CurrentStockItemDto[]>([]);
  readonly totalCount = signal(0);
  readonly pageNumber = signal(1);
  readonly pageSize = signal(20);
  readonly searchQuery = signal('');

  readonly branches = signal<BranchDto[]>([]);
  selectedBranchId = '';

  readonly loading = signal(true);
  readonly errorMessage = signal<string | null>(null);

  constructor(private readonly apiClient: ApiClient) {}

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
    } catch {
      /* فشل تحميل الفروع لا يمنع عرض المخزون. */
    }
  }

  async load(): Promise<void> {
    this.loading.set(true);
    this.errorMessage.set(null);

    try {
      const result = await firstValueFrom(
        this.apiClient.get<PagedResult<CurrentStockItemDto>>(ApiController.Inventory, InventoryOperation.GetCurrentStock, undefined, {
          pageNumber: this.pageNumber(),
          pageSize: this.pageSize(),
          search: this.searchQuery() || undefined,
          branchId: this.selectedBranchId || undefined
        })
      );
      this.items.set(result.items);
      this.totalCount.set(result.totalCount);
    } catch {
      this.errorMessage.set('تعذّر تحميل المخزون الحالي.');
    } finally {
      this.loading.set(false);
    }
  }

  onSearchChange(value: string): void {
    this.searchQuery.set(value);
    this.pageNumber.set(1);
    this.load();
  }

  onBranchFilterChange(): void {
    this.pageNumber.set(1);
    this.load();
  }

  onPageChanged(event: { pageNumber: number; pageSize: number }): void {
    this.pageNumber.set(event.pageNumber);
    this.pageSize.set(event.pageSize);
    this.load();
  }
}
