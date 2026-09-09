import { Component, OnInit, signal } from '@angular/core';
import { CommonModule } from '@angular/common';
import { FormsModule } from '@angular/forms';
import { firstValueFrom } from 'rxjs';
import { ApiClient } from '../../core/api/api-client.service';
import { ApiController } from '../../core/api/api-controller.enum';
import { CustomersOperation } from '../../core/api/operations';
import { PaginationComponent } from '../../shared/components/pagination/pagination.component';

interface CustomerListItemDto {
  id: string;
  fullName: string;
  phone: string | null;
  email: string | null;
  isBlocked: boolean;
  createdAtUtc: string;
}

interface PagedResult<T> {
  items: T[];
  totalCount: number;
}

let searchDebounceTimer: ReturnType<typeof setTimeout> | undefined;

/** كانت مفقودة بالكامل - الـAPI (GetCustomers/BlockCustomer/UnblockCustomer) كان جاهزًا بلا أي واجهة تستخدمه. */
@Component({
  selector: 'app-customers',
  standalone: true,
  imports: [CommonModule, FormsModule, PaginationComponent],
  templateUrl: './customers.component.html',
  styleUrl: './customers.component.css'
})
export class CustomersComponent implements OnInit {
  readonly customers = signal<CustomerListItemDto[]>([]);
  readonly totalCount = signal(0);
  readonly pageNumber = signal(1);
  readonly pageSize = signal(20);
  readonly searchQuery = signal('');
  readonly loading = signal(true);
  readonly errorMessage = signal<string | null>(null);
  readonly togglingId = signal<string | null>(null);

  constructor(private readonly apiClient: ApiClient) {}

  ngOnInit(): void {
    this.loadCustomers();
  }

  async loadCustomers(): Promise<void> {
    this.loading.set(true);
    this.errorMessage.set(null);

    try {
      const result = await firstValueFrom(
        this.apiClient.get<PagedResult<CustomerListItemDto>>(ApiController.Customers, CustomersOperation.List, undefined, {
          pageNumber: this.pageNumber(),
          pageSize: this.pageSize(),
          search: this.searchQuery() || undefined
        })
      );
      this.customers.set(result.items);
      this.totalCount.set(result.totalCount);
    } catch {
      this.errorMessage.set('تعذّر تحميل قائمة الزبائن.');
    } finally {
      this.loading.set(false);
    }
  }

  onSearchChange(value: string): void {
    this.searchQuery.set(value);
    clearTimeout(searchDebounceTimer);
    searchDebounceTimer = setTimeout(() => {
      this.pageNumber.set(1);
      this.loadCustomers();
    }, 350);
  }

  onPageChanged(event: { pageNumber: number; pageSize: number }): void {
    this.pageNumber.set(event.pageNumber);
    this.pageSize.set(event.pageSize);
    this.loadCustomers();
  }

  async toggleBlock(customer: CustomerListItemDto): Promise<void> {
    this.togglingId.set(customer.id);
    this.errorMessage.set(null);

    try {
      const operation = customer.isBlocked ? CustomersOperation.Unblock : CustomersOperation.Block;
      await firstValueFrom(
        this.apiClient.post(ApiController.Customers, operation, {}, { customerId: customer.id })
      );
      await this.loadCustomers();
    } catch {
      this.errorMessage.set('تعذّر تغيير حالة حظر الزبون.');
    } finally {
      this.togglingId.set(null);
    }
  }
}
