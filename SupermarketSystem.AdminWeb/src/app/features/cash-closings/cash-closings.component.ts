import { Component, OnInit, signal } from '@angular/core';
import { CommonModule } from '@angular/common';
import { FormsModule } from '@angular/forms';
import { firstValueFrom } from 'rxjs';
import { ApiClient } from '../../core/api/api-client.service';
import { ApiController } from '../../core/api/api-controller.enum';
import { CashClosingsOperation, BranchesOperation, PaymentMethodsOperation } from '../../core/api/operations';
import { PaginationComponent } from '../../shared/components/pagination/pagination.component';

interface BranchDto {
  id: string;
  name: string;
}

interface PaymentMethodDto {
  id: string;
  name: string;
}

interface CashClosingListItemDto {
  id: string;
  branchId: string;
  branchName: string;
  businessDate: string;
  closedAtUtc: string;
  expectedCash: number;
  countedCash: number;
  variance: number;
}

interface PagedResult<T> {
  items: T[];
  totalCount: number;
}

interface CountedDetailRow {
  paymentMethodId: string;
  paymentMethodName: string;
  countedAmount: number | null;
}

/** كانت مفقودة بالكامل - CompleteCashClosing كان جاهزًا بالباك إند بلا أي واجهة تستخدمه. */
@Component({
  selector: 'app-cash-closings',
  standalone: true,
  imports: [CommonModule, FormsModule, PaginationComponent],
  templateUrl: './cash-closings.component.html',
  styleUrl: './cash-closings.component.css'
})
export class CashClosingsComponent implements OnInit {
  readonly closings = signal<CashClosingListItemDto[]>([]);
  readonly totalCount = signal(0);
  readonly pageNumber = signal(1);
  readonly pageSize = signal(20);
  readonly loading = signal(true);
  readonly errorMessage = signal<string | null>(null);

  readonly branches = signal<BranchDto[]>([]);
  readonly paymentMethods = signal<PaymentMethodDto[]>([]);
  readonly countedDetails = signal<CountedDetailRow[]>([]);

  readonly formOpen = signal(false);
  readonly submitting = signal(false);
  readonly formError = signal<string | null>(null);

  selectedBranchId = '';
  filterBranchId = '';
  businessDate = new Date().toISOString().slice(0, 10);
  countedCash: number | null = null;

  constructor(private readonly apiClient: ApiClient) {}

  ngOnInit(): void {
    this.loadLookups();
    this.loadClosings();
  }

  private async loadLookups(): Promise<void> {
    try {
      const [branchesResult, paymentMethods] = await Promise.all([
        firstValueFrom(this.apiClient.get<PagedResult<BranchDto>>(ApiController.Branches, BranchesOperation.List, undefined, { pageSize: 500 })),
        firstValueFrom(this.apiClient.get<PaymentMethodDto[]>(ApiController.PaymentMethods, PaymentMethodsOperation.List))
      ]);

      this.branches.set(branchesResult.items);
      this.paymentMethods.set(paymentMethods);
      this.countedDetails.set(paymentMethods.map(pm => ({ paymentMethodId: pm.id, paymentMethodName: pm.name, countedAmount: null })));

      if (branchesResult.items.length > 0) this.selectedBranchId = branchesResult.items[0].id;
    } catch {
      this.errorMessage.set('تعذّر تحميل الفروع/طرق الدفع.');
    }
  }

  async loadClosings(): Promise<void> {
    this.loading.set(true);
    this.errorMessage.set(null);

    try {
      const result = await firstValueFrom(
        this.apiClient.get<PagedResult<CashClosingListItemDto>>(ApiController.CashClosings, CashClosingsOperation.List, undefined, {
          pageNumber: this.pageNumber(),
          pageSize: this.pageSize(),
          sortDirection: 'desc',
          branchId: this.filterBranchId || undefined
        })
      );
      this.closings.set(result.items);
      this.totalCount.set(result.totalCount);
    } catch {
      this.errorMessage.set('تعذّر تحميل قائمة تقفيلات الصندوق.');
    } finally {
      this.loading.set(false);
    }
  }

  onFilterBranchChange(): void {
    this.pageNumber.set(1);
    this.loadClosings();
  }

  onPageChanged(event: { pageNumber: number; pageSize: number }): void {
    this.pageNumber.set(event.pageNumber);
    this.pageSize.set(event.pageSize);
    this.loadClosings();
  }

  openForm(): void {
    this.formOpen.set(true);
    this.formError.set(null);
    this.businessDate = new Date().toISOString().slice(0, 10);
    this.countedCash = null;
    this.countedDetails.set(this.paymentMethods().map(pm => ({ paymentMethodId: pm.id, paymentMethodName: pm.name, countedAmount: null })));
  }

  closeForm(): void {
    this.formOpen.set(false);
  }

  async submit(): Promise<void> {
    if (!this.selectedBranchId || !this.businessDate || this.countedCash === null || this.countedCash < 0) {
      this.formError.set('عبّي الفرع، اليوم التجاري، والمبلغ المعدود (لا يمكن أن يكون سالبًا).');
      return;
    }

    this.submitting.set(true);
    this.formError.set(null);

    try {
      await firstValueFrom(
        this.apiClient.post(ApiController.CashClosings, CashClosingsOperation.Complete, {
          branchId: this.selectedBranchId,
          businessDate: this.businessDate,
          countedCash: this.countedCash,
          countedDetails: this.countedDetails()
            .filter(d => d.countedAmount !== null)
            .map(d => ({ paymentMethodId: d.paymentMethodId, countedAmount: d.countedAmount }))
        })
      );

      this.closeForm();
      this.pageNumber.set(1);
      await this.loadClosings();
    } catch (err: unknown) {
      const message =
        err && typeof err === 'object' && 'error' in err
          ? (err as { error?: { detail?: string } }).error?.detail
          : null;
      this.formError.set(message ?? 'تعذّر إتمام تقفيل الصندوق.');
    } finally {
      this.submitting.set(false);
    }
  }

  varianceTone(variance: number): 'green' | 'red' {
    return variance === 0 ? 'green' : 'red';
  }
}
