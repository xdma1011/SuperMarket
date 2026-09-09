import { Component, OnInit, signal } from '@angular/core';
import { CommonModule } from '@angular/common';
import { FormsModule } from '@angular/forms';
import { firstValueFrom } from 'rxjs';
import { ApiClient } from '../../core/api/api-client.service';
import { ApiController } from '../../core/api/api-controller.enum';
import { BranchesOperation } from '../../core/api/operations';
import { PaginationComponent } from '../../shared/components/pagination/pagination.component';

interface BranchListItemDto {
  id: string;
  name: string;
  code: string;
  isActive: boolean;
  phoneNumber: string | null;
}

interface PagedResult<T> {
  items: T[];
  totalCount: number;
}

/** كانت مفقودة بالكامل - الـAPI (CreateBranch/GetBranches) كان جاهزًا بلا أي واجهة تستخدمه. */
@Component({
  selector: 'app-branches',
  standalone: true,
  imports: [CommonModule, FormsModule, PaginationComponent],
  templateUrl: './branches.component.html',
  styleUrl: './branches.component.css'
})
export class BranchesComponent implements OnInit {
  readonly branches = signal<BranchListItemDto[]>([]);
  readonly totalCount = signal(0);
  readonly pageNumber = signal(1);
  readonly pageSize = signal(20);
  readonly loading = signal(true);
  readonly errorMessage = signal<string | null>(null);

  readonly formOpen = signal(false);
  readonly isEditMode = signal(false);
  readonly submitting = signal(false);
  readonly formError = signal<string | null>(null);
  readonly togglingId = signal<string | null>(null);

  editingBranchId = '';
  name = '';
  code = '';
  phoneNumber = '';

  constructor(private readonly apiClient: ApiClient) {}

  ngOnInit(): void {
    this.loadBranches();
  }

  private async loadBranches(): Promise<void> {
    this.loading.set(true);
    this.errorMessage.set(null);

    try {
      const result = await firstValueFrom(
        this.apiClient.get<PagedResult<BranchListItemDto>>(ApiController.Branches, BranchesOperation.List, undefined, {
          pageNumber: this.pageNumber(),
          pageSize: this.pageSize()
        })
      );
      this.branches.set(result.items);
      this.totalCount.set(result.totalCount);
    } catch {
      this.errorMessage.set('تعذّر تحميل قائمة الفروع.');
    } finally {
      this.loading.set(false);
    }
  }

  onPageChanged(event: { pageNumber: number; pageSize: number }): void {
    this.pageNumber.set(event.pageNumber);
    this.pageSize.set(event.pageSize);
    this.loadBranches();
  }

  openCreateForm(): void {
    this.isEditMode.set(false);
    this.name = '';
    this.code = '';
    this.phoneNumber = '';
    this.formOpen.set(true);
    this.formError.set(null);
  }

  openEditForm(branch: BranchListItemDto): void {
    this.isEditMode.set(true);
    this.editingBranchId = branch.id;
    this.name = branch.name;
    this.code = branch.code;
    this.phoneNumber = branch.phoneNumber ?? '';
    this.formOpen.set(true);
    this.formError.set(null);
  }

  closeForm(): void {
    this.formOpen.set(false);
    this.editingBranchId = '';
    this.name = '';
    this.code = '';
    this.phoneNumber = '';
  }

  async submit(): Promise<void> {
    if (!this.name.trim()) {
      this.formError.set('اسم الفرع مطلوب.');
      return;
    }
    if (!this.isEditMode() && !this.code.trim()) {
      this.formError.set('كود الفرع مطلوب عند الإنشاء.');
      return;
    }

    this.submitting.set(true);
    this.formError.set(null);

    try {
      if (this.isEditMode()) {
        await firstValueFrom(
          this.apiClient.put(ApiController.Branches, BranchesOperation.Update, {
            name: this.name.trim(),
            phoneNumber: this.phoneNumber.trim() || null
          }, { branchId: this.editingBranchId })
        );
      } else {
        await firstValueFrom(
          this.apiClient.post(ApiController.Branches, BranchesOperation.Create, {
            name: this.name.trim(),
            code: this.code.trim(),
            phoneNumber: this.phoneNumber.trim() || null,
            street: null,
            city: null,
            postalCode: null,
            country: null
          })
        );
      }

      this.closeForm();
      await this.loadBranches();
    } catch (err: unknown) {
      const message =
        err && typeof err === 'object' && 'error' in err
          ? (err as { error?: { detail?: string } }).error?.detail
          : null;
      this.formError.set(message ?? (this.isEditMode() ? 'تعذّر تعديل الفرع.' : 'تعذّر إنشاء الفرع.'));
    } finally {
      this.submitting.set(false);
    }
  }

  async toggleActive(branch: BranchListItemDto): Promise<void> {
    this.togglingId.set(branch.id);
    this.errorMessage.set(null);

    try {
      await firstValueFrom(
        this.apiClient.post(ApiController.Branches, BranchesOperation.SetActive, {
          isActive: !branch.isActive
        }, { branchId: branch.id })
      );
      await this.loadBranches();
    } catch {
      this.errorMessage.set('تعذّر تغيير حالة الفرع.');
    } finally {
      this.togglingId.set(null);
    }
  }
}
