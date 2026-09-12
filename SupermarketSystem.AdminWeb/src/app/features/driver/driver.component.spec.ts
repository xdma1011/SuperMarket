import { ComponentFixture, TestBed } from '@angular/core/testing';
import { of, throwError } from 'rxjs';
import { DriverComponent } from './driver.component';
import { ApiClient } from '../../core/api/api-client.service';

describe('DriverComponent', () => {
  let fixture: ComponentFixture<DriverComponent>;
  let component: DriverComponent;
  let apiClientSpy: jasmine.SpyObj<ApiClient>;

  const delivery = {
    orderId: 'o1',
    customerName: 'أحمد',
    customerPhone: '0790000000',
    deliveryNote: null,
    deliveryLatitude: 31.9,
    deliveryLongitude: 35.9,
    estimatedTotal: 25,
    itemCount: 3,
    createdAtUtc: ''
  };

  beforeEach(async () => {
    apiClientSpy = jasmine.createSpyObj('ApiClient', ['get', 'post']);
    apiClientSpy.get.and.returnValue(of([]));

    await TestBed.configureTestingModule({
      imports: [DriverComponent],
      providers: [{ provide: ApiClient, useValue: apiClientSpy }]
    }).compileComponents();

    fixture = TestBed.createComponent(DriverComponent);
    component = fixture.componentInstance;
  });

  it('يُنشأ المكوّن بنجاح', () => {
    expect(component).toBeTruthy();
  });

  it('يحمّل الطلبات وطرق الدفع معًا، ويختار أول طريقة دفع افتراضيًا', async () => {
    apiClientSpy.get.and.callFake(((controller: string) => {
      if (controller === 'driver') return of([delivery]);
      return of([{ id: 'pm1', name: 'كاش' }]);
    }) as unknown as typeof apiClientSpy.get);

    fixture.detectChanges();
    await fixture.whenStable();

    expect(component.deliveries().length).toBe(1);
    expect(component.paymentMethods().length).toBe(1);
    expect(component.payPaymentMethodId).toBe('pm1');
  });

  it('يعرض رسالة خطأ عربية واضحة عند فشل تحميل أي من الطلبين', async () => {
    apiClientSpy.get.and.returnValue(throwError(() => new Error('network')));

    fixture.detectChanges();
    await fixture.whenStable();

    expect(component.errorMessage()).toBe('تعذّر تحميل طلباتك.');
  });

  describe('mapUrl', () => {
    it('يبني رابط OpenStreetMap صحيح لو الإحداثيات متوفرة', () => {
      const url = component.mapUrl(delivery);
      expect(url).toContain('openstreetmap.org');
      expect(url).toContain('31.9');
      expect(url).toContain('35.9');
    });

    it('يرجّع null لو الإحداثيات غير متوفرة', () => {
      expect(component.mapUrl({ ...delivery, deliveryLatitude: null })).toBeNull();
      expect(component.mapUrl({ ...delivery, deliveryLongitude: null })).toBeNull();
    });
  });

  describe('openPayModal / closePayModal', () => {
    it('openPayModal يفتح النافذة ويعبّي المبلغ التقديري', () => {
      component.openPayModal(delivery);

      expect(component.payModalOpen()).toBeTrue();
      expect(component.payAmount).toBe(25);
    });

    it('closePayModal يغلق النافذة ويمسح الهدف', () => {
      component.openPayModal(delivery);
      component.closePayModal();

      expect(component.payModalOpen()).toBeFalse();
    });
  });

  describe('confirmPay', () => {
    it('يرفض التأكيد بلا مبلغ صالح أو طريقة دفع، بلا استدعاء API', async () => {
      component.openPayModal(delivery);
      component.payAmount = 0;
      component.payPaymentMethodId = '';

      await component.confirmPay();

      expect(component.errorMessage()).toBe('حدّد مبلغًا موجبًا وطريقة الدفع.');
      expect(apiClientSpy.post).not.toHaveBeenCalled();
    });

    it('يرسل الدفعة ويغلق النافذة عند النجاح', async () => {
      component.openPayModal(delivery);
      component.payPaymentMethodId = 'pm1';
      apiClientSpy.post.and.returnValue(of({}));

      await component.confirmPay();

      expect(apiClientSpy.post).toHaveBeenCalled();
      const [, , body, routeParams] = apiClientSpy.post.calls.mostRecent().args;
      expect((body as Record<string, unknown>)['amountCollected']).toBe(25);
      expect((body as Record<string, unknown>)['paymentMethodId']).toBe('pm1');
      expect(routeParams).toEqual({ orderId: 'o1' });
      expect(component.actionMessage()).toBe('تم تسجيل التسليم والدفعة بنجاح.');
      expect(component.payModalOpen()).toBeFalse();
    });

    it('يعرض رسالة الخطأ التفصيلية من الباك إند لو موجودة', async () => {
      component.openPayModal(delivery);
      component.payPaymentMethodId = 'pm1';
      apiClientSpy.post.and.returnValue(throwError(() => ({ error: { detail: 'الطلب أُلغي.' } })));

      await component.confirmPay();

      expect(component.errorMessage()).toBe('الطلب أُلغي.');
    });

    it('يعرض رسالة عربية عامة لو ما في تفاصيل خطأ', async () => {
      component.openPayModal(delivery);
      component.payPaymentMethodId = 'pm1';
      apiClientSpy.post.and.returnValue(throwError(() => new Error('network')));

      await component.confirmPay();

      expect(component.errorMessage()).toBe('تعذّر تسجيل الدفعة.');
    });
  });
});
