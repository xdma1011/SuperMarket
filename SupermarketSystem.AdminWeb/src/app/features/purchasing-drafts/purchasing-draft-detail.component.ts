import { Component, OnInit, signal } from '@angular/core';
import { CommonModule } from '@angular/common';
import { FormsModule } from '@angular/forms';
import { ActivatedRoute, Router } from '@angular/router';
import { firstValueFrom } from 'rxjs';
import { ApiClient } from '../../core/api/api-client.service';
import { ApiController } from '../../core/api/api-controller.enum';
import { PurchaseInvoiceDraftsOperation, SuppliersOperation, ProductsOperation, PaymentMethodsOperation } from '../../core/api/operations';
import { environment } from '../../../environments/environment';
import { AuthService } from '../../core/services/auth.service';

interface PurchaseInvoiceDraftItemDto {
  rawProductName: string;
  quantity: number;
  unitOfMeasure: string | null;
  unitCost: number | null;
  lineTotal: number | null;
  matchedProductId: string | null;
  matchedProductName: string | null;
  matchedProductUnitId: string | null;
  isBatchTracked: boolean;
  newBatchNumber: string | null;
  newBatchExpiryDate: string | null;
}

interface PurchaseInvoiceDraftDetailDto {
  id: string;
  branchId: string;
  imageReference: string;
  providerName: string | null;
  rawSupplierName: string | null;
  matchedSupplierId: string | null;
  supplierInvoiceReference: string | null;
  invoiceDate: string | null;
  currency: string | null;
  extractedInvoiceTotal: number | null;
  extractionConfidence: string | null;
  warnings: string[];
  items: PurchaseInvoiceDraftItemDto[];
  status: number;
  resultingPurchaseInvoiceId: string | null;
  paidNowAmount: number | null;
  paidNowPaymentMethodId: string | null;
}

interface SupplierDto {
  id: string;
  name: string;
}

interface PaymentMethodDto {
  id: string;
  name: string;
}

interface ProductSearchResultDto {
  id: string;
  name: string;
  isBatchTracked: boolean;
}

interface ProductUnitDto {
  id: string;
  unitName: string;
  isBaseUnit: boolean;
}

/** سطر مع حالة بحث المطابقة المحلية (بلا علاقة بشكل الـDTO المرسَل للسيرفر) */
interface EditableItem extends PurchaseInvoiceDraftItemDto {
  searchTerm: string;
  searchResults: ProductSearchResultDto[];
  searching: boolean;
  barcodeInput: string;
}

/**
 * شاشة مراجعة مسودة فاتورة واحدة - القلب الحقيقي للميزة. AI ما بيعرف
 * IDs المنتجات الفعلية بالـDB (CLAUDE.md context)، فكل سطر ما انطابق
 * تلقائيًا (matchedProductId فاضي) لازم المراجع يبحث ويختار يدويًا، إما
 * بالاسم أو بالباركود - المطابقة التلقائية (الباك إند) بس بداية، لا
 * ضمان.
 */
@Component({
  selector: 'app-purchasing-draft-detail',
  standalone: true,
  imports: [CommonModule, FormsModule],
  templateUrl: './purchasing-draft-detail.component.html',
  styleUrl: './purchasing-draft-detail.component.css'
})
export class PurchasingDraftDetailComponent implements OnInit {
  readonly draft = signal<PurchaseInvoiceDraftDetailDto | null>(null);
  readonly suppliers = signal<SupplierDto[]>([]);
  readonly paymentMethods = signal<PaymentMethodDto[]>([]);
  readonly imageUrl = signal<string | null>(null);
  readonly loading = signal(true);
  readonly errorMessage = signal<string | null>(null);
  readonly actionMessage = signal<string | null>(null);
  readonly saving = signal(false);
  readonly completing = signal(false);
  readonly discarding = signal(false);

  matchedSupplierId = '';
  supplierInvoiceReference = '';
  items: EditableItem[] = [];

  private draftId = '';

  constructor(
    private readonly apiClient: ApiClient,
    private readonly route: ActivatedRoute,
    private readonly router: Router,
    private readonly authService: AuthService
  ) {}

  ngOnInit(): void {
    this.draftId = this.route.snapshot.paramMap.get('id') ?? '';
    this.loadAll();
  }

  private async loadAll(): Promise<void> {
    this.loading.set(true);
    this.errorMessage.set(null);

    try {
      const [draftResult, suppliersResult, paymentMethodsResult] = await Promise.all([
        firstValueFrom(
          this.apiClient.get<PurchaseInvoiceDraftDetailDto>(
            ApiController.PurchaseInvoices, PurchaseInvoiceDraftsOperation.GetById, { draftId: this.draftId })
        ),
        firstValueFrom(
          this.apiClient.get<{ items: SupplierDto[] }>(ApiController.Suppliers, SuppliersOperation.List, undefined, { pageSize: 500 })
        ),
        firstValueFrom(this.apiClient.get<PaymentMethodDto[]>(ApiController.PaymentMethods, PaymentMethodsOperation.List))
      ]);

      this.draft.set(draftResult);
      this.suppliers.set(suppliersResult.items);
      this.paymentMethods.set(paymentMethodsResult);
      this.matchedSupplierId = draftResult.matchedSupplierId ?? '';
      this.supplierInvoiceReference = draftResult.supplierInvoiceReference ?? '';
      this.items = draftResult.items.map(i => ({
        ...i,
        searchTerm: '',
        searchResults: [],
        searching: false,
        barcodeInput: ''
      }));

      await this.loadImage();
    } catch {
      this.errorMessage.set('تعذّر تحميل مسودة الفاتورة.');
    } finally {
      this.loading.set(false);
    }
  }

  private async loadImage(): Promise<void> {
    try {
      const token = this.authService.accessToken();
      const url = `${environment.apiBaseUrl}/purchase-invoices/drafts/${this.draftId}/image`;
      const response = await fetch(url, { headers: token ? { Authorization: `Bearer ${token}` } : {} });
      if (!response.ok) return;
      const blob = await response.blob();
      this.imageUrl.set(window.URL.createObjectURL(blob));
    } catch {
      // الصورة اختيارية للعرض - فشل جلبها لا يوقف المراجعة نفسها.
    }
  }

  supplierNameOf(id: string): string {
    return this.suppliers().find(s => s.id === id)?.name ?? '—';
  }

  paymentMethodNameOf(id: string | null): string {
    if (!id) return '—';
    return this.paymentMethods().find(m => m.id === id)?.name ?? '—';
  }

  get unmatchedCount(): number {
    return this.items.filter(i => !i.matchedProductId).length;
  }

  async searchProduct(item: EditableItem): Promise<void> {
    if (item.searchTerm.trim().length < 2) {
      item.searchResults = [];
      return;
    }

    item.searching = true;
    try {
      const result = await firstValueFrom(
        this.apiClient.get<{ items: ProductSearchResultDto[] }>(
          ApiController.Products, ProductsOperation.List, undefined, { search: item.searchTerm.trim(), pageSize: 8 })
      );
      item.searchResults = result.items;
    } catch {
      item.searchResults = [];
    } finally {
      item.searching = false;
    }
  }

  async searchBarcode(item: EditableItem): Promise<void> {
    if (!item.barcodeInput.trim()) return;

    try {
      const product = await firstValueFrom(
        this.apiClient.get<ProductSearchResultDto>(
          ApiController.Products, ProductsOperation.GetByBarcode, { barcodeValue: item.barcodeInput.trim() })
      );
      await this.selectProduct(item, product);
      item.barcodeInput = '';
    } catch {
      this.errorMessage.set(`لم يُعثر على منتج بالباركود "${item.barcodeInput}".`);
    }
  }

  async selectProduct(item: EditableItem, product: ProductSearchResultDto): Promise<void> {
    item.matchedProductId = product.id;
    item.matchedProductName = product.name;
    item.isBatchTracked = product.isBatchTracked;
    item.searchTerm = '';
    item.searchResults = [];

    try {
      const units = await firstValueFrom(
        this.apiClient.get<ProductUnitDto[]>(ApiController.Products, ProductsOperation.GetUnits, { productId: product.id })
      );
      const baseUnit = units.find(u => u.isBaseUnit) ?? units[0];
      item.matchedProductUnitId = baseUnit?.id ?? null;
    } catch {
      item.matchedProductUnitId = null;
    }
  }

  clearMatch(item: EditableItem): void {
    item.matchedProductId = null;
    item.matchedProductName = null;
    item.matchedProductUnitId = null;
    item.isBatchTracked = false;
  }

  private buildItemsPayload(): PurchaseInvoiceDraftItemDto[] {
    return this.items.map(i => ({
      rawProductName: i.rawProductName,
      quantity: i.quantity,
      unitOfMeasure: i.unitOfMeasure,
      unitCost: i.unitCost,
      lineTotal: i.lineTotal,
      matchedProductId: i.matchedProductId,
      matchedProductName: i.matchedProductName,
      matchedProductUnitId: i.matchedProductUnitId,
      isBatchTracked: i.isBatchTracked,
      newBatchNumber: i.newBatchNumber,
      newBatchExpiryDate: i.newBatchExpiryDate
    }));
  }

  async save(): Promise<boolean> {
    this.saving.set(true);
    this.errorMessage.set(null);
    this.actionMessage.set(null);

    try {
      await firstValueFrom(
        this.apiClient.put(
          ApiController.PurchaseInvoices,
          PurchaseInvoiceDraftsOperation.Update,
          {
            matchedSupplierId: this.matchedSupplierId || null,
            supplierInvoiceReference: this.supplierInvoiceReference.trim() || null,
            items: this.buildItemsPayload()
          },
          { draftId: this.draftId }
        )
      );
      this.actionMessage.set('تم حفظ التعديلات.');
      return true;
    } catch (err: unknown) {
      this.errorMessage.set(this.extractError(err) ?? 'تعذّر حفظ التعديلات.');
      return false;
    } finally {
      this.saving.set(false);
    }
  }

  async complete(): Promise<void> {
    const missingBatch = this.items.find(i => i.isBatchTracked && !i.newBatchNumber?.trim());
    if (missingBatch) {
      this.errorMessage.set(`الصنف "${missingBatch.matchedProductName}" يتتبّع دفعات - أدخل رقم الدفعة له قبل الاعتماد.`);
      return;
    }

    const saved = await this.save();
    if (!saved) return;

    this.completing.set(true);
    this.errorMessage.set(null);

    try {
      await firstValueFrom(
        this.apiClient.post(ApiController.PurchaseInvoices, PurchaseInvoiceDraftsOperation.Complete, {}, { draftId: this.draftId })
      );
      await this.router.navigate(['/purchases']);
    } catch (err: unknown) {
      this.errorMessage.set(this.extractError(err) ?? 'تعذّر اعتماد الفاتورة.');
    } finally {
      this.completing.set(false);
    }
  }

  async discard(): Promise<void> {
    this.discarding.set(true);
    this.errorMessage.set(null);

    try {
      await firstValueFrom(
        this.apiClient.delete(ApiController.PurchaseInvoices, PurchaseInvoiceDraftsOperation.Discard, { draftId: this.draftId })
      );
      await this.router.navigate(['/purchases/drafts']);
    } catch {
      this.errorMessage.set('تعذّر تجاهل المسودة.');
    } finally {
      this.discarding.set(false);
    }
  }

  private extractError(err: unknown): string | null {
    return err && typeof err === 'object' && 'error' in err
      ? (err as { error?: { detail?: string } }).error?.detail ?? null
      : null;
  }
}
