import { ComponentFixture, TestBed } from '@angular/core/testing';
import { of, throwError } from 'rxjs';
import { PurchasingComponent } from './purchasing.component';
import { ApiClient } from '../../core/api/api-client.service';

describe('PurchasingComponent', () => {
  let fixture: ComponentFixture<PurchasingComponent>;
  let component: PurchasingComponent;
  let apiClientSpy: jasmine.SpyObj<ApiClient>;

  const product = { id: 'p1', name: 'سكر', isBatchTracked: false };
  const batchedProduct = { id: 'p2', name: 'لبن', isBatchTracked: true };
  const sampleInvoice = { id: 'inv1', invoiceNumber: 'PI-1', supplierInvoiceReference: null, supplierName: 'مورد', status: 1, totalAmount: 100, totalPaidAmount: 40, createdAtUtc: '' };

  function mockLoadAllSuccess(products = [product]) {
    apiClientSpy.get.and.callFake(((controller: string) => {
      if (controller === 'suppliers') return of({ items: [{ id: 's1', name: 'مورد1' }], totalCount: 1 });
      if (controller === 'branches') return of({ items: [{ id: 'b1', name: 'الرئيسي' }], totalCount: 1 });
      if (controller === 'products') return of({ items: products, totalCount: products.length });
      if (controller === 'payment-methods') return of([{ id: 'pm1', name: 'كاش' }]);
      return of({ items: [], totalCount: 0 });
    }) as unknown as typeof apiClientSpy.get);
  }

  beforeEach(async () => {
    apiClientSpy = jasmine.createSpyObj('ApiClient', ['get', 'post']);
    apiClientSpy.get.and.returnValue(of({ items: [], totalCount: 0 }));

    await TestBed.configureTestingModule({
      imports: [PurchasingComponent],
      providers: [{ provide: ApiClient, useValue: apiClientSpy }]
    }).compileComponents();

    fixture = TestBed.createComponent(PurchasingComponent);
    component = fixture.componentInstance;
  });

  it('يُنشأ المكوّن بنجاح', () => {
    expect(component).toBeTruthy();
  });

  it('يحمّل كل بيانات الإقلاع ويختار أول عنصر بكل قائمة', async () => {
    mockLoadAllSuccess();

    fixture.detectChanges();
    await fixture.whenStable();

    expect(component.selectedSupplierId).toBe('s1');
    expect(component.selectedBranchId).toBe('b1');
    expect(component.newLineProductId).toBe('p1');
    expect(component.paymentMethodId).toBe('pm1');
  });

  it('يعرض رسالة خطأ عربية واضحة عند فشل التحميل', async () => {
    apiClientSpy.get.and.returnValue(throwError(() => new Error('network')));

    fixture.detectChanges();
    await fixture.whenStable();

    expect(component.errorMessage()).toBe('تعذّر تحميل بيانات المشتريات.');
  });

  describe('supplierNameOf', () => {
    it('يرجّع اسم المورد الصحيح من المعرّف', () => {
      component.suppliers.set([{ id: 's1', name: 'مورد الألبان' }]);
      expect(component.supplierNameOf('s1')).toBe('مورد الألبان');
    });

    it('يرجّع "—" لمعرّف غير موجود', () => {
      expect(component.supplierNameOf('s999')).toBe('—');
    });
  });

  describe('openForm / closeForm', () => {
    it('openForm يفتح النموذج ويصفّر الأسطر', () => {
      component.lines = [{ productId: 'p1', productName: 'سكر', unitId: 'u1', units: [], quantity: 1, unitCost: 1, isBatchTracked: false, newBatchNumber: '', newBatchExpiryDate: '' }];

      component.openForm();

      expect(component.formOpen()).toBeTrue();
      expect(component.lines).toEqual([]);
    });

    it('closeForm يصفّر المرجع والأسطر', () => {
      component.supplierInvoiceReference = 'REF-1';
      component.lines = [{ productId: 'p1', productName: 'سكر', unitId: 'u1', units: [], quantity: 1, unitCost: 1, isBatchTracked: false, newBatchNumber: '', newBatchExpiryDate: '' }];

      component.closeForm();

      expect(component.formOpen()).toBeFalse();
      expect(component.supplierInvoiceReference).toBe('');
      expect(component.lines).toEqual([]);
    });
  });

  describe('addLine', () => {
    beforeEach(() => mockLoadAllSuccess([product, batchedProduct]));

    it('يضيف سطرًا جديدًا بالوحدة الأساسية', async () => {
      fixture.detectChanges();
      await fixture.whenStable();
      component.newLineProductId = 'p1';
      apiClientSpy.get.and.returnValue(of([{ id: 'u1', unitName: 'كيلو', isBaseUnit: true }]));

      await component.addLine();

      expect(component.lines.length).toBe(1);
      expect(component.lines[0].isBatchTracked).toBeFalse();
    });

    it('يعلّم السطر isBatchTracked=true لمنتج متتبَّع دفعات', async () => {
      fixture.detectChanges();
      await fixture.whenStable();
      component.newLineProductId = 'p2';
      apiClientSpy.get.and.returnValue(of([{ id: 'u1', unitName: 'قطعة', isBaseUnit: true }]));

      await component.addLine();

      expect(component.lines[0].isBatchTracked).toBeTrue();
    });

    it('يعرض رسالة خطأ عربية واضحة لو المنتج بلا وحدة معرَّفة', async () => {
      fixture.detectChanges();
      await fixture.whenStable();
      component.newLineProductId = 'p1';
      apiClientSpy.get.and.returnValue(of([]));

      await component.addLine();

      expect(component.formError()).toBe('المنتج "سكر" ليس له وحدة معرَّفة.');
    });

    it('يعرض رسالة خطأ عربية واضحة عند فشل جلب الوحدات', async () => {
      fixture.detectChanges();
      await fixture.whenStable();
      component.newLineProductId = 'p1';
      apiClientSpy.get.and.returnValue(throwError(() => new Error('network')));

      await component.addLine();

      expect(component.formError()).toBe('تعذّر جلب وحدات المنتج.');
    });
  });

  describe('removeLine / lineTotal / invoiceTotal', () => {
    it('removeLine يحذف السطر بالمؤشر الصحيح', () => {
      component.lines = [
        { productId: 'p1', productName: 'أ', unitId: 'u1', units: [], quantity: 1, unitCost: 5, isBatchTracked: false, newBatchNumber: '', newBatchExpiryDate: '' },
        { productId: 'p2', productName: 'ب', unitId: 'u1', units: [], quantity: 2, unitCost: 3, isBatchTracked: false, newBatchNumber: '', newBatchExpiryDate: '' }
      ];

      component.removeLine(0);

      expect(component.lines.length).toBe(1);
      expect(component.lines[0].productId).toBe('p2');
    });

    it('lineTotal يحسب كمية × تكلفة الوحدة', () => {
      const line = { productId: 'p1', productName: 'أ', unitId: 'u1', units: [], quantity: 4, unitCost: 2.5, isBatchTracked: false, newBatchNumber: '', newBatchExpiryDate: '' };
      expect(component.lineTotal(line)).toBe(10);
    });

    it('invoiceTotal يجمع كل الأسطر', () => {
      component.lines = [
        { productId: 'p1', productName: 'أ', unitId: 'u1', units: [], quantity: 2, unitCost: 5, isBatchTracked: false, newBatchNumber: '', newBatchExpiryDate: '' },
        { productId: 'p2', productName: 'ب', unitId: 'u1', units: [], quantity: 3, unitCost: 2, isBatchTracked: false, newBatchNumber: '', newBatchExpiryDate: '' }
      ];

      expect(component.invoiceTotal).toBe(16);
    });

    it('invoiceTotal يرجّع صفر بلا أي سطر', () => {
      component.lines = [];
      expect(component.invoiceTotal).toBe(0);
    });
  });

  describe('submit', () => {
    const validLine = { productId: 'p1', productName: 'سكر', unitId: 'u1', units: [], quantity: 2, unitCost: 5, isBatchTracked: false, newBatchNumber: '', newBatchExpiryDate: '' };

    it('يرفض بلا مورد أو فرع أو أسطر', async () => {
      component.selectedSupplierId = '';
      await component.submit();
      expect(component.formError()).toBe('حدّد المورد والفرع، وأضف سطرًا واحدًا على الأقل.');
      expect(apiClientSpy.post).not.toHaveBeenCalled();
    });

    it('يرفض كمية صفر أو تكلفة سالبة', async () => {
      component.selectedSupplierId = 's1';
      component.selectedBranchId = 'b1';
      component.lines = [{ ...validLine, unitCost: -1 }];

      await component.submit();

      expect(component.formError()).toBe('كل سطر يحتاج كمية موجبة وتكلفة غير سالبة.');
    });

    it('يرفض صنف متتبَّع دفعات بلا رقم دفعة', async () => {
      component.selectedSupplierId = 's1';
      component.selectedBranchId = 'b1';
      component.lines = [{ ...validLine, isBatchTracked: true, newBatchNumber: '' }];

      await component.submit();

      expect(component.formError()).toBe('الصنف "سكر" يتتبّع دفعات — أدخل رقم الدفعة له.');
    });

    it('يرسل الفاتورة بنجاح ويغلق النموذج', async () => {
      component.selectedSupplierId = 's1';
      component.selectedBranchId = 'b1';
      component.lines = [validLine];
      apiClientSpy.post.and.returnValue(of({}));

      await component.submit();

      expect(component.formOpen()).toBeFalse();
    });

    it('يعرض رسالة الخطأ التفصيلية من الباك إند لو موجودة', async () => {
      component.selectedSupplierId = 's1';
      component.selectedBranchId = 'b1';
      component.lines = [validLine];
      apiClientSpy.post.and.returnValue(throwError(() => ({ error: { detail: 'تكلفة الوحدة غير منطقية.' } })));

      await component.submit();

      expect(component.formError()).toBe('تكلفة الوحدة غير منطقية.');
    });
  });

  describe('remainingDebt', () => {
    it('يحسب الدين المتبقي = الإجمالي - المدفوع', () => {
      expect(component.remainingDebt(sampleInvoice)).toBe(60);
    });
  });

  describe('openPaymentModal / closePaymentModal', () => {
    it('openPaymentModal يعبّي المبلغ بالدين المتبقي', () => {
      component.openPaymentModal(sampleInvoice);

      expect(component.paymentAmount).toBe(60);
      expect(component.paymentModalOpen()).toBeTrue();
    });

    it('closePaymentModal يصفّر الهدف والمبلغ', () => {
      component.openPaymentModal(sampleInvoice);
      component.closePaymentModal();

      expect(component.paymentModalOpen()).toBeFalse();
      expect(component.paymentAmount).toBeNull();
    });
  });

  describe('submitPayment', () => {
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
});
