import { Component, OnInit, signal } from '@angular/core';
import { CommonModule } from '@angular/common';
import { FormsModule } from '@angular/forms';
import { firstValueFrom } from 'rxjs';
import { ApiClient } from '../../core/api/api-client.service';
import { ApiController } from '../../core/api/api-controller.enum';
import { BranchesOperation, UsersOperation } from '../../core/api/operations';
import { PaginationComponent } from '../../shared/components/pagination/pagination.component';

interface BranchListItemDto {
  id: string;
  name: string;
  code: string;
  isActive: boolean;
}

interface UserItemDto {
  userId: string;
  fullName: string;
  username: string;
  email: string;
  isActive: boolean;
  roleNames: string[];
  roleId: string | null;
  defaultBranchName: string | null;
  defaultBranchId: string | null;
}

interface RoleItemDto {
  id: string;
  name: string;
  description: string | null;
}

interface PagedResult<T> {
  items: T[];
  totalCount: number;
}

interface CreateUserResponse {
  userId: string;
  username: string;
}

/**
 * أول صفحة استخدمت Edit حقيقي (بجانب Create) — النموذج نفسه يُعاد
 * استخدامه للحالتين (isEditMode بيقرر السلوك)، تفاديًا لتكرار نفس
 * الحقول بنموذجين منفصلين.
 */
@Component({
  selector: 'app-users',
  standalone: true,
  imports: [CommonModule, FormsModule, PaginationComponent],
  templateUrl: './users.component.html',
  styleUrl: './users.component.css'
})
export class UsersComponent implements OnInit {
  readonly users = signal<UserItemDto[]>([]);
  readonly totalCount = signal(0);
  readonly pageNumber = signal(1);
  readonly pageSize = signal(20);
  readonly roles = signal<RoleItemDto[]>([]);
  readonly branches = signal<BranchListItemDto[]>([]);
  readonly loading = signal(true);
  readonly errorMessage = signal<string | null>(null);

  readonly formOpen = signal(false);
  readonly isEditMode = signal(false);
  readonly submitting = signal(false);
  readonly formError = signal<string | null>(null);

  editingUserId = '';
  fullName = '';
  username = '';
  email = '';
  password = '';
  selectedRoleId = '';
  selectedBranchId = '';
  isActive = true;

  constructor(private readonly apiClient: ApiClient) {}

  ngOnInit(): void {
    this.loadUsers();
    this.loadRoles();
    this.loadBranches();
  }

  private async loadUsers(): Promise<void> {
    this.loading.set(true);
    this.errorMessage.set(null);

    try {
      const result = await firstValueFrom(
        this.apiClient.get<PagedResult<UserItemDto>>(ApiController.Users, UsersOperation.List, undefined, {
          pageNumber: this.pageNumber(),
          pageSize: this.pageSize()
        })
      );
      this.users.set(result.items);
      this.totalCount.set(result.totalCount);
    } catch {
      this.errorMessage.set('تعذّر تحميل قائمة المستخدمين.');
    } finally {
      this.loading.set(false);
    }
  }

  onPageChanged(event: { pageNumber: number; pageSize: number }): void {
    this.pageNumber.set(event.pageNumber);
    this.pageSize.set(event.pageSize);
    this.loadUsers();
  }

  private async loadRoles(): Promise<void> {
    try {
      const roles = await firstValueFrom(
        this.apiClient.get<RoleItemDto[]>(ApiController.Users, UsersOperation.ListRoles)
      );
      this.roles.set(roles);
      if (roles.length > 0 && !this.selectedRoleId) {
        this.selectedRoleId = roles[0].id;
      }
    } catch {
      /* فشل تحميل الأدوار لا يوقف عرض قائمة المستخدمين. */
    }
  }

  private async loadBranches(): Promise<void> {
    try {
      const result = await firstValueFrom(
        this.apiClient.get<{ items: BranchListItemDto[] }>(ApiController.Branches, BranchesOperation.List)
      );
      this.branches.set(result.items.filter(b => b.isActive));
      if (result.items.length > 0 && !this.selectedBranchId) {
        this.selectedBranchId = result.items[0].id;
      }
    } catch {
      /* فشل تحميل الفروع لا يمنع عرض المستخدمين. */
    }
  }

  openCreateForm(): void {
    this.isEditMode.set(false);
    this.formOpen.set(true);
    this.formError.set(null);
  }

  openEditForm(user: UserItemDto): void {
    this.isEditMode.set(true);
    this.editingUserId = user.userId;
    this.fullName = user.fullName;
    this.username = user.username;
    this.email = user.email;
    this.password = '';
    this.selectedRoleId = user.roleId ?? (this.roles()[0]?.id ?? '');
    this.selectedBranchId = user.defaultBranchId ?? (this.branches()[0]?.id ?? '');
    this.isActive = user.isActive;
    this.formOpen.set(true);
    this.formError.set(null);
  }

  closeForm(): void {
    this.formOpen.set(false);
    this.editingUserId = '';
    this.fullName = '';
    this.username = '';
    this.email = '';
    this.password = '';
    this.isActive = true;
  }

  async submit(): Promise<void> {
    if (!this.fullName.trim() || !this.selectedRoleId || !this.selectedBranchId) {
      this.formError.set('عبّي كل الحقول المطلوبة.');
      return;
    }
    if (!this.isEditMode() && (!this.username.trim() || !this.password)) {
      this.formError.set('اسم المستخدم وكلمة السر مطلوبان عند الإنشاء.');
      return;
    }

    this.submitting.set(true);
    this.formError.set(null);

    try {
      if (this.isEditMode()) {
        await firstValueFrom(
          this.apiClient.put(ApiController.Users, UsersOperation.Update, {
            fullName: this.fullName.trim(),
            email: this.email.trim(),
            roleId: this.selectedRoleId,
            branchId: this.selectedBranchId,
            isActive: this.isActive
          }, { userId: this.editingUserId })
        );
      } else {
        await firstValueFrom(
          this.apiClient.post<CreateUserResponse>(ApiController.Users, UsersOperation.Create, {
            fullName: this.fullName.trim(),
            username: this.username.trim(),
            email: this.email.trim() || `${this.username.trim()}@local.test`,
            password: this.password,
            roleId: this.selectedRoleId,
            branchId: this.selectedBranchId
          })
        );
      }

      this.closeForm();
      await this.loadUsers();
    } catch (err: unknown) {
      const message =
        err && typeof err === 'object' && 'error' in err
          ? (err as { error?: { detail?: string } }).error?.detail
          : null;
      this.formError.set(message ?? (this.isEditMode() ? 'تعذّر تعديل المستخدم.' : 'تعذّر إنشاء المستخدم.'));
    } finally {
      this.submitting.set(false);
    }
  }
}
