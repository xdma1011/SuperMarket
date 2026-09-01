import { Component, OnInit, signal } from '@angular/core';
import { CommonModule } from '@angular/common';
import { FormsModule } from '@angular/forms';
import { Router } from '@angular/router';
import { firstValueFrom } from 'rxjs';
import { ApiClient } from '../../core/api/api-client.service';
import { ApiController } from '../../core/api/api-controller.enum';
import { PurchaseInvoiceDraftsOperation, BranchesOperation } from '../../core/api/operations';

interface PurchaseInvoiceDraftListItemDto {
  id: string;
  rawSupplierName: string | null;
  matchedSupplierId: string | null;
  supplierInvoiceReference: string | null;
  providerName: string | null;
  extractionConfidence: string | null;
  itemCount: number;
  unmatchedItemCount: number;
  status: number;
  createdAtUtc: string;
}

interface BranchDto {
  id: string;
  name: string;
}

interface PagedResult<T> {
  items: T[];
  totalCount: number;
}

/**
 * قائمة مسودات فواتير الشراء (نتيجة قراءة الذكاء الاصطناعي بانتظار
 * مراجعة). الرفع نفسه (modal) موجود هون، بس المراجعة/التعديل التفصيلي
 * بصفحة منفصلة (purchasing-draft-detail) - فتح كل سطر ومطابقته يحتاج
 * مساحة شاشة كاملة، مش مودال فوق قائمة.
 */
@Component({
  selector: 'app-purchasing-drafts',
  standalone: true,
  imports: [CommonModule, FormsModule],
  templateUrl: './purchasing-drafts.component.html',
  styleUrl: './purchasing-drafts.component.css'
})
export class PurchasingDraftsComponent implements OnInit {
  readonly drafts = signal<PurchaseInvoiceDraftListItemDto[]>([]);
  readonly branches = signal<BranchDto[]>([]);
  readonly loading = signal(true);
  readonly errorMessage = signal<string | null>(null);

  readonly uploadModalOpen = signal(false);
  readonly uploading = signal(false);
  readonly uploadError = signal<string | null>(null);

  selectedBranchId = '';
  selectedFile: File | null = null;

  constructor(private readonly apiClient: ApiClient, private readonly router: Router) {}

  ngOnInit(): void {
    this.loadAll();
  }

  private async loadAll(): Promise<void> {
    this.loading.set(true);
    this.errorMessage.set(null);

    try {
      const [draftsResult, branchesResult] = await Promise.all([
        firstValueFrom(
          this.apiClient.get<PagedResult<PurchaseInvoiceDraftListItemDto>>(
            ApiController.PurchaseInvoices, PurchaseInvoiceDraftsOperation.List, undefined, { pageSize: 100 })
        ),
        firstValueFrom(
          this.apiClient.get<PagedResult<BranchDto>>(ApiController.Branches, BranchesOperation.List, undefined, { pageSize: 500 })
        )
      ]);

      this.drafts.set(draftsResult.items);
      this.branches.set(branchesResult.items);
      if (branchesResult.items.length > 0 && !this.selectedBranchId) {
        this.selectedBranchId = branchesResult.items[0].id;
      }
    } catch {
      this.errorMessage.set('تعذّر تحميل مسودات فواتير الشراء.');
    } finally {
      this.loading.set(false);
    }
  }

  branchNameOf(id: string | null): string {
    if (!id) return '—';
    return this.branches().find(b => b.id === id)?.name ?? '—';
  }

  openUploadModal(): void {
    this.uploadModalOpen.set(true);
    this.uploadError.set(null);
    this.selectedFile = null;
  }

  closeUploadModal(): void {
    this.uploadModalOpen.set(false);
  }

  onFileSelected(event: Event): void {
    const input = event.target as HTMLInputElement;
    this.selectedFile = input.files?.[0] ?? null;
  }

  async upload(): Promise<void> {
    if (!this.selectedBranchId || !this.selectedFile) {
      this.uploadError.set('اختر الفرع والصورة أولًا.');
      return;
    }

    this.uploading.set(true);
    this.uploadError.set(null);

    try {
      const formData = new FormData();
      formData.append('file', this.selectedFile);

      const response = await firstValueFrom(
        this.apiClient.post<{ draftId: string }>(
          ApiController.PurchaseInvoices,
          PurchaseInvoiceDraftsOperation.CreateFromImage,
          formData,
          undefined,
          { branchId: this.selectedBranchId }
        )
      );

      this.closeUploadModal();
      await this.router.navigate(['/purchases/drafts', response.draftId]);
    } catch (err: unknown) {
      const message =
        err && typeof err === 'object' && 'error' in err
          ? (err as { error?: { detail?: string } }).error?.detail
          : null;
      this.uploadError.set(message ?? 'تعذّرت قراءة الفاتورة آليًا - جرّب صورة أوضح، أو أدخلها يدويًا.');
    } finally {
      this.uploading.set(false);
    }
  }

  openDraft(draft: PurchaseInvoiceDraftListItemDto): void {
    this.router.navigate(['/purchases/drafts', draft.id]);
  }
}
