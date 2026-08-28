import { Component, OnInit, signal } from '@angular/core';
import { CommonModule } from '@angular/common';
import { FormsModule } from '@angular/forms';
import { firstValueFrom } from 'rxjs';
import { ApiClient } from '../../core/api/api-client.service';
import { ApiController } from '../../core/api/api-controller.enum';
import { SuppliersOperation } from '../../core/api/operations';
import { PaginationComponent } from '../../shared/components/pagination/pagination.component';

interface SupplierListItemDto {
  id: string;
  name: string;
  contactName: string | null;
  phone: string | null;
  email: string | null;
  isActive: boolean;
}

interface PagedResult<T> {
  items: T[];
  totalCount: number;
}

@Component({
  selector: 'app-suppliers',
  standalone: true,
  imports: [CommonModule, FormsModule, PaginationComponent],
  templateUrl: './suppliers.component.html',
  styleUrl: './suppliers.component.css'
})
export class SuppliersComponent implements OnInit {
  readonly suppliers = signal<SupplierListItemDto[]>([]);
  readonly totalCount = signal(0);
  readonly pageNumber = signal(1);
  readonly pageSize = signal(20);
  readonly loading = signal(true);
  readonly errorMessage = signal<string | null>(null);

  readonly formOpen = signal(false);
  readonly isEditMode = signal(false);
  readonly submitting = signal(false);
  readonly formError = signal<string | null>(null);

  editingSupplierId = '';
  name = '';
  contactName = '';
  phone = '';
  email = '';

  constructor(private readonly apiClient: ApiClient) {}

  ngOnInit(): void {
    this.loadSuppliers();
  }

  private async loadSuppliers(): Promise<void> {
    this.loading.set(true);
    this.errorMessage.set(null);

    try {
      const result = await firstValueFrom(
        this.apiClient.get<PagedResult<SupplierListItemDto>>(ApiController.Suppliers, SuppliersOperation.List, undefined, {
          pageNumber: this.pageNumber(),
          pageSize: this.pageSize()
        })
      );
      this.suppliers.set(result.items);
      this.totalCount.set(result.totalCount);
    } catch {
      this.errorMessage.set('تعذّر تحميل قائمة الموردين.');
    } finally {
      this.loading.set(false);
    }
  }

  onPageChanged(event: { pageNumber: number; pageSize: number }): void {
    this.pageNumber.set(event.pageNumber);
    this.pageSize.set(event.pageSize);
    this.loadSuppliers();
  }

  openCreateForm(): void {
    this.isEditMode.set(false);
    this.formOpen.set(true);
    this.formError.set(null);
  }

  openEditForm(supplier: SupplierListItemDto): void {
    this.isEditMode.set(true);
    this.editingSupplierId = supplier.id;
    this.name = supplier.name;
    this.contactName = supplier.contactName ?? '';
    this.phone = supplier.phone ?? '';
    this.email = supplier.email ?? '';
    this.formOpen.set(true);
    this.formError.set(null);
  }

  closeForm(): void {
    this.formOpen.set(false);
    this.editingSupplierId = '';
    this.name = '';
    this.contactName = '';
    this.phone = '';
    this.email = '';
  }

  async submit(): Promise<void> {
    if (!this.name.trim()) {
      this.formError.set('اسم المورد مطلوب.');
      return;
    }

    this.submitting.set(true);
    this.formError.set(null);

    try {
      if (this.isEditMode()) {
        await firstValueFrom(
          this.apiClient.put(ApiController.Suppliers, SuppliersOperation.Update, {
            name: this.name.trim(),
            contactName: this.contactName.trim() || null,
            phone: this.phone.trim() || null,
            email: this.email.trim() || null
          }, { supplierId: this.editingSupplierId })
        );
      } else {
        await firstValueFrom(
          this.apiClient.post(ApiController.Suppliers, SuppliersOperation.Create, {
            name: this.name.trim(),
            contactName: this.contactName.trim() || null,
            phone: this.phone.trim() || null,
            email: this.email.trim() || null,
            address: null
          })
        );
      }

      this.closeForm();
      await this.loadSuppliers();
    } catch {
      this.formError.set(this.isEditMode() ? 'تعذّر تعديل المورد.' : 'تعذّر إنشاء المورد.');
    } finally {
      this.submitting.set(false);
    }
  }
}
