import { Component, OnDestroy, OnInit, computed, inject, signal } from '@angular/core';
import { CommonModule } from '@angular/common';
import { FormsModule } from '@angular/forms';
import { firstValueFrom } from 'rxjs';
import { ApiClient } from '../../core/api/api-client.service';
import { ApiController } from '../../core/api/api-controller.enum';
import { PaymentMethodsOperation, PreparedOrdersOperation, SaleAssistOperation } from '../../core/api/operations';
import { AuthService, PublicBranchDto } from '../../core/services/auth.service';
import { PermissionsService } from '../../core/services/permissions.service';
import { BarcodeScannerComponent } from '../catalog/barcode-scanner/barcode-scanner.component';

export type QuickScanMode = 'prepare' | 'at-cost';

export interface ScanLookupItem {
  productId: string;
  productUnitId: string;
  productName: string;
  unitName: string;
  unitPrice: number;
  isBatchTracked: boolean;
  barcode: string | null;
}

export interface ScanCartLine {
  productId: string;
  productUnitId: string;
  productName: string;
  unitName: string;
  unitPrice: number;
  quantity: number;
  /** بوضع السحب بسعر التكلفة بس - من عرض السعر (null = ما إله تكلفة معروفة). */
  unitCost?: number | null;
}

interface PreparedOrderResponse {
  preparedOrderId: string;
  ticketNumber: number;
  itemCount: number;
  estimatedTotal: number;
}

export interface OpenPreparedOrder {
  id: string;
  ticketNumber: number;
  note: string | null;
  preparedByName: string;
  createdAtUtc: string;
  estimatedTotal: number;
  items: { productName: string; unitName: string; quantity: number }[];
}

interface AtCostQuoteLine {
  productId: string;
  productUnitId: string;
  productBatchId: string | null;
  unitCost: number | null;
  lineTotal: number | null;
}

interface AtCostQuote {
  lines: AtCostQuoteLine[];
  total: number;
  sellingValue: number;
  missingCost: string[];
}

interface AtCostResponse {
  saleInvoiceId: string;
  invoiceNumber: string;
  totalAmount: number;
  deductFromShare: boolean;
}

interface PaymentMethodDto {
  id: string;
  name: string;
  requiresExternalReference: boolean;
}

/** نفس الباركود مرتين خلال هالمدة من الكاميرا = قراءة وحدة (الكاميرا بتقرأ نفس الكود كذا مرة بالثانية). */
const CAMERA_REPEAT_MS = 1800;

/**
 * صفحة التلفون (28/9/2026) - بتنفتح من تلفون المساعد/الشريك كأنها لوحة الإدارة العادية:
 *
 * - "تجهيز طلب للكاشير" (Sales.Create): بيحط الأغراض بالكيس وبيضربها، بيكبس إرسال، وبيطلعله رقم طلب.
 *   الكاشير بينزّل الطلب بالسلة من زر "طلبات جاهزة" وبيحاسب هو على المصاري الحقيقية.
 * - "سحب بسعر التكلفة" (Sales.AtCostWithdrawal): صاحب المحل/الشريك بياخد بضاعة بالتكلفة - يا بيدفع
 *   حقها للصندوق، يا "اخصمها مني" (بتنسجّل عليه). منتج بلا تكلفة معروفة مرفوض.
 *
 * الضرب: خانة (ماسح بلوتوث/كتابة + Enter) أو الكاميرا (بدها https بالمتصفح). باركود = الوحدة
 * بالضبط، كلمة = بحث بالاسم.
 */
@Component({
  selector: 'app-quick-scan',
  standalone: true,
  imports: [CommonModule, FormsModule, BarcodeScannerComponent],
  templateUrl: './quick-scan.component.html',
  styleUrl: './quick-scan.component.css'
})
export class QuickScanComponent implements OnInit, OnDestroy {
  private readonly apiClient = inject(ApiClient);
  private readonly auth = inject(AuthService);
  private readonly permissions = inject(PermissionsService);

  readonly mode = signal<QuickScanMode>('prepare');
  readonly canWithdrawAtCost = computed(() => this.permissions.has('Sales.AtCostWithdrawal'));

  readonly branches = signal<PublicBranchDto[]>([]);
  branchId = '';

  term = '';
  note = '';
  readonly lines = signal<ScanCartLine[]>([]);
  readonly choices = signal<ScanLookupItem[]>([]);
  readonly looking = signal(false);
  readonly scannerOpen = signal(false);

  readonly submitting = signal(false);
  readonly errorMessage = signal<string | null>(null);
  readonly successMessage = signal<string | null>(null);
  readonly lastTicket = signal<PreparedOrderResponse | null>(null);
  readonly openOrders = signal<OpenPreparedOrder[]>([]);

  // سحب بسعر التكلفة
  readonly quote = signal<AtCostQuote | null>(null);
  readonly quoting = signal(false);
  readonly paymentMethods = signal<PaymentMethodDto[]>([]);
  settlement: 'pay' | 'deduct' = 'pay';
  paymentMethodId = '';

  readonly itemCount = computed(() => this.lines().reduce((sum, l) => sum + l.quantity, 0));
  readonly estimatedTotal = computed(() => this.lines().reduce((sum, l) => sum + l.quantity * l.unitPrice, 0));

  private lastCameraCode = '';
  private lastCameraAt = 0;
  private quoteTimer: ReturnType<typeof setTimeout> | null = null;

  ngOnInit(): void {
    void this.loadBranches();
  }

  ngOnDestroy(): void {
    if (this.quoteTimer) clearTimeout(this.quoteTimer);
  }

  private async loadBranches(): Promise<void> {
    try {
      const branches = await this.auth.getPublicBranches();
      this.branches.set(branches);
      this.branchId = this.auth.defaultBranchId(branches);
    } catch {
      this.branchId = this.auth.currentBranchId() ?? '';
    }
    await this.loadOpenOrders();
  }

  setMode(mode: QuickScanMode): void {
    if (mode === this.mode()) return;
    this.mode.set(mode);
    this.clearMessages();
    this.lastTicket.set(null);
    if (mode === 'at-cost') {
      void this.loadPaymentMethods();
      this.scheduleQuote();
    } else {
      this.quote.set(null);
    }
  }

  async onBranchChange(): Promise<void> {
    this.clearMessages();
    this.scheduleQuote();
    await this.loadOpenOrders();
  }

  async onSubmitTerm(): Promise<void> {
    const term = this.term.trim();
    if (!term) return;
    await this.lookupAndAdd(term);
  }

  onCameraScanned(code: string): void {
    const now = Date.now();
    if (code === this.lastCameraCode && now - this.lastCameraAt < CAMERA_REPEAT_MS) return;
    this.lastCameraCode = code;
    this.lastCameraAt = now;
    navigator.vibrate?.(60);
    void this.lookupAndAdd(code);
  }

  private async lookupAndAdd(term: string): Promise<void> {
    if (!this.branchId) {
      this.errorMessage.set('ما في فرع محدَّد.');
      return;
    }

    this.looking.set(true);
    this.clearMessages();
    this.choices.set([]);
    try {
      const results = await firstValueFrom(
        this.apiClient.get<ScanLookupItem[]>(ApiController.Sales, SaleAssistOperation.ScanLookup, undefined, {
          branchId: this.branchId,
          term
        })
      );

      if (results.length === 0) {
        this.errorMessage.set(`ما لقيت إشي لـ"${term}" بهالفرع.`);
      } else if (results.length === 1) {
        this.addItem(results[0]);
        this.term = '';
      } else {
        this.choices.set(results);
      }
    } catch (err) {
      this.errorMessage.set(this.errorDetail(err) ?? 'تعذّر البحث.');
    } finally {
      this.looking.set(false);
    }
  }

  addItem(item: ScanLookupItem): void {
    this.choices.set([]);
    this.term = '';
    this.lastTicket.set(null);
    const existing = this.lines().find(l => l.productUnitId === item.productUnitId);
    if (existing) {
      this.setQuantity(existing, existing.quantity + 1);
      return;
    }
    this.lines.update(lines => [
      ...lines,
      {
        productId: item.productId,
        productUnitId: item.productUnitId,
        productName: item.productName,
        unitName: item.unitName,
        unitPrice: item.unitPrice,
        quantity: 1
      }
    ]);
    this.scheduleQuote();
  }

  setQuantity(line: ScanCartLine, quantity: number): void {
    const value = Math.round(Number(quantity) * 1000) / 1000;
    if (!Number.isFinite(value) || value <= 0) {
      this.removeLine(line);
      return;
    }
    this.lines.update(lines => lines.map(l => (l.productUnitId === line.productUnitId ? { ...l, quantity: value } : l)));
    this.scheduleQuote();
  }

  removeLine(line: ScanCartLine): void {
    this.lines.update(lines => lines.filter(l => l.productUnitId !== line.productUnitId));
    this.scheduleQuote();
  }

  clearCart(): void {
    this.lines.set([]);
    this.note = '';
    this.quote.set(null);
    this.choices.set([]);
  }

  // ===== تجهيز طلب للكاشير =====

  async sendToCashier(): Promise<void> {
    if (this.lines().length === 0) {
      this.errorMessage.set('السلة فاضية.');
      return;
    }

    this.submitting.set(true);
    this.clearMessages();
    try {
      const response = await firstValueFrom(
        this.apiClient.post<PreparedOrderResponse>(ApiController.PreparedOrders, PreparedOrdersOperation.Create, {
          branchId: this.branchId,
          note: this.note.trim() || null,
          items: this.lines().map(l => ({ productId: l.productId, productUnitId: l.productUnitId, quantity: l.quantity }))
        })
      );
      this.lastTicket.set(response);
      this.clearCart();
      await this.loadOpenOrders();
    } catch (err) {
      this.errorMessage.set(this.errorDetail(err) ?? 'تعذّر إرسال الطلب.');
    } finally {
      this.submitting.set(false);
    }
  }

  async loadOpenOrders(): Promise<void> {
    if (!this.branchId) return;
    try {
      const orders = await firstValueFrom(
        this.apiClient.get<OpenPreparedOrder[]>(ApiController.PreparedOrders, PreparedOrdersOperation.ListOpen, undefined, {
          branchId: this.branchId
        })
      );
      this.openOrders.set(orders);
    } catch {
      this.openOrders.set([]);
    }
  }

  async cancelOrder(order: OpenPreparedOrder): Promise<void> {
    if (!confirm(`إلغاء الطلب رقم ${order.ticketNumber}؟`)) return;
    this.clearMessages();
    try {
      await firstValueFrom(this.apiClient.post(ApiController.PreparedOrders, PreparedOrdersOperation.Cancel, {}, { id: order.id }));
      if (this.lastTicket()?.preparedOrderId === order.id) this.lastTicket.set(null);
      await this.loadOpenOrders();
    } catch (err) {
      this.errorMessage.set(this.errorDetail(err) ?? 'تعذّر إلغاء الطلب.');
      await this.loadOpenOrders();
    }
  }

  // ===== سحب بسعر التكلفة =====

  private async loadPaymentMethods(): Promise<void> {
    if (this.paymentMethods().length > 0) return;
    try {
      const methods = await firstValueFrom(
        this.apiClient.get<PaymentMethodDto[]>(ApiController.PaymentMethods, PaymentMethodsOperation.List)
      );
      // طرق بلا رقم مرجعي بس (كاش) - الفيزا بدها رقم عملية ما إله معنى هون.
      const usable = methods.filter(m => !m.requiresExternalReference);
      this.paymentMethods.set(usable);
      if (!this.paymentMethodId && usable.length > 0) this.paymentMethodId = usable[0].id;
    } catch {
      this.paymentMethods.set([]);
    }
  }

  private scheduleQuote(): void {
    if (this.mode() !== 'at-cost') return;
    if (this.quoteTimer) clearTimeout(this.quoteTimer);
    this.quoteTimer = setTimeout(() => void this.refreshQuote(), 350);
  }

  async refreshQuote(): Promise<void> {
    if (this.mode() !== 'at-cost') return;
    if (this.lines().length === 0) {
      this.quote.set(null);
      return;
    }

    this.quoting.set(true);
    try {
      const quote = await firstValueFrom(
        this.apiClient.post<AtCostQuote>(ApiController.Sales, SaleAssistOperation.AtCostQuote, {
          branchId: this.branchId,
          items: this.lines().map(l => ({ productId: l.productId, productUnitId: l.productUnitId, quantity: l.quantity }))
        })
      );
      this.quote.set(quote);
      this.lines.update(lines =>
        lines.map(l => ({
          ...l,
          unitCost: quote.lines.find(q => q.productUnitId === l.productUnitId)?.unitCost ?? null
        }))
      );
    } catch (err) {
      this.quote.set(null);
      this.errorMessage.set(this.errorDetail(err) ?? 'تعذّر حساب التكلفة.');
    } finally {
      this.quoting.set(false);
    }
  }

  readonly canConfirmAtCost = computed(() => {
    const quote = this.quote();
    return !!quote && quote.missingCost.length === 0 && this.lines().length > 0 && !this.quoting();
  });

  async confirmAtCost(): Promise<void> {
    const quote = this.quote();
    if (!quote || quote.missingCost.length > 0) return;
    if (this.settlement === 'pay' && !this.paymentMethodId) {
      this.errorMessage.set('اختار طريقة الدفع.');
      return;
    }

    const how = this.settlement === 'pay' ? 'بتدفع حقها' : 'تنخصم منك';
    if (!confirm(`سحب ${this.itemCount()} قطعة بسعر التكلفة ${quote.total.toFixed(3)} د.أ (${how})؟`)) return;

    this.submitting.set(true);
    this.clearMessages();
    try {
      const response = await firstValueFrom(
        this.apiClient.post<AtCostResponse>(ApiController.Sales, SaleAssistOperation.AtCostComplete, {
          branchId: this.branchId,
          clientRequestId: crypto.randomUUID(),
          deductFromShare: this.settlement === 'deduct',
          paymentMethodId: this.settlement === 'pay' ? this.paymentMethodId : null,
          items: quote.lines.map(q => ({
            productId: q.productId,
            productUnitId: q.productUnitId,
            quantity: this.lines().find(l => l.productUnitId === q.productUnitId)?.quantity ?? 0,
            productBatchId: q.productBatchId
          }))
        })
      );
      this.successMessage.set(
        response.deductFromShare
          ? `✓ انسجّل السحب (${response.invoiceNumber}) بـ${response.totalAmount.toFixed(3)} د.أ - عليك لحد ما ينخصم من نصيبك.`
          : `✓ انسجّل السحب (${response.invoiceNumber}) - حط ${response.totalAmount.toFixed(3)} د.أ بالصندوق.`
      );
      this.clearCart();
    } catch (err) {
      this.errorMessage.set(this.errorDetail(err) ?? 'تعذّر تسجيل السحب.');
    } finally {
      this.submitting.set(false);
    }
  }

  // ===== مساعدات =====

  private clearMessages(): void {
    this.errorMessage.set(null);
    this.successMessage.set(null);
  }

  private errorDetail(err: unknown): string | null {
    return err && typeof err === 'object' && 'error' in err
      ? ((err as { error?: { detail?: string } }).error?.detail ?? null)
      : null;
  }

  timeOf(utc: string): string {
    return new Date(utc.endsWith('Z') ? utc : utc + 'Z').toLocaleTimeString('ar-JO', { hour: '2-digit', minute: '2-digit' });
  }
}
