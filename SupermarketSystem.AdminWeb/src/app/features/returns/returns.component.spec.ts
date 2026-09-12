import { ComponentFixture, TestBed } from '@angular/core/testing';
import { of, throwError } from 'rxjs';
import { ReturnsComponent } from './returns.component';
import { ApiClient } from '../../core/api/api-client.service';

describe('ReturnsComponent', () => {
  let fixture: ComponentFixture<ReturnsComponent>;
  let component: ReturnsComponent;
  let apiClientSpy: jasmine.SpyObj<ApiClient>;

  const sampleInvoiceDetail = {
    id: 'inv1',
    invoiceNumber: 'INV-1',
    statusCode: 1,
    statusTitle: 'مكتملة',
    totalAmount: 30,
    totalReturnedAmount: 0,
    createdAtUtc: '',
    items: [
      { saleInvoiceItemId: 'i1', productId: 'p1', productName: 'سكر', quantity: 5, quantityReturned: 0, unitPriceSnapshot: 2, lineTotal: 10 },
      { saleInvoiceItemId: 'i2', productId: 'p2', productName: 'ملح', quantity: 3, quantityReturned: 3, unitPriceSnapshot: 1, lineTotal: 3 }
    ]
  };

  beforeEach(async () => {
    apiClientSpy = jasmine.createSpyObj('ApiClient', ['get', 'post']);
    apiClientSpy.get.and.returnValue(of([]));

    await TestBed.configureTestingModule({
      imports: [ReturnsComponent],
      providers: [{ provide: ApiClient, useValue: apiClientSpy }]
    }).compileComponents();

    fixture = TestBed.createComponent(ReturnsComponent);
    component = fixture.componentInstance;
  });

  it('يُنشأ المكوّن بنجاح ويحمّل طرق الدفع بالمنشئ', async () => {
    expect(component).toBeTruthy();
  });

  it('يختار أول طريقة دفع تلقائيًا عند تحميلها بنجاح', async () => {
    apiClientSpy.get.and.returnValue(of([{ id: 'pm1', name: 'كاش', requiresExternalReference: false }]));

    fixture = TestBed.createComponent(ReturnsComponent);
    component = fixture.componentInstance;
    await fixture.whenStable();

    expect(component.selectedPaymentMethodId).toBe('pm1');
  });

  describe('search', () => {
    it('لا يبحث لو الاستعلام فاضٍ', async () => {
      component.searchQuery.set('   ');
      await component.search();
      expect(apiClientSpy.get).not.toHaveBeenCalledWith(jasmine.anything(), jasmine.anything(), undefined, jasmine.anything());
    });

    it('يعرض رسالة "لا توجد فاتورة" لو ما في نتائج', async () => {
      component.searchQuery.set('INV-999');
      apiClientSpy.get.and.returnValue(of({ items: [], totalCount: 0 }));

      await component.search();

      expect(component.errorMessage()).toBe('لا توجد فاتورة بهذا الرقم.');
    });

    it('يعرض نتائج البحث عند العثور على فواتير', async () => {
      component.searchQuery.set('INV-1');
      apiClientSpy.get.and.returnValue(of({ items: [{ id: 'inv1', invoiceNumber: 'INV-1', statusCode: 1, statusTitle: 'مكتملة', totalAmount: 30, totalReturnedAmount: 0, createdAtUtc: '' }], totalCount: 1 }));

      await component.search();

      expect(component.searchResults().length).toBe(1);
      expect(component.errorMessage()).toBeNull();
    });

    it('يعرض رسالة خطأ عربية واضحة عند فشل البحث', async () => {
      component.searchQuery.set('INV-1');
      apiClientSpy.get.and.returnValue(throwError(() => new Error('network')));

      await component.search();

      expect(component.errorMessage()).toBe('تعذّر البحث عن الفاتورة.');
    });
  });

  describe('selectInvoice', () => {
    it('يبني returnLines بس للأصناف اللي لسه فيها كمية قابلة للإرجاع', async () => {
      apiClientSpy.get.and.returnValue(of(sampleInvoiceDetail));

      await component.selectInvoice('inv1');

      expect(component.returnLines().length).toBe(1);
      expect(component.returnLines()[0].productName).toBe('سكر');
      expect(component.returnLines()[0].maxReturnable).toBe(5);
    });

    it('يعرض رسالة خطأ عربية واضحة عند فشل تحميل تفاصيل الفاتورة', async () => {
      apiClientSpy.get.and.returnValue(throwError(() => new Error('network')));

      await component.selectInvoice('inv1');

      expect(component.errorMessage()).toBe('تعذّر تحميل تفاصيل الفاتورة.');
    });
  });

  describe('clearSelection', () => {
    it('يمسح كل حالة الاختيار والبحث', async () => {
      apiClientSpy.get.and.returnValue(of(sampleInvoiceDetail));
      await component.selectInvoice('inv1');
      component.searchQuery.set('INV-1');

      component.clearSelection();

      expect(component.selectedInvoice()).toBeNull();
      expect(component.returnLines()).toEqual([]);
      expect(component.searchQuery()).toBe('');
    });
  });

  describe('selectedTotal / hasAnySelectedQuantity', () => {
    beforeEach(async () => {
      apiClientSpy.get.and.returnValue(of(sampleInvoiceDetail));
      await component.selectInvoice('inv1');
    });

    it('selectedTotal يحسب مجموع الكميات المحدَّدة × السعر', () => {
      component.returnLines.update(lines => lines.map(l => ({ ...l, quantity: 2 })));
      expect(component.selectedTotal).toBe(4);
    });

    it('selectedTotal يرجّع صفر بلا أي كمية محدَّدة', () => {
      expect(component.selectedTotal).toBe(0);
    });

    it('hasAnySelectedQuantity يرجّع false بلا أي كمية محدَّدة', () => {
      expect(component.hasAnySelectedQuantity).toBeFalse();
    });

    it('hasAnySelectedQuantity يرجّع true لو صنف واحد على الأقل عنده كمية', () => {
      component.returnLines.update(lines => lines.map((l, i) => (i === 0 ? { ...l, quantity: 1 } : l)));
      expect(component.hasAnySelectedQuantity).toBeTrue();
    });
  });

  describe('submitReturn', () => {
    it('لا يفعل شيئًا لو ما في فاتورة محدَّدة', async () => {
      await component.submitReturn();
      expect(apiClientSpy.post).not.toHaveBeenCalled();
    });

    it('يرفض الإرسال بلا أي كمية إرجاع محدَّدة', async () => {
      apiClientSpy.get.and.returnValue(of(sampleInvoiceDetail));
      await component.selectInvoice('inv1');

      await component.submitReturn();

      expect(component.submitError()).toBe('حدّد كمية إرجاع لصنف واحد على الأقل.');
      expect(apiClientSpy.post).not.toHaveBeenCalled();
    });

    it('يرفض الإرسال بلا طريقة استرجاع محدَّدة', async () => {
      apiClientSpy.get.and.returnValue(of(sampleInvoiceDetail));
      await component.selectInvoice('inv1');
      component.returnLines.update(lines => lines.map(l => ({ ...l, quantity: 1 })));
      component.selectedPaymentMethodId = '';

      await component.submitReturn();

      expect(component.submitError()).toBe('حدّد طريقة الاسترجاع.');
    });

    it('يسجّل الإرجاع بنجاح ويصفّر الاختيار', async () => {
      apiClientSpy.get.and.returnValue(of(sampleInvoiceDetail));
      await component.selectInvoice('inv1');
      component.returnLines.update(lines => lines.map(l => ({ ...l, quantity: 1 })));
      component.selectedPaymentMethodId = 'pm1';
      apiClientSpy.post.and.returnValue(of({}));

      await component.submitReturn();

      expect(component.successMessage()).toBe('تم تسجيل الإرجاع بنجاح.');
      expect(component.selectedInvoice()).toBeNull();
    });

    it('يعرض رسالة الخطأ التفصيلية من الباك إند لو موجودة', async () => {
      apiClientSpy.get.and.returnValue(of(sampleInvoiceDetail));
      await component.selectInvoice('inv1');
      component.returnLines.update(lines => lines.map(l => ({ ...l, quantity: 1 })));
      component.selectedPaymentMethodId = 'pm1';
      apiClientSpy.post.and.returnValue(throwError(() => ({ error: { detail: 'تجاوز الكمية القابلة للإرجاع.' } })));

      await component.submitReturn();

      expect(component.submitError()).toBe('تجاوز الكمية القابلة للإرجاع.');
    });
  });
});
