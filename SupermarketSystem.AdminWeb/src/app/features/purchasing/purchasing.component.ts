import { Component, OnInit, signal, inject, computed } from '@angular/core';
import { CommonModule } from '@angular/common';
import { FormsModule } from '@angular/forms';
import { firstValueFrom, forkJoin } from 'rxjs';
import { ApiClient } from '../../core/api/api-client.service';
import { isRequestCancelled, latestRequest } from '../../core/api/latest-request';
import { ApiController } from '../../core/api/api-controller.enum';
import { PurchaseInvoicesOperation, ProductsOperation, SuppliersOperation, BranchesOperation, PaymentMethodsOperation } from '../../core/api/operations';
import { PaginationComponent } from '../../shared/components/pagination/pagination.component';
import { AuthService } from '../../core/services/auth.service';
import { PermissionsService } from '../../core/services/permissions.service';

interface PurchaseInvoiceListItemDto {
  id: string;
  invoiceNumber: string;
  supplierInvoiceReference: string | null;
  supplierName: string;
  status: number;
  totalAmount: number;
  totalPaidAmount: number;
  createdAtUtc: string;
  /** رصيد افتتاحي: بلا مورد ولا دين ولا دفعات. */
  isOpeningBalance?: boolean;
}

interface SupplierDto {
  id: string;
  name: string;
}

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

interface PaymentMethodDto {
  id: string;
  name: string;
}

interface DraftLine {
  productId: string;
  productName: string;
  unitId: string;
  units: ProductUnitDto[];
  quantity: number;
  unitCost: number;
  // منتج "يتتبّع دفعات" (IsBatchTracked) لازم رقم دفعة جديد لكل فاتورة
  // شراء — الباك إند يرفض السطر بلا هذا (PurchaseInvoice.BatchRequired).
  // تاريخ الصلاحية اختياري (مو كل منتج متتبَّع له تاريخ صلاحية فعلي).
  isBatchTracked: boolean;
  newBatchNumber: string;
  newBatchExpiryDate: string;
}

interface PagedResult<T> {
  items: T[];
  totalCount: number;
}

/**
 * أعقد صفحة بالدفعة — فاتورة شراء بأسطر متعددة، كل سطر يحتاج وحدة
 * المنتج الصحيحة (ProductUnitId)، لا اسم المنتج فقط. الوحدات تُجلَب عند
 * اختيار المنتج (endpoint منفصل، أضيف بنفس هالدفعة — كان ناقصًا بالكامل).
 */
@Component({
  selector: 'app-purchasing',
  standalone: true,
  imports: [CommonModule, FormsModule, PaginationComponent],
  templateUrl: './purchasing.component.html',
  styleUrl: './purchasing.component.css'
})
export class PurchasingComponent implements OnInit {
  private readonly loadRequest = latestRequest();
  private readonly auth = inject(AuthService);
  private readonly permissions = inject(PermissionsService);

  /** رصيد الافتتاح بيزيد مخزون بتكلفة بلا مورد - لصاحب المحل بس (Finance.Manage)، الباك إند بيفرضها كمان. */
  readonly canRecordOpeningBalance = computed(() => this.permissions.loaded() && this.permissions.has('Finance.Manage'));
  /** النموذج المفتوح هو "رصيد افتتاحي" مش فاتورة شراء عادية. */
  readonly openingMode = signal(false);

  readonly invoices = signal<PurchaseInvoiceListItemDto[]>([]);
  readonly totalCount = signal(0);
  readonly pageNumber = signal(1);
  readonly pageSize = signal(20);
  readonly suppliers = signal<SupplierDto[]>([]);
  readonly branches = signal<BranchDto[]>([]);
  readonly products = signal<ProductDto[]>([]);
  readonly loading = signal(true);
  readonly errorMessage = signal<string | null>(null);

  readonly formOpen = signal(false);
  readonly submitting = signal(false);

  // === modal تسجيل دفعة لمورد ===
  readonly paymentMethods = signal<PaymentMethodDto[]>([]);
  readonly paymentModalOpen = signal(false);
  readonly paymentSubmitting = signal(false);
  readonly paymentError = signal<string | null>(null);
  paymentTargetInvoice: PurchaseInvoiceListItemDto | null = null;
  paymentAmount: number | null = null;
  paymentMethodId = '';
  readonly formError = signal<string | null>(null);

  selectedSupplierId = '';
  selectedBranchId = '';
  supplierInvoiceReference = '';
  dueDate = '';
  lines: DraftLine[] = [];

  newLineProductId = '';

  constructor(private readonly apiClient: ApiClient) {}

  ngOnInit(): void {
    this.loadAll();
  }

  private async loadAll(): Promise<void> {
    this.loading.set(true);
    this.errorMessage.set(null);

    try {
      const [invoicesResult, suppliersResult, branchesResult, productsResult, paymentMethodsResult] = await this.loadRequest.run(forkJoin([
        this.apiClient.get<PagedResult<PurchaseInvoiceListItemDto>>(
          ApiController.PurchaseInvoices, PurchaseInvoicesOperation.List, undefined,
          { pageNumber: this.pageNumber(), pageSize: this.pageSize() }),
        // pageSize كبير عمدًا هون — هاي قوائم تعبئة Dropdown بالنموذج،
        // لا جداول معروضة، فالقصّ الافتراضي (20) كان رح يخفي موردين/فروع/
        // منتجات موجودة فعليًا بلا أي مؤشر للمستخدم إنها ناقصة.
        this.apiClient.get<PagedResult<SupplierDto>>(ApiController.Suppliers, SuppliersOperation.List, undefined, { pageSize: 500 }),
        this.apiClient.get<PagedResult<BranchDto>>(ApiController.Branches, BranchesOperation.List, undefined, { pageSize: 500 }),
        this.apiClient.get<PagedResult<ProductDto>>(ApiController.Products, ProductsOperation.List, undefined, { pageSize: 500 }),
        this.apiClient.get<PaymentMethodDto[]>(ApiController.PaymentMethods, PaymentMethodsOperation.List)
      ]));

      this.invoices.set(invoicesResult.items);
      this.totalCount.set(invoicesResult.totalCount);
      this.suppliers.set(suppliersResult.items);
      this.branches.set(branchesResult.items);
      this.products.set(productsResult.items);
      this.paymentMethods.set(paymentMethodsResult);
      if (paymentMethodsResult.length > 0) this.paymentMethodId = paymentMethodsResult[0].id;

      if (suppliersResult.items.length > 0) this.selectedSupplierId = suppliersResult.items[0].id;
      if (branchesResult.items.length > 0) this.selectedBranchId = this.auth.defaultBranchId(branchesResult.items);
      if (productsResult.items.length > 0) this.newLineProductId = productsResult.items[0].id;
    } catch (err) {
      if (isRequestCancelled(err)) return;
      this.errorMessage.set('تعذّر تحميل بيانات المشتريات.');
    } finally {
      this.loading.set(false);
    }
  }

  supplierNameOf(id: string): string {
    return this.suppliers().find(s => s.id === id)?.name ?? '—';
  }

  onPageChanged(event: { pageNumber: number; pageSize: number }): void {
    this.pageNumber.set(event.pageNumber);
    this.pageSize.set(event.pageSize);
    this.loadAll();
  }

  openForm(): void {
    this.openingMode.set(false);
    this.formOpen.set(true);
    this.formError.set(null);
    this.lines = [];
  }

  openOpeningForm(): void {
    this.openingMode.set(true);
    this.formOpen.set(true);
    this.formError.set(null);
    this.lines = [];
  }

  closeForm(): void {
    this.formOpen.set(false);
    this.openingMode.set(false);
    this.supplierInvoiceReference = '';
    this.dueDate = '';
    this.lines = [];
  }

  async addLine(): Promise<void> {
    if (!this.newLineProductId) return;

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

      this.lines = [
        ...this.lines,
        {
          productId: product.id,
          productName: product.name,
          unitId: baseUnit.id,
          units,
          quantity: 1,
          unitCost: 0,
          isBatchTracked: product.isBatchTracked,
          newBatchNumber: '',
          newBatchExpiryDate: ''
        }
      ];
    } catch {
      this.formError.set('تعذّر جلب وحدات المنتج.');
    }
  }

  removeLine(index: number): void {
    this.lines = this.lines.filter((_, i) => i !== index);
  }

  lineTotal(line: DraftLine): number {
    return (line.quantity || 0) * (line.unitCost || 0);
  }

  get invoiceTotal(): number {
    return this.lines.reduce((sum, l) => sum + this.lineTotal(l), 0);
  }

  async submit(): Promise<void> {
    const opening = this.openingMode();
    if ((!opening && !this.selectedSupplierId) || !this.selectedBranchId || this.lines.length === 0) {
      this.formError.set(opening ? 'حدّد الفرع، وأضف سطرًا واحدًا على الأقل.' : 'حدّد المورد والفرع، وأضف سطرًا واحدًا على الأقل.');
      return;
    }

    if (this.lines.some(l => l.quantity <= 0 || l.unitCost < 0)) {
      this.formError.set('كل سطر يحتاج كمية موجبة وتكلفة غير سالبة.');
      return;
    }

    const missingBatch = this.lines.find(l => l.isBatchTracked && !l.newBatchNumber.trim());
    if (missingBatch) {
      this.formError.set(`الصنف "${missingBatch.productName}" يتتبّع دفعات — أدخل رقم الدفعة له.`);
      return;
    }

    this.submitting.set(true);
    this.formError.set(null);

    try {
      const items = this.lines.map(l => ({
        productId: l.productId,
        productUnitId: l.unitId,
        quantity: l.quantity,
        unitCost: l.unitCost,
        existingProductBatchId: null,
        newBatchNumber: l.isBatchTracked ? l.newBatchNumber.trim() : null,
        newBatchExpiryDate: l.isBatchTracked && l.newBatchExpiryDate ? l.newBatchExpiryDate : null
      }));

      if (opening) {
        await firstValueFrom(
          this.apiClient.post(ApiController.PurchaseInvoices, PurchaseInvoicesOperation.OpeningBalance, {
            branchId: this.selectedBranchId,
            note: this.supplierInvoiceReference.trim() || null,
            items
          })
        );
      } else {
        await firstValueFrom(
          this.apiClient.post(ApiController.PurchaseInvoices, PurchaseInvoicesOperation.Complete, {
            branchId: this.selectedBranchId,
            supplierId: this.selectedSupplierId,
            supplierInvoiceReference: this.supplierInvoiceReference.trim() || null,
            dueDate: this.dueDate || null,
            items
          })
        );
      }

      this.closeForm();
      await this.loadAll();
    } catch (err: unknown) {
      const message =
        err && typeof err === 'object' && 'error' in err
          ? (err as { error?: { detail?: string } }).error?.detail
          : null;
      this.formError.set(message ?? 'تعذّر تسجيل فاتورة الشراء.');
    } finally {
      this.submitting.set(false);
    }
  }

  remainingDebt(invoice: PurchaseInvoiceListItemDto): number {
    // الرصيد الافتتاحي بلا مورد - ما عليه دين أبدًا.
    return invoice.isOpeningBalance ? 0 : invoice.totalAmount - invoice.totalPaidAmount;
  }

  openPaymentModal(invoice: PurchaseInvoiceListItemDto): void {
    this.paymentTargetInvoice = invoice;
    this.paymentAmount = this.remainingDebt(invoice);
    this.paymentError.set(null);
    this.paymentModalOpen.set(true);
  }

  closePaymentModal(): void {
    this.paymentModalOpen.set(false);
    this.paymentTargetInvoice = null;
    this.paymentAmount = null;
  }

  async submitPayment(): Promise<void> {
    const invoice = this.paymentTargetInvoice;
    if (!invoice || !this.paymentAmount || this.paymentAmount <= 0 || !this.paymentMethodId) {
      this.paymentError.set('حدّد مبلغًا موجبًا وطريقة دفع.');
      return;
    }

    if (this.paymentAmount > this.remainingDebt(invoice)) {
      this.paymentError.set('المبلغ أكبر من الدين المتبقي على هذه الفاتورة.');
      return;
    }

    this.paymentSubmitting.set(true);
    this.paymentError.set(null);

    try {
      await firstValueFrom(
        this.apiClient.post(
          ApiController.PurchaseInvoices,
          PurchaseInvoicesOperation.RecordPayment,
          {
            paymentMethodId: this.paymentMethodId,
            amount: this.paymentAmount,
            externalReference: null,
            clientRequestId: crypto.randomUUID()
          },
          { purchaseInvoiceId: invoice.id }
        )
      );

      this.closePaymentModal();
      await this.loadAll();
    } catch (err: unknown) {
      const message =
        err && typeof err === 'object' && 'error' in err
          ? (err as { error?: { detail?: string } }).error?.detail
          : null;
      this.paymentError.set(message ?? 'تعذّر تسجيل الدفعة.');
    } finally {
      this.paymentSubmitting.set(false);
    }
  }
}
