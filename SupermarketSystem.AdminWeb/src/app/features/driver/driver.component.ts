import { Component, OnInit, signal } from '@angular/core';
import { CommonModule } from '@angular/common';
import { FormsModule } from '@angular/forms';
import { firstValueFrom } from 'rxjs';
import { ApiClient } from '../../core/api/api-client.service';
import { ApiController } from '../../core/api/api-controller.enum';
import { DriverOperation, PaymentMethodsOperation } from '../../core/api/operations';

interface MyDeliveryDto {
  orderId: string;
  customerName: string;
  customerPhone: string | null;
  deliveryNote: string | null;
  deliveryLatitude: number | null;
  deliveryLongitude: number | null;
  estimatedTotal: number;
  itemCount: number;
  createdAtUtc: string;
}

interface PaymentMethodDto {
  id: string;
  name: string;
}

/**
 * صفحة السائق الوحيدة - يشوفها فقط لأنه ما عنده أي صلاحية ثانية
 * (Orders.Deliver فقط)، فباقي القائمة الجانبية فاضية له تلقائيًا (راجع
 * PermissionsService/NAV_ITEMS). بعد تسجيل الدخول يُوجَّه هون مباشرة
 * (راجع LoginComponent).
 */
@Component({
  selector: 'app-driver',
  standalone: true,
  imports: [CommonModule, FormsModule],
  templateUrl: './driver.component.html',
  styleUrl: './driver.component.css'
})
export class DriverComponent implements OnInit {
  readonly deliveries = signal<MyDeliveryDto[]>([]);
  readonly paymentMethods = signal<PaymentMethodDto[]>([]);
  readonly loading = signal(true);
  readonly errorMessage = signal<string | null>(null);
  readonly actionMessage = signal<string | null>(null);
  readonly processingId = signal<string | null>(null);

  readonly payModalOpen = signal(false);
  payAmount: number | null = null;
  payPaymentMethodId = '';
  private targetDelivery: MyDeliveryDto | null = null;

  constructor(private readonly apiClient: ApiClient) {}

  ngOnInit(): void {
    this.loadAll();
  }

  private async loadAll(): Promise<void> {
    this.loading.set(true);
    this.errorMessage.set(null);

    try {
      const [deliveriesResult, paymentMethodsResult] = await Promise.all([
        firstValueFrom(this.apiClient.get<MyDeliveryDto[]>(ApiController.Driver, DriverOperation.MyDeliveries)),
        firstValueFrom(this.apiClient.get<PaymentMethodDto[]>(ApiController.PaymentMethods, PaymentMethodsOperation.List))
      ]);

      this.deliveries.set(deliveriesResult);
      this.paymentMethods.set(paymentMethodsResult);
      if (paymentMethodsResult.length > 0) this.payPaymentMethodId = paymentMethodsResult[0].id;
    } catch {
      this.errorMessage.set('تعذّر تحميل طلباتك.');
    } finally {
      this.loading.set(false);
    }
  }

  mapUrl(delivery: MyDeliveryDto): string | null {
    if (delivery.deliveryLatitude == null || delivery.deliveryLongitude == null) return null;
    return `https://www.openstreetmap.org/?mlat=${delivery.deliveryLatitude}&mlon=${delivery.deliveryLongitude}#map=17/${delivery.deliveryLatitude}/${delivery.deliveryLongitude}`;
  }

  openPayModal(delivery: MyDeliveryDto): void {
    this.targetDelivery = delivery;
    this.payAmount = delivery.estimatedTotal;
    this.payModalOpen.set(true);
  }

  closePayModal(): void {
    this.payModalOpen.set(false);
    this.targetDelivery = null;
  }

  async confirmPay(): Promise<void> {
    if (!this.targetDelivery || !this.payAmount || this.payAmount <= 0 || !this.payPaymentMethodId) {
      this.errorMessage.set('حدّد مبلغًا موجبًا وطريقة الدفع.');
      return;
    }

    this.processingId.set(this.targetDelivery.orderId);
    this.errorMessage.set(null);

    try {
      await firstValueFrom(
        this.apiClient.post(
          ApiController.Driver,
          DriverOperation.Complete,
          {
            paymentMethodId: this.payPaymentMethodId,
            amountCollected: this.payAmount,
            clientRequestId: crypto.randomUUID()
          },
          { orderId: this.targetDelivery.orderId }
        )
      );
      this.actionMessage.set('تم تسجيل التسليم والدفعة بنجاح.');
      this.closePayModal();
      await this.loadAll();
    } catch (err: unknown) {
      const message =
        err && typeof err === 'object' && 'error' in err
          ? (err as { error?: { detail?: string } }).error?.detail
          : null;
      this.errorMessage.set(message ?? 'تعذّر تسجيل الدفعة.');
    } finally {
      this.processingId.set(null);
    }
  }
}
