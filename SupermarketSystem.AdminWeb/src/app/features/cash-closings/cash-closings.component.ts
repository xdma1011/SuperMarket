import { Component, OnInit, computed, signal, inject } from '@angular/core';
import { CommonModule } from '@angular/common';
import { FormsModule } from '@angular/forms';
import { firstValueFrom } from 'rxjs';
import { ApiClient } from '../../core/api/api-client.service';
import { isRequestCancelled, latestRequest } from '../../core/api/latest-request';
import { ApiController } from '../../core/api/api-controller.enum';
import { CashClosingsOperation, BranchesOperation, PaymentMethodsOperation } from '../../core/api/operations';
import { PaginationComponent } from '../../shared/components/pagination/pagination.component';
import { AuthService } from '../../core/services/auth.service';
import { BusinessTimeService } from '../../core/services/business-time.service';
import { PermissionsService } from '../../core/services/permissions.service';

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
  shiftNumber: number;
  closedAtUtc: string;
  expectedCash: number;
  countedCash: number;
  variance: number;
  pendingSalesCount: number;
  pendingSalesAmount: number;
  explainedVariance: number;
  unexplainedVariance: number;
}

interface VarianceNoteDto {
  id: string;
  reasonCode: number;
  reasonTitle: string;
  explainedAmount: number;
  note: string | null;
  relatedExpenseId: string | null;
  recordedByName: string;
  recordedAtUtc: string;
}

interface VarianceNotesResponse {
  cashClosingId: string;
  variance: number;
  explainedTotal: number;
  unexplainedVariance: number;
  pendingSalesCount: number;
  pendingSalesAmount: number;
  notes: VarianceNoteDto[];
}

// أسماء VarianceExplanationReason بالـC# - الباك إند بيقبل ويرجّع الأسماء (JsonStringEnumConverter عام).
export const VARIANCE_REASONS: { value: string; label: string }[] = [
  { value: 'LateSales', label: 'بيعات متأخرة (بطابور الكاشير)' },
  { value: 'ForgottenDrawerPayment', label: 'دفعة/مصروف من الدرج ما انسجّل' },
  { value: 'CountError', label: 'خطأ بعدّ الكاش' },
  { value: 'Unknown', label: 'غير معروف (سألنا وما في تفسير)' },
  { value: 'Other', label: 'سبب آخر (اكتب ملاحظة)' }
];

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
  private readonly listRequest = latestRequest();
  private readonly auth = inject(AuthService);
  private readonly businessTime = inject(BusinessTimeService);
  private readonly permissions = inject(PermissionsService);
  private readonly notesRequest = latestRequest();

  /** تفسير الفرق = Returns.Review (صاحب المحل/مساعد الأدمن)؛ الكاشير ما بيفسّر فرق نفسه. fail-open لحد ما تتحمّل الصلاحيات (الحماية الحقيقية بالباك إند). */
  readonly canExplain = computed(() => !this.permissions.loaded() || this.permissions.has('Returns.Review'));
  readonly varianceReasons = VARIANCE_REASONS;

  readonly explainOpenId = signal<string | null>(null);
  readonly varianceNotes = signal<VarianceNotesResponse | null>(null);
  readonly notesLoading = signal(false);
  readonly noteSaving = signal(false);
  readonly noteError = signal<string | null>(null);
  noteReason = 'LateSales';
  noteAmount: number | null = null;
  noteText = '';

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
  businessDate = this.businessTime.localDate();
  shiftNumber = 1;
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

      if (branchesResult.items.length > 0) this.selectedBranchId = this.auth.defaultBranchId(branchesResult.items);
    } catch {
      this.errorMessage.set('تعذّر تحميل الفروع/طرق الدفع.');
    }
  }

  async loadClosings(): Promise<void> {
    this.loading.set(true);
    this.errorMessage.set(null);

    try {
      const result = await this.listRequest.run(
        this.apiClient.get<PagedResult<CashClosingListItemDto>>(ApiController.CashClosings, CashClosingsOperation.List, undefined, {
          pageNumber: this.pageNumber(),
          pageSize: this.pageSize(),
          sortDirection: 'desc',
          branchId: this.filterBranchId || undefined
        })
      );
      this.closings.set(result.items);
      this.totalCount.set(result.totalCount);
    } catch (err) {
      if (isRequestCancelled(err)) return;
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
    this.businessDate = this.businessTime.localDate();
    this.shiftNumber = 1;
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

    if (!this.shiftNumber || this.shiftNumber < 1) {
      this.formError.set('رقم الوردية يجب أن يكون 1 على الأقل.');
      return;
    }

    this.submitting.set(true);
    this.formError.set(null);

    try {
      await firstValueFrom(
        this.apiClient.post(ApiController.CashClosings, CashClosingsOperation.Complete, {
          branchId: this.selectedBranchId,
          businessDate: this.businessDate,
          shiftNumber: this.shiftNumber,
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

  async toggleExplain(closing: CashClosingListItemDto): Promise<void> {
    if (this.explainOpenId() === closing.id) {
      this.explainOpenId.set(null);
      this.varianceNotes.set(null);
      this.notesRequest.cancel();
      return;
    }

    this.explainOpenId.set(closing.id);
    this.varianceNotes.set(null);
    this.noteError.set(null);
    this.noteReason = closing.pendingSalesCount > 0 ? 'LateSales' : 'ForgottenDrawerPayment';
    this.noteAmount = null;
    this.noteText = '';
    this.notesLoading.set(true);
    try {
      const result = await this.notesRequest.run(
        this.apiClient.get<VarianceNotesResponse>(ApiController.CashClosings, CashClosingsOperation.VarianceNotes, { id: closing.id })
      );
      if (this.explainOpenId() === closing.id) this.varianceNotes.set(result);
    } catch (err) {
      if (isRequestCancelled(err)) return;
      this.noteError.set('تعذّر تحميل تفسيرات الفرق.');
    } finally {
      this.notesLoading.set(false);
    }
  }

  /** المتبقي بلا تفسير بالقيمة المطلقة - بيعبّي مبلغ التفسير تلقائيًا بالكبسة. */
  remainingToExplain(): number {
    const notes = this.varianceNotes();
    return notes ? Math.abs(notes.unexplainedVariance) : 0;
  }

  fillRemaining(): void {
    this.noteAmount = this.remainingToExplain();
  }

  async saveNote(closing: CashClosingListItemDto): Promise<void> {
    if (this.noteAmount === null || this.noteAmount < 0) {
      this.noteError.set('اكتب المبلغ اللي بيفسّره هالسبب (صفر لو "ما في تفسير").');
      return;
    }

    if (this.noteReason === 'Other' && !this.noteText.trim()) {
      this.noteError.set('اكتب ملاحظة توضّح السبب.');
      return;
    }

    this.noteSaving.set(true);
    this.noteError.set(null);
    try {
      const result = await firstValueFrom(
        this.apiClient.post<VarianceNotesResponse>(
          ApiController.CashClosings,
          CashClosingsOperation.VarianceNotes,
          { reason: this.noteReason, explainedAmount: this.noteAmount, note: this.noteText.trim() || null, relatedExpenseId: null },
          { id: closing.id }
        )
      );
      this.varianceNotes.set(result);
      this.noteAmount = null;
      this.noteText = '';
      await this.loadClosings();
    } catch (err: unknown) {
      const message =
        err && typeof err === 'object' && 'error' in err
          ? (err as { error?: { detail?: string } }).error?.detail
          : null;
      this.noteError.set(message ?? 'تعذّر حفظ التفسير.');
    } finally {
      this.noteSaving.set(false);
    }
  }

  varianceTone(variance: number): 'green' | 'red' {
    return variance === 0 ? 'green' : 'red';
  }
}
