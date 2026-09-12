import { ComponentFixture, TestBed } from '@angular/core/testing';
import { of, throwError } from 'rxjs';
import { ComplimentaryComponent } from './complimentary.component';
import { ApiClient } from '../../core/api/api-client.service';

describe('ComplimentaryComponent', () => {
  let fixture: ComponentFixture<ComplimentaryComponent>;
  let component: ComplimentaryComponent;
  let apiClientSpy: jasmine.SpyObj<ApiClient>;

  function mockLoadAllSuccess() {
    apiClientSpy.get.and.callFake(((controller: string, operation: string) => {
      if (controller === 'products' && operation === '') return of({ items: [{ id: 'p1', name: 'سكر' }], totalCount: 1 });
      if (controller === 'branches') return of({ items: [{ id: 'b1', name: 'الرئيسي' }], totalCount: 1 });
      if (operation === '{productId}/units') return of([{ id: 'u1', unitName: 'كيلو', isBaseUnit: true }]);
      return of([]);
    }) as unknown as typeof apiClientSpy.get);
  }

  beforeEach(async () => {
    apiClientSpy = jasmine.createSpyObj('ApiClient', ['get', 'post']);
    apiClientSpy.get.and.returnValue(of({ items: [], totalCount: 0 }));

    await TestBed.configureTestingModule({
      imports: [ComplimentaryComponent],
      providers: [{ provide: ApiClient, useValue: apiClientSpy }]
    }).compileComponents();

    fixture = TestBed.createComponent(ComplimentaryComponent);
    component = fixture.componentInstance;
  });

  it('يُنشأ المكوّن بنجاح', () => {
    expect(component).toBeTruthy();
  });

  it('يحمّل المنتجات والفروع، ويختار أول عنصر من كل، ويجلب وحدات أول منتج تلقائيًا', async () => {
    mockLoadAllSuccess();

    fixture.detectChanges();
    await fixture.whenStable();

    expect(component.products().length).toBe(1);
    expect(component.branches().length).toBe(1);
    expect(component.selectedBranchId).toBe('b1');
    expect(component.selectedProductId).toBe('p1');
    expect(component.units().length).toBe(1);
    expect(component.selectedUnitId).toBe('u1');
  });

  it('يعرض رسالة خطأ عربية واضحة عند فشل تحميل البيانات الأساسية', async () => {
    apiClientSpy.get.and.returnValue(throwError(() => new Error('network')));

    fixture.detectChanges();
    await fixture.whenStable();

    expect(component.errorMessage()).toBe('تعذّر تحميل البيانات الأساسية.');
  });

  describe('onProductChange', () => {
    it('يمسح الوحدات لو ما في منتج محدَّد', async () => {
      component.selectedProductId = '';
      await component.onProductChange();
      expect(component.units()).toEqual([]);
    });

    it('يختار الوحدة الأساسية (isBaseUnit) لو موجودة، لا أول عنصر بالقائمة', async () => {
      component.selectedProductId = 'p1';
      apiClientSpy.get.and.returnValue(
        of([
          { id: 'u1', unitName: 'قطعة', isBaseUnit: false },
          { id: 'u2', unitName: 'كرتونة', isBaseUnit: true }
        ])
      );

      await component.onProductChange();

      expect(component.selectedUnitId).toBe('u2');
    });

    it('يعرض رسالة خطأ عربية واضحة عند فشل جلب الوحدات', async () => {
      component.selectedProductId = 'p1';
      apiClientSpy.get.and.returnValue(throwError(() => new Error('network')));

      await component.onProductChange();

      expect(component.errorMessage()).toBe('تعذّر جلب وحدات هذا المنتج.');
    });
  });

  describe('submit', () => {
    it('يرفض الإرسال بلا كل الحقول المطلوبة', async () => {
      component.selectedProductId = 'p1';
      component.selectedBranchId = '';
      component.selectedUnitId = 'u1';
      component.quantity = 1;

      await component.submit();

      expect(component.errorMessage()).toContain('عبّي كل الحقول المطلوبة');
      expect(apiClientSpy.post).not.toHaveBeenCalled();
    });

    it('يرفض كمية صفر أو سالبة', async () => {
      component.selectedProductId = 'p1';
      component.selectedBranchId = 'b1';
      component.selectedUnitId = 'u1';
      component.quantity = 0;

      await component.submit();

      expect(apiClientSpy.post).not.toHaveBeenCalled();
    });

    it('يسجّل الضيافة بنجاح ويصفّر الكمية والسبب', async () => {
      component.selectedProductId = 'p1';
      component.selectedBranchId = 'b1';
      component.selectedUnitId = 'u1';
      component.quantity = 5;
      component.reason = 'زبون VIP';
      apiClientSpy.post.and.returnValue(of({}));

      await component.submit();

      expect(component.successMessage()).toBe('تم تسجيل الضيافة بنجاح، ونقص المخزون فورًا.');
      expect(component.quantity).toBeNull();
      expect(component.reason).toBe('');
    });

    it('يعرض رسالة الخطأ التفصيلية من الباك إند لو موجودة (مثلًا تجاوز حد سماح مع مراجعة)', async () => {
      component.selectedProductId = 'p1';
      component.selectedBranchId = 'b1';
      component.selectedUnitId = 'u1';
      component.quantity = 100;
      apiClientSpy.post.and.returnValue(throwError(() => ({ error: { detail: 'تجاوز الحد اليومي - بانتظار مراجعة.' } })));

      await component.submit();

      expect(component.errorMessage()).toBe('تجاوز الحد اليومي - بانتظار مراجعة.');
    });
  });
});
