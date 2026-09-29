import { ComponentFixture, TestBed } from '@angular/core/testing';
import { of, throwError } from 'rxjs';
import { CouponsComponent, CouponDto } from './coupons.component';
import { ApiClient } from '../../core/api/api-client.service';
import { BusinessTimeService } from '../../core/services/business-time.service';

const EID: CouponDto = {
  id: 'c1', code: 'EID-2026', title: 'خصم العيد', discountTypeCode: 2, discountTypeTitle: 'نسبة', value: 10, maxDiscountAmount: 1,
  minOrderAmount: 3, startAtUtc: '2026-09-28T21:00:00Z', endAtUtc: '2026-10-28T20:59:59.999Z', customerId: null, customerName: null,
  customerPhone: null, maxUsesPerCustomer: 1, maxTotalUses: null, isActive: true, lastSentAtUtc: null, reservedCount: 1,
  redeemedCount: 2, redeemedDiscountTotal: 0.9, statusTitle: 'شغّال'
};

describe('CouponsComponent', () => {
  let fixture: ComponentFixture<CouponsComponent>;
  let component: CouponsComponent;
  let api: jasmine.SpyObj<ApiClient>;

  beforeEach(async () => {
    api = jasmine.createSpyObj('ApiClient', ['get', 'post', 'put']);
    api.get.and.callFake(((controller: string) => {
      if (controller === 'coupons') return of([EID]);
      if (controller === 'customers') return of({ items: [{ id: 'cu1', fullName: 'أبو سامي', phone: '0795550001' }] });
      return of([]);
    }) as unknown as typeof api.get);

    await TestBed.configureTestingModule({
      imports: [CouponsComponent],
      providers: [{ provide: ApiClient, useValue: api }, BusinessTimeService]
    }).compileComponents();

    fixture = TestBed.createComponent(CouponsComponent);
    component = fixture.componentInstance;
    fixture.detectChanges();
    await fixture.whenStable();
  });

  it('بيعرض الكوبونات بوصف الخصم', () => {
    expect(component.coupons().length).toBe(1);
    expect(component.describe(EID)).toBe('10% (لحد 1.000 د.أ)');
    fixture.detectChanges();
    expect(fixture.nativeElement.textContent).toContain('EID-2026');
  });

  it('كود غلط أو نسبة فوق 100 ما بينحفظوا', async () => {
    await component.openForm();
    component.formCode = 'a b';
    component.formTitle = 'تجربة';
    component.formValue = 1;
    await component.save();
    expect(api.post).not.toHaveBeenCalled();
    expect(component.errorMessage()).toContain('الكود');

    component.formCode = 'OK-1234';
    component.formType = 'Percent';
    component.formValue = 150;
    await component.save();
    expect(api.post).not.toHaveBeenCalled();
  });

  it('كوبون لزبون معيّن بينبعت بالكود بحروف كبيرة والفترة أيام محلية كاملة', async () => {
    api.post.and.returnValue(of({ couponId: 'new' }));
    await component.openForm();
    component.formCode = 'vip-1';
    component.formTitle = 'زبون مميز';
    component.formValue = 0.5;
    component.formAudience = 'customer';
    component.formCustomerId = 'cu1';
    component.formStartDate = '2026-10-01';
    component.formEndDate = '2026-10-31';
    await component.save();
    const body = api.post.calls.mostRecent().args[2] as Record<string, unknown>;
    expect(body['code']).toBe('VIP-1');
    expect(body['customerId']).toBe('cu1');
    expect(body['discountType']).toBe('FixedAmount');
    expect(body['maxDiscountAmount']).toBeNull();
    expect(new Date(body['endAtUtc'] as string).getTime()).toBeGreaterThan(new Date(body['startAtUtc'] as string).getTime());
    expect(component.formOpen()).toBeFalse();
  });

  it('الإرسال لزبون واحد بيبعت الـid، وللكل بلاه، ورسالة السيرفر بتبين عند الفشل', async () => {
    api.post.and.returnValue(of({ customersTargeted: 1, telegramSent: 1 }));
    await component.openSend(EID);
    component.sendMode = 'customer';
    component.sendCustomerId = 'cu1';
    await component.send();
    expect(api.post).toHaveBeenCalledWith('coupons' as never, '{id}/send', { customerId: 'cu1' }, { id: 'c1' });
    expect(component.successMessage()).toContain('تلغرام');

    spyOn(window, 'confirm').and.returnValue(true);
    api.post.and.returnValue(throwError(() => ({ error: { detail: 'الكوبون موقوف' } })));
    await component.openSend(EID);
    await component.send();
    expect(api.post.calls.mostRecent().args[2]).toEqual({ customerId: null });
    expect(component.errorMessage()).toBe('الكوبون موقوف');
  });
});
