import { ComponentFixture, TestBed } from '@angular/core/testing';
import { of, throwError } from 'rxjs';
import { SalesComponent } from './sales.component';
import { ApiClient } from '../../core/api/api-client.service';

describe('SalesComponent', () => {
  let fixture: ComponentFixture<SalesComponent>;
  let component: SalesComponent;
  let apiClientSpy: jasmine.SpyObj<ApiClient>;

  beforeEach(async () => {
    apiClientSpy = jasmine.createSpyObj('ApiClient', ['get']);
    apiClientSpy.get.and.returnValue(of({ items: [], totalCount: 0 }));

    await TestBed.configureTestingModule({
      imports: [SalesComponent],
      providers: [{ provide: ApiClient, useValue: apiClientSpy }]
    }).compileComponents();

    fixture = TestBed.createComponent(SalesComponent);
    component = fixture.componentInstance;
  });

  it('يُنشأ المكوّن بنجاح', () => {
    expect(component).toBeTruthy();
  });

  describe('loadInvoices', () => {
    it('يحمّل الفواتير ويحدّث totalCount', async () => {
      apiClientSpy.get.and.returnValue(
        of({ items: [{ id: 's1', invoiceNumber: 'INV-1', statusCode: 1, statusTitle: 'مكتملة', totalAmount: 10, totalReturnedAmount: 0, createdAtUtc: '', customerName: null, customerPhone: null }], totalCount: 1 })
      );

      await component.loadInvoices();

      expect(component.invoices().length).toBe(1);
      expect(component.totalCount()).toBe(1);
    });

    it('يعرض رسالة خطأ عربية واضحة عند فشل تحميل الفواتير', async () => {
      apiClientSpy.get.and.returnValue(throwError(() => new Error('network')));

      await component.loadInvoices();

      expect(component.errorMessage()).toBe('تعذّر تحميل الفواتير.');
    });
  });

  it('فشل تحميل ملخّص المبيعات لا يمنع عرض جدول الفواتير', async () => {
    apiClientSpy.get.and.callFake(((controller: string) => {
      if (controller === 'reports') return throwError(() => new Error('reports down'));
      return of({ items: [], totalCount: 0 });
    }) as unknown as typeof apiClientSpy.get);

    fixture.detectChanges();
    await fixture.whenStable();

    expect(component.summary()).toBeNull();
    expect(component.errorMessage()).toBeNull();
  });

  describe('onSearchChange', () => {
    it('يحدّث البحث ويرجّع لصفحة 1 ويعيد التحميل', () => {
      component.pageNumber.set(3);
      apiClientSpy.get.calls.reset();

      component.onSearchChange('INV');

      expect(component.searchQuery()).toBe('INV');
      expect(component.pageNumber()).toBe(1);
      expect(apiClientSpy.get).toHaveBeenCalled();
    });
  });

  describe('onPageChanged', () => {
    it('يحدّث رقم وحجم الصفحة ويعيد التحميل', () => {
      apiClientSpy.get.calls.reset();

      component.onPageChanged({ pageNumber: 2, pageSize: 50 });

      expect(component.pageNumber()).toBe(2);
      expect(component.pageSize()).toBe(50);
      expect(apiClientSpy.get).toHaveBeenCalled();
    });
  });

  describe('statusTone', () => {
    it('يرجّع red لحالة ملغاة (2)', () => {
      expect(component.statusTone(2)).toBe('red');
    });

    it('يرجّع accent لحالة إرجاع جزئي (3) أو كامل (4)', () => {
      expect(component.statusTone(3)).toBe('accent');
      expect(component.statusTone(4)).toBe('accent');
    });

    it('يرجّع green لحالة مكتملة (1) أو أي قيمة أخرى غير معروفة', () => {
      expect(component.statusTone(1)).toBe('green');
      expect(component.statusTone(99)).toBe('green');
    });
  });
});
