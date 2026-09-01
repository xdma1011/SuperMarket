import { Component, OnInit, signal } from '@angular/core';
import { CommonModule } from '@angular/common';
import { FormsModule } from '@angular/forms';
import { firstValueFrom } from 'rxjs';
import { ApiClient } from '../../core/api/api-client.service';
import { ApiController } from '../../core/api/api-controller.enum';
import { PurchaseInvoiceDraftsOperation, BranchesOperation, PaymentMethodsOperation } from '../../core/api/operations';

interface BranchDto {
  id: string;
  name: string;
}

interface PaymentMethodDto {
  id: string;
  name: string;
}

interface PagedResult<T> {
  items: T[];
  totalCount: number;
}

/**
 * صفحة رفع مستقلة، متاحة حتى بصلاحية Purchasing.CreateDraft بس (الكاشير
 * افتراضيًا) - بعكس صفحة قائمة المسودات (/purchases/drafts) اللي تحتاج
 * Purchasing.Create كاملة. عمدًا ما بتفتح صفحة المراجعة بعد الرفع (الكاشير
 * غالبًا ما يقدر يفتحها أصلًا لو ما عنده Purchasing.Create) - بس بترجّع
 * رسالة تأكيد "بانتظار مراجعة الإدارة".
 *
 * حقل "المبلغ المدفوع الآن" اختياري - لو الكاشير أو المشرف دفع كاش
 * (أو أي طريقة تؤثر على درج الكاش) للمورد لحظة استلام البضاعة، هالمبلغ
 * ينكتب فورًا كحركة بدرج الكاش (لا ينتظر اعتماد المراجع لاحقًا) - هذا
 * يحل مشكلة توقيت حقيقية بتقفيل الصندوق اليومي.
 */
@Component({
  selector: 'app-upload-invoice-image',
  standalone: true,
  imports: [CommonModule, FormsModule],
  templateUrl: './upload-invoice-image.component.html',
  styleUrl: './upload-invoice-image.component.css'
})
export class UploadInvoiceImageComponent implements OnInit {
  readonly branches = signal<BranchDto[]>([]);
  readonly paymentMethods = signal<PaymentMethodDto[]>([]);
  readonly uploading = signal(false);
  readonly errorMessage = signal<string | null>(null);
  readonly successMessage = signal<string | null>(null);

  selectedBranchId = '';
  selectedFile: File | null = null;
  paidNow = false;
  paidNowAmount: number | null = null;
  paidNowPaymentMethodId = '';

  constructor(private readonly apiClient: ApiClient) {}

  ngOnInit(): void {
    this.loadBranches();
    this.loadPaymentMethods();
  }

  private async loadBranches(): Promise<void> {
    try {
      const result = await firstValueFrom(
        this.apiClient.get<PagedResult<BranchDto>>(ApiController.Branches, BranchesOperation.List, undefined, { pageSize: 500 })
      );
      this.branches.set(result.items);
      if (result.items.length > 0) this.selectedBranchId = result.items[0].id;
    } catch {
      this.errorMessage.set('تعذّر تحميل قائمة الفروع.');
    }
  }

  private async loadPaymentMethods(): Promise<void> {
    try {
      const result = await firstValueFrom(
        this.apiClient.get<PaymentMethodDto[]>(ApiController.PaymentMethods, PaymentMethodsOperation.List)
      );
      this.paymentMethods.set(result);
      if (result.length > 0) this.paidNowPaymentMethodId = result[0].id;
    } catch {
      // اختيارية - فشل تحميلها ما يوقف رفع الفاتورة نفسه، بس يعطّل خانة "دفعت الآن".
    }
  }

  onFileSelected(event: Event): void {
    const input = event.target as HTMLInputElement;
    this.selectedFile = input.files?.[0] ?? null;
  }

  async upload(): Promise<void> {
    if (!this.selectedBranchId || !this.selectedFile) {
      this.errorMessage.set('اختر الفرع والصورة أولًا.');
      return;
    }

    if (this.paidNow && (!this.paidNowAmount || this.paidNowAmount <= 0 || !this.paidNowPaymentMethodId)) {
      this.errorMessage.set('حدّد مبلغًا موجبًا وطريقة الدفع لو دفعت للمورد الآن.');
      return;
    }

    this.uploading.set(true);
    this.errorMessage.set(null);
    this.successMessage.set(null);

    try {
      const formData = new FormData();
      formData.append('file', this.selectedFile);

      const queryParams: Record<string, string> = { branchId: this.selectedBranchId };
      if (this.paidNow && this.paidNowAmount && this.paidNowPaymentMethodId) {
        queryParams['paidNowAmount'] = String(this.paidNowAmount);
        queryParams['paidNowPaymentMethodId'] = this.paidNowPaymentMethodId;
      }

      await firstValueFrom(
        this.apiClient.post(ApiController.PurchaseInvoices, PurchaseInvoiceDraftsOperation.CreateFromImage, formData, undefined, queryParams)
      );

      this.successMessage.set(
        this.paidNow
          ? 'تم رفع الفاتورة وقراءتها، وسُجّل المبلغ المدفوع بدرج الكاش فورًا - بانتظار مراجعة الإدارة لاعتماد الفاتورة نفسها.'
          : 'تم رفع الفاتورة وقراءتها - بانتظار مراجعة الإدارة قبل اعتمادها.'
      );
      this.selectedFile = null;
      this.paidNow = false;
      this.paidNowAmount = null;
    } catch (err: unknown) {
      const message =
        err && typeof err === 'object' && 'error' in err
          ? (err as { error?: { detail?: string } }).error?.detail
          : null;
      this.errorMessage.set(message ?? 'تعذّرت قراءة الفاتورة آليًا - جرّب صورة أوضح.');
    } finally {
      this.uploading.set(false);
    }
  }
}
