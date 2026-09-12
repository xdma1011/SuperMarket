import { ComponentFixture, TestBed } from '@angular/core/testing';
import { of, throwError } from 'rxjs';
import { ReportsComponent } from './reports.component';
import { ApiClient } from '../../core/api/api-client.service';

describe('ReportsComponent', () => {
  let fixture: ComponentFixture<ReportsComponent>;
  let component: ReportsComponent;
  let apiClientSpy: jasmine.SpyObj<ApiClient>;

  beforeEach(async () => {
    apiClientSpy = jasmine.createSpyObj('ApiClient', ['get']);
    apiClientSpy.get.and.returnValue(of({ items: [], totalCount: 0 }));

    await TestBed.configureTestingModule({
      imports: [ReportsComponent],
      providers: [{ provide: ApiClient, useValue: apiClientSpy }]
    }).compileComponents();

    fixture = TestBed.createComponent(ReportsComponent);
    component = fixture.componentInstance;
  });

  it('يُنشأ المكوّن بنجاح ويبدأ بأول تقرير من REPORT_CONFIGS نشِطًا', () => {
    expect(component).toBeTruthy();
    expect(component.activeReportId()).toBe(component.standardReports[0].id);
  });

  it('يختار أول فرع تلقائيًا عند تحميل الفروع', async () => {
    apiClientSpy.get.and.callFake(((controller: string) => {
      if (controller === 'branches') return of({ items: [{ id: 'b1', name: 'الرئيسي' }] });
      return of({ items: [], totalCount: 0 });
    }) as unknown as typeof apiClientSpy.get);

    fixture.detectChanges();
    await fixture.whenStable();

    expect(component.selectedBranchId).toBe('b1');
  });

  describe('isSpecial', () => {
    it('يرجّع true للتقارير الخاصة الثلاثة', () => {
      component.activeReportId.set('sales-summary');
      expect(component.isSpecial()).toBeTrue();
      component.activeReportId.set('capital-value');
      expect(component.isSpecial()).toBeTrue();
      component.activeReportId.set('supplier-debts');
      expect(component.isSpecial()).toBeTrue();
    });

    it('يرجّع false لأي تقرير عادي', () => {
      component.activeReportId.set('recent-returns');
      expect(component.isSpecial()).toBeFalse();
    });
  });

  describe('activeConfig', () => {
    it('يرجّع إعداد التقرير النشِط الصحيح', () => {
      component.activeReportId.set('negative-stock');
      expect(component.activeConfig?.id).toBe('negative-stock');
    });

    it('يرجّع undefined لتقرير خاص (غير موجود بـstandardReports)', () => {
      component.activeReportId.set('sales-summary');
      expect(component.activeConfig).toBeUndefined();
    });
  });

  describe('selectReport', () => {
    it('يبدّل التقرير النشِط ويرجّع لصفحة 1 ويعيد التحميل', () => {
      component.pageNumber.set(5);
      apiClientSpy.get.calls.reset();

      component.selectReport('negative-stock');

      expect(component.activeReportId()).toBe('negative-stock');
      expect(component.pageNumber()).toBe(1);
      expect(apiClientSpy.get).toHaveBeenCalled();
    });
  });

  describe('loadActiveReport routing', () => {
    it('يحمّل ملخّص المبيعات لتقرير sales-summary', async () => {
      component.activeReportId.set('sales-summary');
      apiClientSpy.get.and.returnValue(
        of({ period: { fromUtc: '', toUtc: '', invoiceCount: 1, totalSales: 10, totalDiscounts: 0, totalReturnedAmount: 0, netRevenue: 10 }, comparisonPeriod: null, netRevenueChangePercent: null })
      );

      await component.loadActiveReport();

      expect(component.salesSummary()).not.toBeNull();
    });

    it('يحمّل تقرير رأس المال لتقرير capital-value', async () => {
      component.activeReportId.set('capital-value');
      apiClientSpy.get.and.returnValue(
        of({ items: { items: [], totalCount: 0 }, totalCapitalValue: 500, productsExcludedNoCostHistory: 0 })
      );

      await component.loadActiveReport();

      expect(component.capitalValue()?.totalCapitalValue).toBe(500);
    });

    it('يحمّل ديون الموردين لتقرير supplier-debts', async () => {
      component.activeReportId.set('supplier-debts');
      apiClientSpy.get.and.returnValue(of({ suppliers: [], grandTotalDebt: 200 }));

      await component.loadActiveReport();

      expect(component.supplierDebts()?.grandTotalDebt).toBe(200);
    });

    it('يحمّل تقرير قياسي عادي عبر endpoint التقارير العام', async () => {
      component.activeReportId.set('negative-stock');
      apiClientSpy.get.and.returnValue(of({ items: [{ productName: 'سكر', quantityOnHand: -3 }], totalCount: 1 }));

      await component.loadActiveReport();

      expect(component.rows().length).toBe(1);
    });

    it('يرفض تحميل تقرير يحتاج فرع بلا فرع محدَّد', async () => {
      component.activeReportId.set('reorder-needed');
      component.selectedBranchId = '';

      await component.loadActiveReport();

      expect(component.errorMessage()).toBe('هذا التقرير يحتاج تحديد فرع أولًا.');
      expect(component.rows()).toEqual([]);
    });

    it('يعرض رسالة خطأ عربية واضحة عند فشل تحميل تقرير قياسي', async () => {
      component.activeReportId.set('negative-stock');
      apiClientSpy.get.and.returnValue(throwError(() => new Error('network')));

      await component.loadActiveReport();

      expect(component.errorMessage()).toBe('تعذّر تحميل التقرير.');
    });

    it('يعرض رسالة خطأ عربية واضحة مختلفة عند فشل ملخّص المبيعات', async () => {
      component.activeReportId.set('sales-summary');
      apiClientSpy.get.and.returnValue(throwError(() => new Error('network')));

      await component.loadActiveReport();

      expect(component.errorMessage()).toBe('تعذّر تحميل ملخّص المبيعات.');
    });
  });

  describe('onPageChanged / onDateRangeChanged / onBranchChanged', () => {
    it('onPageChanged يحدّث الصفحة ويعيد التحميل', () => {
      apiClientSpy.get.calls.reset();
      component.onPageChanged({ pageNumber: 3, pageSize: 50 });
      expect(component.pageNumber()).toBe(3);
      expect(apiClientSpy.get).toHaveBeenCalled();
    });

    it('onDateRangeChanged يرجّع لصفحة 1 ويعيد التحميل', () => {
      component.pageNumber.set(4);
      apiClientSpy.get.calls.reset();
      component.onDateRangeChanged();
      expect(component.pageNumber()).toBe(1);
      expect(apiClientSpy.get).toHaveBeenCalled();
    });

    it('onBranchChanged يرجّع لصفحة 1 ويعيد التحميل', () => {
      component.pageNumber.set(4);
      apiClientSpy.get.calls.reset();
      component.onBranchChanged();
      expect(component.pageNumber()).toBe(1);
      expect(apiClientSpy.get).toHaveBeenCalled();
    });
  });

  describe('formatCell', () => {
    it('يرجّع "—" لقيمة null أو undefined', () => {
      expect(component.formatCell(null, { type: 'text' })).toBe('—');
      expect(component.formatCell(undefined, { type: 'text' })).toBe('—');
    });

    it('يترجم قيمة enum عبر enumMap', () => {
      expect(component.formatCell(2, { type: 'enum', enumMap: { 1: 'طلب الزبون', 2: 'تالف' } })).toBe('تالف');
    });

    it('يرجّع القيمة الخام كنص لو enum بلا خريطة مطابقة', () => {
      expect(component.formatCell(99, { type: 'enum', enumMap: { 1: 'طلب الزبون' } })).toBe('99');
    });

    it('ينسّق القيمة المالية بمنزلتين عشريتين', () => {
      expect(component.formatCell(1000, { type: 'currency' })).toBe('1,000.00');
    });

    it('ينسّق الرقم بلا منازل عشرية إجبارية', () => {
      expect(component.formatCell(1000, { type: 'number' })).toBe('1,000');
    });

    it('يرجّع نص عادي لأي نوع آخر (text)', () => {
      expect(component.formatCell('نص حر', { type: 'text' })).toBe('نص حر');
    });
  });
});
