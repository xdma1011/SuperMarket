import { Component, OnInit, signal } from '@angular/core';
import { CommonModule } from '@angular/common';
import { FormsModule } from '@angular/forms';
import { firstValueFrom } from 'rxjs';
import { ApiClient } from '../../core/api/api-client.service';
import { ApiController } from '../../core/api/api-controller.enum';
import { OrdersOperation, PaymentMethodsOperation } from '../../core/api/operations';

interface OrderListItemDto {
  id: string;
  customerId: string;
  customerName: string;
  customerPhone: string | null;
  branchId: string;
  status: number;
  deliveryNote: string | null;
  estimatedTotal: number;
  itemCount: number;
  createdAtUtc: string;
}

interface PagedResult<T> {
  items: T[];
  totalCount: number;
}

interface PaymentMethodDto {
  id: string;
  name: string;
}

/**
 * أساس تطبيق الزبائن (نقاش صاحب المشروع) - قائمة الطلبات من جهة
 * الكاشير. "قبول" ما بيخصم مخزون (بس يعلّم التزام بالتجهيز)، "إكمال"
 * (لحظة التسليم الفعلية) هو اللي بينشئ فاتورة بيع حقيقية ويخصم المخزون -
 * راجع تعليق Order.cs بالباك إند للسبب (Cash on Delivery).
 */
@Component({
  selector: 'app-orders',
  standalone: true,
  imports: [CommonModule, FormsModule],
  templateUrl: './orders.component.html',
  styleUrl: './orders.component.css'
})
export class OrdersComponent implements OnInit {
  readonly orders = signal<OrderListItemDto[]>([]);
  readonly paymentMethods = signal<PaymentMethodDto[]>([]);
  readonly loading = signal(true);
  readonly errorMessage = signal<string | null>(null);
  readonly actionMessage = signal<string | null>(null);
  readonly processingId = signal<string | null>(null);

  readonly rejectModalOpen = signal(false);
  readonly completeModalOpen = signal(false);
  rejectReason = '';
  completeAmount: number | null = null;
  completePaymentMethodId = '';
  private targetOrder: OrderListItemDto | null = null;

  constructor(private readonly apiClient: ApiClient) {}

  ngOnInit(): void {
    this.loadAll();
  }

  private async loadAll(): Promise<void> {
    this.loading.set(true);
    this.errorMessage.set(null);

    try {
      const [ordersResult, paymentMethodsResult] = await Promise.all([
        firstValueFrom(
          this.apiClient.get<PagedResult<OrderListItemDto>>(ApiController.Orders, OrdersOperation.List, undefined, { pageSize: 100 })
        ),
        firstValueFrom(this.apiClient.get<PaymentMethodDto[]>(ApiController.PaymentMethods, PaymentMethodsOperation.List))
      ]);

      this.orders.set(ordersResult.items);
      this.paymentMethods.set(paymentMethodsResult);
      if (paymentMethodsResult.length > 0) this.completePaymentMethodId = paymentMethodsResult[0].id;
    } catch {
      this.errorMessage.set('تعذّر تحميل الطلبات.');
    } finally {
      this.loading.set(false);
    }
  }

  statusLabel(status: number): string {
    switch (status) {
      case 1: return 'بانتظار القبول';
      case 2: return 'قيد التجهيز';
      case 3: return 'مكتمل';
      case 4: return 'مرفوض';
      default: return '—';
    }
  }

  async accept(order: OrderListItemDto): Promise<void> {
    this.processingId.set(order.id);
    this.errorMessage.set(null);
    this.actionMessage.set(null);

    try {
      await firstValueFrom(this.apiClient.post(ApiController.Orders, OrdersOperation.Accept, {}, { orderId: order.id }));
      this.actionMessage.set(`تم قبول الطلب - ${order.customerName}.`);
      await this.loadAll();
    } catch {
      this.errorMessage.set('تعذّر قبول الطلب.');
    } finally {
      this.processingId.set(null);
    }
  }

  openRejectModal(order: OrderListItemDto): void {
    this.targetOrder = order;
    this.rejectReason = '';
    this.rejectModalOpen.set(true);
  }

  closeRejectModal(): void {
    this.rejectModalOpen.set(false);
    this.targetOrder = null;
  }

  async confirmReject(): Promise<void> {
    if (!this.targetOrder || !this.rejectReason.trim()) {
      this.errorMessage.set('سبب الرفض إلزامي.');
      return;
    }

    this.processingId.set(this.targetOrder.id);
    this.errorMessage.set(null);

    try {
      await firstValueFrom(
        this.apiClient.post(ApiController.Orders, OrdersOperation.Reject, { reason: this.rejectReason.trim() }, { orderId: this.targetOrder.id })
      );
      this.actionMessage.set('تم رفض الطلب.');
      this.closeRejectModal();
      await this.loadAll();
    } catch {
      this.errorMessage.set('تعذّر رفض الطلب.');
    } finally {
      this.processingId.set(null);
    }
  }

  openCompleteModal(order: OrderListItemDto): void {
    this.targetOrder = order;
    this.completeAmount = order.estimatedTotal;
    this.completeModalOpen.set(true);
  }

  closeCompleteModal(): void {
    this.completeModalOpen.set(false);
    this.targetOrder = null;
  }

  async confirmComplete(): Promise<void> {
    if (!this.targetOrder || !this.completeAmount || this.completeAmount <= 0 || !this.completePaymentMethodId) {
      this.errorMessage.set('حدّد مبلغًا موجبًا وطريقة الدفع.');
      return;
    }

    this.processingId.set(this.targetOrder.id);
    this.errorMessage.set(null);

    try {
      await firstValueFrom(
        this.apiClient.post(
          ApiController.Orders,
          OrdersOperation.Complete,
          {
            payments: [{ paymentMethodId: this.completePaymentMethodId, amount: this.completeAmount, externalReference: null }],
            clientRequestId: crypto.randomUUID()
          },
          { orderId: this.targetOrder.id }
        )
      );
      this.actionMessage.set('تم إكمال الطلب - الفاتورة صدرت والمخزون انخصم.');
      this.closeCompleteModal();
      await this.loadAll();
    } catch (err: unknown) {
      const message =
        err && typeof err === 'object' && 'error' in err
          ? (err as { error?: { detail?: string } }).error?.detail
          : null;
      this.errorMessage.set(message ?? 'تعذّر إكمال الطلب.');
    } finally {
      this.processingId.set(null);
    }
  }
}
