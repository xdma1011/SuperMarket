import { Component, OnInit, inject, signal } from '@angular/core';
import { CommonModule } from '@angular/common';
import { FormsModule } from '@angular/forms';
import { firstValueFrom } from 'rxjs';
import { ApiClient } from '../../core/api/api-client.service';
import { isRequestCancelled, latestRequest } from '../../core/api/latest-request';
import { ApiController } from '../../core/api/api-controller.enum';
import { CouponsOperation, CustomersOperation } from '../../core/api/operations';
import { BusinessTimeService } from '../../core/services/business-time.service';

export interface CouponDto {
  id: string;
  code: string;
  title: string;
  discountTypeCode: number;
  discountTypeTitle: string;
  value: number;
  maxDiscountAmount: number | null;
  minOrderAmount: number;
  startAtUtc: string;
  endAtUtc: string;
  customerId: string | null;
  customerName: string | null;
  customerPhone: string | null;
  maxUsesPerCustomer: number;
  maxTotalUses: number | null;
  isActive: boolean;
  lastSentAtUtc: string | null;
  reservedCount: number;
  redeemedCount: number;
  redeemedDiscountTotal: number;
  statusTitle: string;
}

interface CustomerOption {
  id: string;
  fullName: string;
  phone: string | null;
}

/**
 * كوبونات خصم تطبيق الزبائن (29/9/2026، "ضيفها عادي وانا اللي ببعثها يدوي او للكل"): كود لزبون معيّن أو للكل، مبلغ
 * ثابت أو نسبة بسقف، فترة وحدود استعمال، وإرسال يدوي (إشعار التطبيق + تلغرام). الزبون بيكتب الكود وقت الطلب؛
 * الخصم بينحسب على أسعار لحظة التسليم وبيطلع بالفاتورة كخصم فاتورة.
 */
@Component({
  selector: 'app-coupons',
  standalone: true,
  imports: [CommonModule, FormsModule],
  templateUrl: './coupons.component.html',
  styleUrl: './coupons.component.css'
})
export class CouponsComponent implements OnInit {
  private readonly listRequest = latestRequest();
  private readonly apiClient = inject(ApiClient);
  private readonly businessTime = inject(BusinessTimeService);

  readonly coupons = signal<CouponDto[]>([]);
  readonly customers = signal<CustomerOption[]>([]);
  readonly loading = signal(false);
  readonly submitting = signal(false);
  readonly errorMessage = signal<string | null>(null);
  readonly successMessage = signal<string | null>(null);

  readonly formOpen = signal(false);
  editingId: string | null = null;
  formCode = '';
  formTitle = '';
  formType: 'FixedAmount' | 'Percent' = 'FixedAmount';
  formValue: number | null = null;
  formMaxDiscount: number | null = null;
  formMinOrder: number | null = null;
  formStartDate = '';
  formEndDate = '';
  formAudience: 'all' | 'customer' = 'all';
  formCustomerId = '';
  formMaxUsesPerCustomer = 1;
  formMaxTotalUses: number | null = null;

  // الإرسال
  readonly sendTarget = signal<CouponDto | null>(null);
  sendMode: 'all' | 'customer' = 'all';
  sendCustomerId = '';

  async ngOnInit(): Promise<void> {
    await this.load();
  }

  async load(): Promise<void> {
    this.loading.set(true);
    try {
      this.coupons.set(await this.listRequest.run(this.apiClient.get<CouponDto[]>(ApiController.Coupons, CouponsOperation.List)));
    } catch (err) {
      if (isRequestCancelled(err)) return;
      this.errorMessage.set(this.errorDetail(err) ?? 'تعذّر تحميل الكوبونات.');
    } finally {
      this.loading.set(false);
    }
  }

  private async ensureCustomers(): Promise<void> {
    if (this.customers().length > 0) return;
    try {
      const result = await firstValueFrom(this.apiClient.get<{ items: CustomerOption[] }>(
        ApiController.Customers, CustomersOperation.List, undefined, { pageNumber: 1, pageSize: 500 }));
      this.customers.set(result.items);
    } catch {
      this.customers.set([]);
    }
  }

  describe(c: CouponDto): string {
    return c.discountTypeCode === 1
      ? `${c.value.toFixed(3)} د.أ`
      : `${c.value}%` + (c.maxDiscountAmount ? ` (لحد ${c.maxDiscountAmount.toFixed(3)} د.أ)` : '');
  }

  formatDate(value: string | null): string {
    return value ? this.businessTime.formatDateTime(value) : '—';
  }

  async openForm(coupon?: CouponDto): Promise<void> {
    this.clearMessages();
    this.editingId = coupon?.id ?? null;
    this.formCode = coupon?.code ?? '';
    this.formTitle = coupon?.title ?? '';
    this.formType = coupon?.discountTypeCode === 2 ? 'Percent' : 'FixedAmount';
    this.formValue = coupon?.value ?? null;
    this.formMaxDiscount = coupon?.maxDiscountAmount ?? null;
    this.formMinOrder = coupon?.minOrderAmount || null;
    const today = this.businessTime.localDate();
    this.formStartDate = coupon ? this.businessTime.localDate(new Date(coupon.startAtUtc)) : today;
    // النهاية مخزّنة بداية اليوم اللي بعدها - نعرض آخر يوم فعلي
    this.formEndDate = coupon
      ? this.businessTime.localDate(new Date(new Date(coupon.endAtUtc).getTime() - 1000))
      : this.businessTime.addDays(today, 30);
    this.formAudience = coupon?.customerId ? 'customer' : 'all';
    this.formCustomerId = coupon?.customerId ?? '';
    this.formMaxUsesPerCustomer = coupon?.maxUsesPerCustomer ?? 1;
    this.formMaxTotalUses = coupon?.maxTotalUses ?? null;
    this.formOpen.set(true);
    await this.ensureCustomers();
  }

  closeForm(): void {
    this.formOpen.set(false);
  }

  async save(): Promise<void> {
    const code = this.formCode.trim().toUpperCase();
    if (!this.editingId && !/^[A-Z0-9-]{4,20}$/.test(code)) {
      this.errorMessage.set('الكود من 4 لـ20 حرف: حروف إنجليزية وأرقام وشرطة بس (مثلًا EID-2026).');
      return;
    }
    if (!this.formTitle.trim()) {
      this.errorMessage.set('اكتب عنوان الكوبون (بيبين للزبون).');
      return;
    }
    if (!this.formValue || this.formValue <= 0 || (this.formType === 'Percent' && this.formValue > 100)) {
      this.errorMessage.set(this.formType === 'Percent' ? 'النسبة بين 0 و100.' : 'المبلغ لازم يكون أكبر من صفر.');
      return;
    }
    if (!this.formStartDate || !this.formEndDate || this.formEndDate < this.formStartDate) {
      this.errorMessage.set('تاريخ النهاية لازم يكون بعد (أو نفس) تاريخ البداية.');
      return;
    }
    if (!this.editingId && this.formAudience === 'customer' && !this.formCustomerId) {
      this.errorMessage.set('اختر الزبون.');
      return;
    }

    this.submitting.set(true);
    this.clearMessages();
    const body = {
      title: this.formTitle.trim(),
      discountType: this.formType,
      value: this.formValue,
      maxDiscountAmount: this.formType === 'Percent' ? this.formMaxDiscount || null : null,
      minOrderAmount: this.formMinOrder || 0,
      startAtUtc: this.businessTime.dayStartUtc(this.formStartDate),
      endAtUtc: this.businessTime.dayEndUtc(this.formEndDate),
      maxUsesPerCustomer: this.formMaxUsesPerCustomer || 1,
      maxTotalUses: this.formMaxTotalUses || null
    };
    try {
      if (this.editingId) {
        await firstValueFrom(this.apiClient.put(ApiController.Coupons, CouponsOperation.Update, body, { id: this.editingId }));
      } else {
        await firstValueFrom(this.apiClient.post(ApiController.Coupons, CouponsOperation.Create, {
          ...body, code, customerId: this.formAudience === 'customer' ? this.formCustomerId : null
        }));
      }
      this.formOpen.set(false);
      this.successMessage.set('انحفظ الكوبون - ابعته للزبائن من زر "إرسال".');
      await this.load();
    } catch (err) {
      this.errorMessage.set(this.errorDetail(err) ?? 'تعذّر حفظ الكوبون.');
    } finally {
      this.submitting.set(false);
    }
  }

  async toggleActive(coupon: CouponDto): Promise<void> {
    this.clearMessages();
    try {
      await firstValueFrom(this.apiClient.post(ApiController.Coupons, CouponsOperation.SetActive, { isActive: !coupon.isActive }, { id: coupon.id }));
      await this.load();
    } catch (err) {
      this.errorMessage.set(this.errorDetail(err) ?? 'تعذّر تغيير حالة الكوبون.');
    }
  }

  async openSend(coupon: CouponDto): Promise<void> {
    this.clearMessages();
    this.sendMode = 'all';
    this.sendCustomerId = '';
    this.sendTarget.set(coupon);
    if (!coupon.customerId) await this.ensureCustomers();
  }

  closeSend(): void {
    this.sendTarget.set(null);
  }

  async send(): Promise<void> {
    const coupon = this.sendTarget();
    if (!coupon) return;
    const customerId = coupon.customerId ? null : this.sendMode === 'customer' ? this.sendCustomerId : null;
    if (!coupon.customerId && this.sendMode === 'customer' && !customerId) {
      this.errorMessage.set('اختر الزبون.');
      return;
    }
    if (!coupon.customerId && this.sendMode === 'all' && !confirm(`إرسال ${coupon.code} لكل الزبائن؟`)) return;

    this.submitting.set(true);
    this.clearMessages();
    try {
      const result = await firstValueFrom(this.apiClient.post<{ customersTargeted: number; telegramSent: number }>(
        ApiController.Coupons, CouponsOperation.Send, { customerId }, { id: coupon.id }));
      this.sendTarget.set(null);
      this.successMessage.set(
        `انبعت لـ${result.customersTargeted} زبون (إشعار التطبيق)` +
        (result.telegramSent > 0 ? `، منهم ${result.telegramSent} على تلغرام.` : '.'));
      await this.load();
    } catch (err) {
      this.errorMessage.set(this.errorDetail(err) ?? 'تعذّر الإرسال.');
    } finally {
      this.submitting.set(false);
    }
  }

  private clearMessages(): void {
    this.errorMessage.set(null);
    this.successMessage.set(null);
  }

  private errorDetail(err: unknown): string | null {
    return err && typeof err === 'object' && 'error' in err ? (err as { error?: { detail?: string } }).error?.detail ?? null : null;
  }
}
