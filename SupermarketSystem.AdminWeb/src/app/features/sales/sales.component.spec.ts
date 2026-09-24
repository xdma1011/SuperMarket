import { ComponentFixture, TestBed } from '@angular/core/testing';
import { of, throwError } from 'rxjs';
import { SalesComponent } from './sales.component';
import { ApiClient } from '../../core/api/api-client.service';

describe('SalesComponent', () => {
  let fixture: ComponentFixture<SalesComponent>;
  let component: SalesComponent;
  let apiClientSpy: jasmine.SpyObj<ApiClient>;

  beforeEach(async () => {
    apiClientSpy = jasmine.createSpyObj('ApiClient', ['get', 'post']);
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
        of({ items: [{ id: 's1', invoiceNumber: 'INV-1', statusCode: 1, statusTitle: 'مكتملة', totalAmount: 10, totalPaidAmount: 10, totalReturnedAmount: 0, createdAtUtc: '', customerName: null, customerPhone: null }], totalCount: 1 })
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

  describe('remainingDebt وتسديد الدفعة', () => {
    const sampleInvoice = {
      id: 's1', invoiceNumber: 'INV-1', statusCode: 1, statusTitle: 'مكتملة',
      totalAmount: 100, totalPaidAmount: 40, totalReturnedAmount: 0,
      createdAtUtc: '', customerName: 'زبون', customerPhone: '0790000000'
    };

    it('remainingDebt يحسب الفرق بين الإجمالي والمدفوع', () => {
      expect(component.remainingDebt(sampleInvoice)).toBe(60);
    });

    it('remainingDebt صفر لفاتورة ملغاة (دفعاتها معكوسة، مش دين حقيقي)', () => {
      expect(component.remainingDebt({ ...sampleInvoice, statusCode: 2, totalPaidAmount: 0 })).toBe(0);
    });

    it('الفاتورة الملغاة ما بتنكتب عليها "مسدَّدة" بعمود الدين', () => {
      apiClientSpy.get.and.returnValue(of({ items: [], totalCount: 0 }));
      fixture.detectChanges();
      component.invoices.set([{ ...sampleInvoice, statusCode: 2, statusTitle: 'ملغاة', totalPaidAmount: 0 }]);
      fixture.detectChanges();

      const debtCell = (fixture.nativeElement as HTMLElement).querySelectorAll('tbody tr td')[3];
      expect(debtCell.textContent).not.toContain('مسدَّدة');
      expect(debtCell.textContent?.trim()).toBe('—');
    });

    it('openPaymentModal يفتح النافذة ويقترح كامل الدين المتبقي', () => {
      component.openPaymentModal(sampleInvoice);

      expect(component.paymentModalOpen()).toBeTrue();
      expect(component.paymentAmount).toBe(60);
    });

    it('يرفض بلا مبلغ صالح أو طريقة دفع', async () => {
      component.openPaymentModal(sampleInvoice);
      component.paymentAmount = 0;

      await component.submitPayment();

      expect(component.paymentError()).toBe('حدّد مبلغًا موجبًا وطريقة دفع.');
      expect(apiClientSpy.post).not.toHaveBeenCalled();
    });

    it('يرفض مبلغًا أكبر من الدين المتبقي', async () => {
      component.openPaymentModal(sampleInvoice);
      component.paymentAmount = 999;
      component.paymentMethodId = 'pm1';

      await component.submitPayment();

      expect(component.paymentError()).toBe('المبلغ أكبر من الدين المتبقي على هذه الفاتورة.');
      expect(apiClientSpy.post).not.toHaveBeenCalled();
    });

    it('يسجّل الدفعة بنجاح ويغلق النافذة', async () => {
      component.openPaymentModal(sampleInvoice);
      component.paymentMethodId = 'pm1';
      apiClientSpy.post.and.returnValue(of({}));

      await component.submitPayment();

      expect(component.paymentModalOpen()).toBeFalse();
    });

    it('يعرض رسالة الخطأ التفصيلية من الباك إند لو موجودة', async () => {
      component.openPaymentModal(sampleInvoice);
      component.paymentMethodId = 'pm1';
      apiClientSpy.post.and.returnValue(throwError(() => ({ error: { detail: 'المبلغ غير صالح.' } })));

      await component.submitPayment();

      expect(component.paymentError()).toBe('المبلغ غير صالح.');
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
