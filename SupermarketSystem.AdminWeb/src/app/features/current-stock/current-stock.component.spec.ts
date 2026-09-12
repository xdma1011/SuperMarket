import { ComponentFixture, TestBed } from '@angular/core/testing';
import { of, throwError } from 'rxjs';
import { CurrentStockComponent } from './current-stock.component';
import { ApiClient } from '../../core/api/api-client.service';

describe('CurrentStockComponent', () => {
  let fixture: ComponentFixture<CurrentStockComponent>;
  let component: CurrentStockComponent;
  let apiClientSpy: jasmine.SpyObj<ApiClient>;

  beforeEach(async () => {
    apiClientSpy = jasmine.createSpyObj('ApiClient', ['get']);
    apiClientSpy.get.and.returnValue(of({ items: [], totalCount: 0 }));

    await TestBed.configureTestingModule({
      imports: [CurrentStockComponent],
      providers: [{ provide: ApiClient, useValue: apiClientSpy }]
    }).compileComponents();

    fixture = TestBed.createComponent(CurrentStockComponent);
    component = fixture.componentInstance;
  });

  it('يُنشأ المكوّن بنجاح', () => {
    expect(component).toBeTruthy();
  });

  it('يحمّل الفروع والمخزون معًا عند ngOnInit', async () => {
    apiClientSpy.get.and.callFake(((controller: string) => {
      if (controller === 'branches') return of({ items: [{ id: 'b1', name: 'الفرع الرئيسي' }], totalCount: 1 });
      return of({
        items: [
          {
            productId: 'p1',
            productName: 'سكر',
            categoryName: 'مواد غذائية',
            branchId: 'b1',
            branchName: 'الرئيسي',
            quantityOnHand: 10,
            baseUnitName: 'كيلو'
          }
        ],
        totalCount: 1
      });
    }) as unknown as typeof apiClientSpy.get);

    fixture.detectChanges();
    await fixture.whenStable();

    expect(component.items().length).toBe(1);
  });

  describe('load', () => {
    it('يعرض رسالة خطأ عربية واضحة عند فشل تحميل المخزون', async () => {
      apiClientSpy.get.and.returnValue(throwError(() => new Error('network')));

      await component.load();

      expect(component.errorMessage()).toBe('تعذّر تحميل المخزون الحالي.');
      expect(component.loading()).toBeFalse();
    });

    it('لا يظهر أي خطأ لو فشل تحميل الفروع فقط (لا يمنع عرض المخزون)', async () => {
      apiClientSpy.get.and.returnValue(throwError(() => new Error('branches failed')));

      await (component as unknown as { loadBranches(): Promise<void> }).loadBranches();

      expect(component.branches()).toEqual([]);
    });
  });

  describe('onSearchChange', () => {
    it('يحدّث البحث ويرجّع لصفحة 1 ويعيد التحميل فورًا', async () => {
      component.pageNumber.set(4);
      apiClientSpy.get.calls.reset();

      component.onSearchChange('سكر');

      expect(component.searchQuery()).toBe('سكر');
      expect(component.pageNumber()).toBe(1);
      expect(apiClientSpy.get).toHaveBeenCalled();
    });
  });

  describe('onBranchFilterChange', () => {
    it('يرجّع لصفحة 1 ويعيد التحميل', () => {
      component.pageNumber.set(3);
      apiClientSpy.get.calls.reset();

      component.onBranchFilterChange();

      expect(component.pageNumber()).toBe(1);
      expect(apiClientSpy.get).toHaveBeenCalled();
    });
  });

  describe('onPageChanged', () => {
    it('يحدّث رقم وحجم الصفحة ويعيد التحميل', () => {
      apiClientSpy.get.calls.reset();

      component.onPageChanged({ pageNumber: 5, pageSize: 100 });

      expect(component.pageNumber()).toBe(5);
      expect(component.pageSize()).toBe(100);
      expect(apiClientSpy.get).toHaveBeenCalled();
    });
  });
});
