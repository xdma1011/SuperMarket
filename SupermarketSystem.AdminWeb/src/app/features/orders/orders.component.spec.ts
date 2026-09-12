import { ComponentFixture, TestBed } from '@angular/core/testing';
import { of, throwError } from 'rxjs';
import { OrdersComponent } from './orders.component';
import { ApiClient } from '../../core/api/api-client.service';

describe('OrdersComponent', () => {
  let fixture: ComponentFixture<OrdersComponent>;
  let component: OrdersComponent;
  let apiClientSpy: jasmine.SpyObj<ApiClient>;

  const sampleOrder = {
    id: 'o1',
    customerId: 'c1',
    customerName: 'أحمد',
    customerPhone: null,
    branchId: 'b1',
    status: 1,
    deliveryNote: null,
    estimatedTotal: 40,
    itemCount: 2,
    createdAtUtc: '',
    driverId: null,
    driverName: null
  };

  beforeEach(async () => {
    apiClientSpy = jasmine.createSpyObj('ApiClient', ['get', 'post']);
    apiClientSpy.get.and.returnValue(of({ items: [], totalCount: 0 }));

    await TestBed.configureTestingModule({
      imports: [OrdersComponent],
      providers: [{ provide: ApiClient, useValue: apiClientSpy }]
    }).compileComponents();

    fixture = TestBed.createComponent(OrdersComponent);
    component = fixture.componentInstance;
  });

  it('يُنشأ المكوّن بنجاح', () => {
    expect(component).toBeTruthy();
  });

  it('يحمّل الطلبات وطرق الدفع والسائقين معًا، ويختار أول طريقة دفع', async () => {
    apiClientSpy.get.and.callFake(((controller: string, operation: string) => {
      if (controller === 'orders' && operation === 'drivers') return of([{ id: 'd1', fullName: 'سائق' }]);
      if (controller === 'payment-methods') return of([{ id: 'pm1', name: 'كاش' }]);
      return of({ items: [sampleOrder], totalCount: 1 });
    }) as unknown as typeof apiClientSpy.get);

    fixture.detectChanges();
    await fixture.whenStable();

    expect(component.orders().length).toBe(1);
    expect(component.drivers().length).toBe(1);
    expect(component.completePaymentMethodId).toBe('pm1');
  });

  it('يعرض رسالة خطأ عربية واضحة عند فشل التحميل', async () => {
    apiClientSpy.get.and.returnValue(throwError(() => new Error('network')));

    fixture.detectChanges();
    await fixture.whenStable();

    expect(component.errorMessage()).toBe('تعذّر تحميل الطلبات.');
  });

  describe('statusLabel', () => {
    it('يترجم كل حالة معروفة لنص عربي صحيح', () => {
      expect(component.statusLabel(1)).toBe('بانتظار القبول');
      expect(component.statusLabel(2)).toBe('قيد التجهيز');
      expect(component.statusLabel(3)).toBe('مكتمل');
      expect(component.statusLabel(4)).toBe('مرفوض');
    });

    it('يرجّع "—" لحالة غير معروفة', () => {
      expect(component.statusLabel(99)).toBe('—');
    });
  });

  describe('accept', () => {
    it('يقبل الطلب ويعيد التحميل عند النجاح', async () => {
      apiClientSpy.post.and.returnValue(of({}));

      await component.accept(sampleOrder);

      expect(component.actionMessage()).toBe('تم قبول الطلب - أحمد.');
    });

    it('يعرض رسالة خطأ عربية واضحة عند الفشل', async () => {
      apiClientSpy.post.and.returnValue(throwError(() => new Error('network')));

      await component.accept(sampleOrder);

      expect(component.errorMessage()).toBe('تعذّر قبول الطلب.');
    });
  });

  describe('openRejectModal / closeRejectModal / confirmReject', () => {
    it('openRejectModal يفتح النافذة ويصفّر السبب', () => {
      component.rejectReason = 'قديم';
      component.openRejectModal(sampleOrder);

      expect(component.rejectModalOpen()).toBeTrue();
      expect(component.rejectReason).toBe('');
    });

    it('confirmReject يرفض بلا سبب', async () => {
      component.openRejectModal(sampleOrder);
      component.rejectReason = '   ';

      await component.confirmReject();

      expect(component.errorMessage()).toBe('سبب الرفض إلزامي.');
      expect(apiClientSpy.post).not.toHaveBeenCalled();
    });

    it('confirmReject يرسل السبب ويغلق النافذة عند النجاح', async () => {
      component.openRejectModal(sampleOrder);
      component.rejectReason = 'نفد المخزون';
      apiClientSpy.post.and.returnValue(of({}));

      await component.confirmReject();

      expect(apiClientSpy.post).toHaveBeenCalledWith(
        jasmine.anything(),
        jasmine.anything(),
        { reason: 'نفد المخزون' },
        { orderId: 'o1' }
      );
      expect(component.rejectModalOpen()).toBeFalse();
    });

    it('confirmReject لا يفعل شيئًا بلا طلب هدف', async () => {
      await component.confirmReject();
      expect(apiClientSpy.post).not.toHaveBeenCalled();
    });
  });

  describe('openCompleteModal / confirmComplete', () => {
    it('openCompleteModal يعبّي المبلغ التقديري', () => {
      component.openCompleteModal(sampleOrder);
      expect(component.completeAmount).toBe(40);
      expect(component.completeModalOpen()).toBeTrue();
    });

    it('confirmComplete يرفض بلا مبلغ صالح أو طريقة دفع', async () => {
      component.openCompleteModal(sampleOrder);
      component.completeAmount = 0;

      await component.confirmComplete();

      expect(component.errorMessage()).toBe('حدّد مبلغًا موجبًا وطريقة الدفع.');
    });

    it('confirmComplete ينجح ويعرض رسالة إتمام واضحة', async () => {
      component.openCompleteModal(sampleOrder);
      component.completePaymentMethodId = 'pm1';
      apiClientSpy.post.and.returnValue(of({}));

      await component.confirmComplete();

      expect(component.actionMessage()).toBe('تم إكمال الطلب - الفاتورة صدرت والمخزون انخصم.');
      expect(component.completeModalOpen()).toBeFalse();
    });

    it('confirmComplete يعرض رسالة الخطأ التفصيلية من الباك إند لو موجودة', async () => {
      component.openCompleteModal(sampleOrder);
      component.completePaymentMethodId = 'pm1';
      apiClientSpy.post.and.returnValue(throwError(() => ({ error: { detail: 'مبلغ الدفعة لا يطابق الإجمالي.' } })));

      await component.confirmComplete();

      expect(component.errorMessage()).toBe('مبلغ الدفعة لا يطابق الإجمالي.');
    });
  });

  describe('openAssignDriverModal / confirmAssignDriver', () => {
    it('openAssignDriverModal يختار السائق الحالي لو موجود، وإلا أول سائق بالقائمة', () => {
      component.drivers.set([{ id: 'd1', fullName: 'سائق1' }]);
      component.openAssignDriverModal(sampleOrder);

      expect(component.selectedDriverId).toBe('d1');
    });

    it('confirmAssignDriver يرفض بلا سائق محدَّد', async () => {
      component.drivers.set([]);
      component.openAssignDriverModal(sampleOrder);

      await component.confirmAssignDriver();

      expect(component.errorMessage()).toBe('اختر سائقًا.');
    });

    it('confirmAssignDriver ينجح ويغلق النافذة', async () => {
      component.drivers.set([{ id: 'd1', fullName: 'سائق1' }]);
      component.openAssignDriverModal(sampleOrder);
      apiClientSpy.post.and.returnValue(of({}));

      await component.confirmAssignDriver();

      expect(component.actionMessage()).toBe('تم إسناد السائق.');
      expect(component.assignDriverModalOpen()).toBeFalse();
    });

    it('confirmAssignDriver يعرض رسالة خطأ عربية واضحة عند الفشل', async () => {
      component.drivers.set([{ id: 'd1', fullName: 'سائق1' }]);
      component.openAssignDriverModal(sampleOrder);
      apiClientSpy.post.and.returnValue(throwError(() => new Error('network')));

      await component.confirmAssignDriver();

      expect(component.errorMessage()).toBe('تعذّر إسناد السائق.');
    });
  });
});
