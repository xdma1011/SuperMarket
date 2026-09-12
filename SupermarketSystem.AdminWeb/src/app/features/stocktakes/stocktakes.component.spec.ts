import { ComponentFixture, TestBed } from '@angular/core/testing';
import { Router } from '@angular/router';
import { of, throwError } from 'rxjs';
import { StocktakesComponent } from './stocktakes.component';
import { ApiClient } from '../../core/api/api-client.service';

describe('StocktakesComponent', () => {
  let fixture: ComponentFixture<StocktakesComponent>;
  let component: StocktakesComponent;
  let apiClientSpy: jasmine.SpyObj<ApiClient>;
  let routerSpy: jasmine.SpyObj<Router>;

  beforeEach(async () => {
    apiClientSpy = jasmine.createSpyObj('ApiClient', ['get', 'post']);
    apiClientSpy.get.and.returnValue(of({ items: [], totalCount: 0 }));
    routerSpy = jasmine.createSpyObj('Router', ['navigate']);
    routerSpy.navigate.and.resolveTo(true);

    await TestBed.configureTestingModule({
      imports: [StocktakesComponent],
      providers: [{ provide: ApiClient, useValue: apiClientSpy }, { provide: Router, useValue: routerSpy }]
    }).compileComponents();

    fixture = TestBed.createComponent(StocktakesComponent);
    component = fixture.componentInstance;
  });

  it('يُنشأ المكوّن بنجاح', () => {
    expect(component).toBeTruthy();
  });

  it('يحمّل الفروع ويختار أول فرع تلقائيًا', async () => {
    apiClientSpy.get.and.callFake(((controller: string) => {
      if (controller === 'branches') return of({ items: [{ id: 'b1', name: 'الرئيسي' }], totalCount: 1 });
      return of({ items: [], totalCount: 0 });
    }) as unknown as typeof apiClientSpy.get);

    fixture.detectChanges();
    await fixture.whenStable();

    expect(component.selectedBranchId).toBe('b1');
  });

  describe('load', () => {
    it('يعرض رسالة خطأ عربية واضحة عند فشل تحميل عمليات الجرد', async () => {
      apiClientSpy.get.and.returnValue(throwError(() => new Error('network')));

      await component.load();

      expect(component.errorMessage()).toBe('تعذّر تحميل عمليات الجرد.');
    });
  });

  describe('onPageChanged', () => {
    it('يحدّث الصفحة ويعيد التحميل', () => {
      apiClientSpy.get.calls.reset();

      component.onPageChanged({ pageNumber: 2, pageSize: 10 });

      expect(component.pageNumber()).toBe(2);
      expect(apiClientSpy.get).toHaveBeenCalled();
    });
  });

  describe('openCreateForm / closeForm', () => {
    it('openCreateForm يفتح النموذج ويمسح خطأ سابق', () => {
      component.formError.set('خطأ سابق');
      component.openCreateForm();

      expect(component.formOpen()).toBeTrue();
      expect(component.formError()).toBeNull();
    });

    it('closeForm يغلق النموذج', () => {
      component.formOpen.set(true);
      component.closeForm();
      expect(component.formOpen()).toBeFalse();
    });
  });

  describe('submitCreate', () => {
    it('يرفض الإنشاء بلا فرع محدَّد', async () => {
      component.selectedBranchId = '';

      await component.submitCreate();

      expect(component.formError()).toBe('اختر فرع.');
      expect(apiClientSpy.post).not.toHaveBeenCalled();
    });

    it('ينشئ الجرد وينتقل لصفحة تفاصيله عند النجاح', async () => {
      component.selectedBranchId = 'b1';
      apiClientSpy.post.and.returnValue(of({ stocktakeId: 'st1', stocktakeNumber: 'ST-1', itemCount: 20 }));

      await component.submitCreate();

      expect(component.formOpen()).toBeFalse();
      expect(routerSpy.navigate).toHaveBeenCalledWith(['/stocktakes', 'st1']);
    });

    it('يرسل includeAllProductsAtBranch=true دائمًا (بلا اختيار أصناف بهذه المرحلة)', async () => {
      component.selectedBranchId = 'b1';
      apiClientSpy.post.and.returnValue(of({ stocktakeId: 'st1', stocktakeNumber: 'ST-1', itemCount: 5 }));

      await component.submitCreate();

      expect(apiClientSpy.post).toHaveBeenCalledWith(
        jasmine.anything(),
        jasmine.anything(),
        jasmine.objectContaining({ includeAllProductsAtBranch: true, branchId: 'b1' })
      );
    });

    it('يعرض رسالة الخطأ التفصيلية من الباك إند لو موجودة', async () => {
      component.selectedBranchId = 'b1';
      apiClientSpy.post.and.returnValue(throwError(() => ({ error: { detail: 'يوجد جرد مفتوح أصلًا لهذا الفرع.' } })));

      await component.submitCreate();

      expect(component.formError()).toBe('يوجد جرد مفتوح أصلًا لهذا الفرع.');
      expect(routerSpy.navigate).not.toHaveBeenCalled();
    });
  });

  describe('openStocktake', () => {
    it('ينتقل لصفحة تفاصيل الجرد بالمعرّف الصحيح', () => {
      component.openStocktake('st9');
      expect(routerSpy.navigate).toHaveBeenCalledWith(['/stocktakes', 'st9']);
    });
  });

  describe('statusTone', () => {
    it('يرجّع green لحالة معتمد (4)', () => {
      expect(component.statusTone(4)).toBe('green');
    });

    it('يرجّع red لحالة مرفوض (5)', () => {
      expect(component.statusTone(5)).toBe('red');
    });

    it('يرجّع accent لأي حالة أخرى (مفتوح/مكتمل بانتظار اعتماد)', () => {
      expect(component.statusTone(1)).toBe('accent');
      expect(component.statusTone(3)).toBe('accent');
    });
  });
});
