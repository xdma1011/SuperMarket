import { Component, signal } from '@angular/core';
import { CommonModule } from '@angular/common';
import { FormsModule } from '@angular/forms';
import { firstValueFrom } from 'rxjs';
import { ApiClient } from '../../core/api/api-client.service';
import { ApiController } from '../../core/api/api-controller.enum';
import { SalesOperation, ReturnsOperation, PaymentMethodsOperation } from '../../core/api/operations';

interface SaleInvoiceListItemDto {
  id: string;
  invoiceNumber: string;
  statusCode: number;
  statusTitle: string;
  totalAmount: number;
  totalReturnedAmount: number;
  createdAtUtc: string;
}

interface SaleInvoiceItemDetailDto {
  saleInvoiceItemId: string;
  productId: string;
  productName: string;
  quantity: number;
  quantityReturned: number;
  unitPriceSnapshot: number;
  lineTotal: number;
}

interface SaleInvoiceDetailDto {
  id: string;
  invoiceNumber: string;
  statusCode: number;
  statusTitle: string;
  totalAmount: number;
  totalReturnedAmount: number;
  createdAtUtc: string;
  items: SaleInvoiceItemDetailDto[];
}

interface PaymentMethodDto {
  id: string;
  name: string;
  requiresExternalReference: boolean;
}

interface PagedResult<T> {
  items: T[];
  totalCount: number;
}

interface ReturnLine {
  saleInvoiceItemId: string;
  productName: string;
  maxReturnable: number;
  unitPrice: number;
  quantity: number;
}

/**
 * تدفّق الإرجاع: (1) دوّر عن الفاتورة الأصلية برقمها، (2) اختر الأصناف
 * والكميات المرتجعة (محدودة بـmaxReturnable = Quantity - QuantityReturned
 * لكل سطر، نفس القيد اللي الباك إند بيحرسه ذريًا)، (3) حدّد طريقة
 * الاسترجاع، (4) أرسل. الحد هون راحة تجربة بس — الحارس الفعلي دائمًا
 * بالباك إند (ISaleInvoiceOperations.TryRecordReturnedQuantityAsync).
 */
@Component({
  selector: 'app-returns',
  standalone: true,
  imports: [CommonModule, FormsModule],
  templateUrl: './returns.component.html',
  styleUrl: './returns.component.css'
})
export class ReturnsComponent {
  readonly searchQuery = signal('');
  readonly searchResults = signal<SaleInvoiceListItemDto[]>([]);
  readonly searching = signal(false);
  readonly errorMessage = signal<string | null>(null);

  readonly selectedInvoice = signal<SaleInvoiceDetailDto | null>(null);
  readonly returnLines = signal<ReturnLine[]>([]);

  readonly paymentMethods = signal<PaymentMethodDto[]>([]);
  selectedPaymentMethodId = '';
  notes = '';

  readonly submitting = signal(false);
  readonly submitError = signal<string | null>(null);
  readonly successMessage = signal<string | null>(null);

  constructor(private readonly apiClient: ApiClient) {
    this.loadPaymentMethods();
  }

  private async loadPaymentMethods(): Promise<void> {
    try {
      const methods = await firstValueFrom(
        this.apiClient.get<PaymentMethodDto[]>(ApiController.PaymentMethods, PaymentMethodsOperation.List)
      );
      this.paymentMethods.set(methods);
      if (methods.length > 0) {
        this.selectedPaymentMethodId = methods[0].id;
      }
    } catch {
      /* فشل تحميل طرق الدفع لا يمنع البحث عن فاتورة. */
    }
  }

  async search(): Promise<void> {
    const query = this.searchQuery().trim();
    if (!query) return;

    this.searching.set(true);
    this.errorMessage.set(null);
    this.selectedInvoice.set(null);

    try {
      const result = await firstValueFrom(
        this.apiClient.get<PagedResult<SaleInvoiceListItemDto>>(
          ApiController.Sales, SalesOperation.List, undefined, { search: query, pageSize: 10 }
        )
      );
      this.searchResults.set(result.items);
      if (result.items.length === 0) {
        this.errorMessage.set('لا توجد فاتورة بهذا الرقم.');
      }
    } catch {
      this.errorMessage.set('تعذّر البحث عن الفاتورة.');
    } finally {
      this.searching.set(false);
    }
  }

  async selectInvoice(invoiceId: string): Promise<void> {
    this.errorMessage.set(null);
    this.successMessage.set(null);

    try {
      const invoice = await firstValueFrom(
        this.apiClient.get<SaleInvoiceDetailDto>(ApiController.Sales, SalesOperation.GetById, { id: invoiceId })
      );

      this.selectedInvoice.set(invoice);
      this.searchResults.set([]);

      this.returnLines.set(
        invoice.items
          .filter(i => i.quantity - i.quantityReturned > 0)
          .map(i => ({
            saleInvoiceItemId: i.saleInvoiceItemId,
            productName: i.productName,
            maxReturnable: i.quantity - i.quantityReturned,
            unitPrice: i.unitPriceSnapshot,
            quantity: 0
          }))
      );
    } catch {
      this.errorMessage.set('تعذّر تحميل تفاصيل الفاتورة.');
    }
  }

  clearSelection(): void {
    this.selectedInvoice.set(null);
    this.returnLines.set([]);
    this.searchQuery.set('');
    this.searchResults.set([]);
  }

  get selectedTotal(): number {
    return this.returnLines().reduce((sum, l) => sum + l.quantity * l.unitPrice, 0);
  }

  get hasAnySelectedQuantity(): boolean {
    return this.returnLines().some(l => l.quantity > 0);
  }

  async submitReturn(): Promise<void> {
    const invoice = this.selectedInvoice();
    if (!invoice) return;

    const linesToReturn = this.returnLines().filter(l => l.quantity > 0);

    if (linesToReturn.length === 0) {
      this.submitError.set('حدّد كمية إرجاع لصنف واحد على الأقل.');
      return;
    }

    if (!this.selectedPaymentMethodId) {
      this.submitError.set('حدّد طريقة الاسترجاع.');
      return;
    }

    this.submitting.set(true);
    this.submitError.set(null);

    const returnTotal = linesToReturn.reduce((sum, l) => sum + l.quantity * l.unitPrice, 0);

    try {
      await firstValueFrom(
        this.apiClient.post(ApiController.Returns, ReturnsOperation.Process, {
          originalSaleInvoiceId: invoice.id,
          clientRequestId: crypto.randomUUID(),
          reason: 1,
          notes: this.notes.trim() || null,
          items: linesToReturn.map(l => ({
            saleInvoiceItemId: l.saleInvoiceItemId,
            quantity: l.quantity
          })),
          refunds: [{
            paymentMethodId: this.selectedPaymentMethodId,
            amount: returnTotal,
            externalReference: null,
            clientRequestId: crypto.randomUUID()
          }]
        })
      );

      this.successMessage.set('تم تسجيل الإرجاع بنجاح.');
      this.clearSelection();
      this.notes = '';
    } catch (err: unknown) {
      const message =
        err && typeof err === 'object' && 'error' in err
          ? (err as { error?: { detail?: string } }).error?.detail
          : null;
      this.submitError.set(message ?? 'تعذّر تسجيل الإرجاع.');
    } finally {
      this.submitting.set(false);
    }
  }
}
