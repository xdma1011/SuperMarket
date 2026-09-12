import { ComponentFixture, TestBed } from '@angular/core/testing';
import { of, throwError } from 'rxjs';
import { CashClosingsComponent } from './cash-closings.component';
import { ApiClient } from '../../core/api/api-client.service';

describe('CashClosingsComponent', () => {
  let fixture: ComponentFixture<CashClosingsComponent>;
  let component: CashClosingsComponent;
  let apiClientSpy: jasmine.SpyObj<ApiClient>;

  function mockLookupsSuccess() {
    apiClientSpy.get.and.callFake(((controller: string) => {
      if (controller === 'branches') return of({ items: [{ id: 'b1', name: 'الرئيسي' }], totalCount: 1 });
      if (controller === 'payment-methods') return of([{ id: 'pm1', name: 'كاش' }]);
      return of({ items: [], totalCount: 0 });
    }) as unknown as typeof apiClientSpy.get);
  }

  beforeEach(async () => {
    apiClientSpy = jasmine.createSpyObj('ApiClient', ['get', 'post']);
    apiClientSpy.get.and.returnValue(of({ items: [], totalCount: 0 }));

    await TestBed.configureTestingModule({
      imports: [CashClosingsComponent],
      providers: [{ provide: ApiClient, useValue: apiClientSpy }]
    }).compileComponents();

    fixture = TestBed.createComponent(CashClosingsComponent);
    component = fixture.componentInstance;
  });

  it('يُنشأ المكوّن بنجاح', () => {
    expect(component).toBeTruthy();
  });

  it('يحمّل الفروع وطرق الدفع ويبني countedDetails بحجم مطابق، ويختار أول فرع', async () => {
    mockLookupsSuccess();

    fixture.detectChanges();
    await fixture.whenStable();

    expect(component.selectedBranchId).toBe('b1');
    expect(component.countedDetails().length).toBe(1);
    expect(component.countedDetails()[0].paymentMethodId).toBe('pm1');
  });

  it('يعرض رسالة خطأ عربية واضحة لو فشل تحميل الفروع/طرق الدفع', async () => {
    apiClientSpy.get.and.returnValue(throwError(() => new Error('network')));

    fixture.detectChanges();
    await fixture.whenStable();

    expect(component.errorMessage()).toBe('تعذّر تحميل الفروع/طرق الدفع.');
  });

  describe('loadClosings', () => {
    it('يعرض رسالة خطأ عربية واضحة عند فشل تحميل التقفيلات', async () => {
      apiClientSpy.get.and.returnValue(throwError(() => new Error('network')));

      await component.loadClosings();

      expect(component.errorMessage()).toBe('تعذّر تحميل قائمة تقفيلات الصندوق.');
    });
  });

  describe('onFilterBranchChange / onPageChanged', () => {
    it('onFilterBranchChange يرجّع لصفحة 1 ويعيد التحميل', () => {
      component.pageNumber.set(3);
      apiClientSpy.get.calls.reset();

      component.onFilterBranchChange();

      expect(component.pageNumber()).toBe(1);
      expect(apiClientSpy.get).toHaveBeenCalled();
    });

    it('onPageChanged يحدّث الصفحة', () => {
      apiClientSpy.get.calls.reset();
      component.onPageChanged({ pageNumber: 4, pageSize: 50 });
      expect(component.pageNumber()).toBe(4);
    });
  });

  describe('openForm', () => {
    it('يعيد بناء countedDetails من طرق الدفع الحالية ويصفّر المبلغ المعدود', () => {
      component.paymentMethods.set([{ id: 'pm1', name: 'كاش' }]);
      component.countedCash = 999;

      component.openForm();

      expect(component.formOpen()).toBeTrue();
      expect(component.countedCash).toBeNull();
      expect(component.countedDetails().length).toBe(1);
    });
  });

  describe('submit', () => {
    it('يرفض الإرسال بلا فرع أو تاريخ أو مبلغ معدود صالح', async () => {
      component.selectedBranchId = '';
      component.countedCash = null;

      await component.submit();

      expect(component.formError()).toContain('عبّي الفرع');
      expect(apiClientSpy.post).not.toHaveBeenCalled();
    });

    it('يرفض مبلغًا معدودًا سالبًا', async () => {
      component.selectedBranchId = 'b1';
      component.countedCash = -5;

      await component.submit();

      expect(apiClientSpy.post).not.toHaveBeenCalled();
    });

    it('يرسل فقط تفاصيل countedDetails غير null، ويصفّي الباقي', async () => {
      component.selectedBranchId = 'b1';
      component.countedCash = 100;
      component.countedDetails.set([
        { paymentMethodId: 'pm1', paymentMethodName: 'كاش', countedAmount: 100 },
        { paymentMethodId: 'pm2', paymentMethodName: 'بطاقة', countedAmount: null }
      ]);
      apiClientSpy.post.and.returnValue(of({}));

      await component.submit();

      const [, , body] = apiClientSpy.post.calls.mostRecent().args;
      const sentDetails = (body as { countedDetails: unknown[] }).countedDetails;
      expect(sentDetails.length).toBe(1);
      expect(component.formOpen()).toBeFalse();
    });

    it('يعرض رسالة الخطأ التفصيلية من الباك إند لو موجودة', async () => {
      component.selectedBranchId = 'b1';
      component.countedCash = 50;
      apiClientSpy.post.and.returnValue(throwError(() => ({ error: { detail: 'يوجد تقفيل مسجَّل أصلًا لهذا اليوم.' } })));

      await component.submit();

      expect(component.formError()).toBe('يوجد تقفيل مسجَّل أصلًا لهذا اليوم.');
    });
  });

  describe('varianceTone', () => {
    it('يرجّع green لو الفرق صفر (بلا عجز أو زيادة)', () => {
      expect(component.varianceTone(0)).toBe('green');
    });

    it('يرجّع red لو في أي فرق (عجز أو زيادة)', () => {
      expect(component.varianceTone(5)).toBe('red');
      expect(component.varianceTone(-5)).toBe('red');
    });
  });
});
