import { ComponentFixture, TestBed } from '@angular/core/testing';
import { ActivatedRoute, Router, convertToParamMap } from '@angular/router';
import { of, throwError } from 'rxjs';
import { StocktakeDetailComponent } from './stocktake-detail.component';
import { ApiClient } from '../../core/api/api-client.service';
import { PermissionsService } from '../../core/services/permissions.service';

describe('StocktakeDetailComponent', () => {
  let fixture: ComponentFixture<StocktakeDetailComponent>;
  let component: StocktakeDetailComponent;
  let apiClientSpy: jasmine.SpyObj<ApiClient>;
  let routerSpy: jasmine.SpyObj<Router>;

  const sampleDetail = {
    stocktakeId: 'st1',
    stocktakeNumber: 'ST-1',
    branchId: 'b1',
    status: 2,
    completedAtUtc: null,
    approvedAtUtc: null,
    items: [
      { stocktakeItemId: 'i1', productId: 'p1', productName: 'سكر', productBatchId: null, expectedQuantity: 10, countedQuantity: null, variance: null, countedByUserId: null, countedAtUtc: null },
      { stocktakeItemId: 'i2', productId: 'p2', productName: 'ملح', productBatchId: null, expectedQuantity: 5, countedQuantity: 5, variance: 0, countedByUserId: 'u1', countedAtUtc: '' }
    ]
  };

  beforeEach(async () => {
    apiClientSpy = jasmine.createSpyObj('ApiClient', ['get', 'post']);
    apiClientSpy.get.and.returnValue(of(sampleDetail));
    routerSpy = jasmine.createSpyObj('Router', ['navigate']);
    routerSpy.navigate.and.resolveTo(true);

    await TestBed.configureTestingModule({
      imports: [StocktakeDetailComponent],
      providers: [
        { provide: ApiClient, useValue: apiClientSpy },
        { provide: Router, useValue: routerSpy },
        { provide: ActivatedRoute, useValue: { snapshot: { paramMap: convertToParamMap({ id: 'st1' }) } } },
        { provide: PermissionsService, useValue: jasmine.createSpyObj('PermissionsService', ['has'], { loaded: () => true }) }
      ]
    }).compileComponents();

    fixture = TestBed.createComponent(StocktakeDetailComponent);
    component = fixture.componentInstance;
  });

  it('يُنشأ المكوّن بنجاح', () => {
    expect(component).toBeTruthy();
  });

  it('يقرأ معرّف الجرد من الرابط ويحمّل التفاصيل عند ngOnInit', async () => {
    fixture.detectChanges();
    await fixture.whenStable();

    expect(apiClientSpy.get).toHaveBeenCalledWith(jasmine.anything(), jasmine.anything(), { id: 'st1' });
    expect(component.stocktake()).not.toBeNull();
  });

  it('يعرض رسالة خطأ عربية واضحة عند فشل التحميل', async () => {
    apiClientSpy.get.and.returnValue(throwError(() => new Error('network')));

    await component.load();

    expect(component.errorMessage()).toBe('تعذّر تحميل الجرد - تأكد إنه موجود.');
  });

  describe('canCount', () => {
    it('يرجّع true بحالة مسودة (1) أو قيد التنفيذ (2)', async () => {
      await component.load();
      component.stocktake.update(s => (s ? { ...s, status: 1 } : s));
      expect(component.canCount()).toBeTrue();

      component.stocktake.update(s => (s ? { ...s, status: 2 } : s));
      expect(component.canCount()).toBeTrue();
    });

    it('يرجّع false بحالة مكتمل أو معتمد أو ملغى', async () => {
      await component.load();
      component.stocktake.update(s => (s ? { ...s, status: 3 } : s));
      expect(component.canCount()).toBeFalse();
    });

    it('يرجّع false بلا أي جرد محمَّل', () => {
      expect(component.canCount()).toBeFalse();
    });
  });

  describe('saveCount', () => {
    it('لا يفعل شيئًا لقيمة null', async () => {
      await component.load();
      await component.saveCount(sampleDetail.items[0], null);
      expect(apiClientSpy.post).not.toHaveBeenCalled();
    });

    it('لا يفعل شيئًا لقيمة سالبة', async () => {
      await component.load();
      await component.saveCount(sampleDetail.items[0], -1);
      expect(apiClientSpy.post).not.toHaveBeenCalled();
    });

    it('يحفظ العدّ ويحدّث الكمية والفرق بالسطر المحلي', async () => {
      await component.load();
      const item = { ...sampleDetail.items[0] };
      apiClientSpy.post.and.returnValue(of({ expectedQuantity: 10, countedQuantity: 12, variance: 2 }));

      await component.saveCount(item, 12);

      expect(item.countedQuantity).toBe(12);
      expect(item.variance).toBe(2);
      expect(component.savingItemId()).toBeNull();
    });

    it('يعرض رسالة خطأ عربية تحمل اسم المنتج عند الفشل', async () => {
      await component.load();
      apiClientSpy.post.and.returnValue(throwError(() => new Error('network')));

      await component.saveCount(sampleDetail.items[0], 10);

      expect(component.errorMessage()).toBe('تعذّر حفظ عدّ "سكر".');
    });
  });

  describe('countedItemsCount / totalItemsCount', () => {
    it('يحسب عدد الأصناف المعدودة والإجمالي بدقة', async () => {
      await component.load();

      expect(component.countedItemsCount).toBe(1);
      expect(component.totalItemsCount).toBe(2);
    });

    it('يرجّع صفر بلا جرد محمَّل', () => {
      expect(component.countedItemsCount).toBe(0);
      expect(component.totalItemsCount).toBe(0);
    });
  });

  describe('completeStocktake', () => {
    it('يكمل الجرد ويعيد التحميل عند النجاح', async () => {
      apiClientSpy.post.and.returnValue(of({}));

      await component.completeStocktake();

      expect(component.completing()).toBeFalse();
    });

    it('يعرض رسالة الخطأ التفصيلية من الباك إند لو موجودة', async () => {
      apiClientSpy.post.and.returnValue(throwError(() => ({ error: { detail: 'يوجد أصناف غير معدودة.' } })));

      await component.completeStocktake();

      expect(component.errorMessage()).toBe('يوجد أصناف غير معدودة.');
    });
  });

  describe('approveStocktake', () => {
    it('يعتمد الجرد بنجاح', async () => {
      apiClientSpy.post.and.returnValue(of({ stocktakeId: 'st1', stocktakeNumber: 'ST-1', appliedCorrections: [] }));

      await component.approveStocktake();

      expect(component.approving()).toBeFalse();
    });

    it('يعرض رسالة خطأ عربية عامة عند الفشل بلا تفاصيل', async () => {
      apiClientSpy.post.and.returnValue(throwError(() => new Error('network')));

      await component.approveStocktake();

      expect(component.errorMessage()).toBe('تعذّر اعتماد الجرد.');
    });
  });

  describe('backToList', () => {
    it('ينتقل لقائمة عمليات الجرد', () => {
      component.backToList();
      expect(routerSpy.navigate).toHaveBeenCalledWith(['/stocktakes']);
    });
  });

  describe('varianceClass', () => {
    it('يرجّع نص فاضٍ لفرق null أو صفر', () => {
      expect(component.varianceClass(null)).toBe('');
      expect(component.varianceClass(0)).toBe('');
    });

    it('يرجّع variance-positive لفرق موجب', () => {
      expect(component.varianceClass(3)).toBe('variance-positive');
    });

    it('يرجّع variance-negative لفرق سالب', () => {
      expect(component.varianceClass(-3)).toBe('variance-negative');
    });
  });
});
