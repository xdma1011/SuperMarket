import { ComponentFixture, TestBed } from '@angular/core/testing';
import { ActivatedRoute, Router, convertToParamMap } from '@angular/router';
import { of, throwError } from 'rxjs';
import { PurchasingDraftDetailComponent } from './purchasing-draft-detail.component';
import { ApiClient } from '../../core/api/api-client.service';
import { AuthService } from '../../core/services/auth.service';

describe('PurchasingDraftDetailComponent', () => {
  let fixture: ComponentFixture<PurchasingDraftDetailComponent>;
  let component: PurchasingDraftDetailComponent;
  let apiClientSpy: jasmine.SpyObj<ApiClient>;
  let routerSpy: jasmine.SpyObj<Router>;

  const sampleDraft = {
    id: 'd1',
    branchId: 'b1',
    imageReference: 'ref',
    providerName: 'Gemini',
    rawSupplierName: 'مورد خام',
    matchedSupplierId: null,
    supplierInvoiceReference: null,
    invoiceDate: null,
    currency: 'JOD',
    extractedInvoiceTotal: 50,
    extractionConfidence: 'High',
    warnings: [],
    items: [
      {
        rawProductName: 'سكر', quantity: 2, unitOfMeasure: 'كيلو', unitCost: 1, lineTotal: 2,
        matchedProductId: null, matchedProductName: null, matchedProductUnitId: null,
        isBatchTracked: false, newBatchNumber: null, newBatchExpiryDate: null
      }
    ],
    status: 1,
    resultingPurchaseInvoiceId: null,
    paidNowAmount: null,
    paidNowPaymentMethodId: null
  };

  beforeEach(async () => {
    apiClientSpy = jasmine.createSpyObj('ApiClient', ['get', 'put', 'post', 'delete']);
    apiClientSpy.get.and.returnValue(of(sampleDraft));
    routerSpy = jasmine.createSpyObj('Router', ['navigate']);
    routerSpy.navigate.and.resolveTo(true);
    spyOn(window, 'fetch').and.resolveTo(new Response(null, { status: 404 }));

    await TestBed.configureTestingModule({
      imports: [PurchasingDraftDetailComponent],
      providers: [
        { provide: ApiClient, useValue: apiClientSpy },
        { provide: Router, useValue: routerSpy },
        { provide: ActivatedRoute, useValue: { snapshot: { paramMap: convertToParamMap({ id: 'd1' }) } } },
        { provide: AuthService, useValue: jasmine.createSpyObj('AuthService', [], { accessToken: () => 'tok' }) }
      ]
    }).compileComponents();

    fixture = TestBed.createComponent(PurchasingDraftDetailComponent);
    component = fixture.componentInstance;
  });

  it('يُنشأ المكوّن بنجاح', () => {
    expect(component).toBeTruthy();
  });

  it('يحمّل المسودة والموردين وطرق الدفع، ويبني items بحالة بحث محلية فاضية', async () => {
    apiClientSpy.get.and.callFake(((controller: string) => {
      if (controller === 'purchase-invoices') return of(sampleDraft);
      if (controller === 'suppliers') return of({ items: [{ id: 's1', name: 'مورد' }] });
      return of([{ id: 'pm1', name: 'كاش' }]);
    }) as unknown as typeof apiClientSpy.get);

    fixture.detectChanges();
    await fixture.whenStable();

    expect(component.draft()).not.toBeNull();
    expect(component.items.length).toBe(1);
    expect(component.items[0].searchTerm).toBe('');
    expect(component.items[0].searching).toBeFalse();
  });

  it('يعرض رسالة خطأ عربية واضحة عند فشل تحميل المسودة', async () => {
    apiClientSpy.get.and.returnValue(throwError(() => new Error('network')));

    fixture.detectChanges();
    await fixture.whenStable();

    expect(component.errorMessage()).toBe('تعذّر تحميل مسودة الفاتورة.');
  });

  it('فشل تحميل الصورة لا يوقف عرض المراجعة (اختيارية)', async () => {
    (window.fetch as jasmine.Spy).and.rejectWith(new Error('network'));

    fixture.detectChanges();
    await fixture.whenStable();

    expect(component.errorMessage()).toBeNull();
    expect(component.imageUrl()).toBeNull();
  });

  describe('supplierNameOf / paymentMethodNameOf', () => {
    it('supplierNameOf يرجّع اسم المورد الصحيح', () => {
      component.suppliers.set([{ id: 's1', name: 'مورد الألبان' }]);
      expect(component.supplierNameOf('s1')).toBe('مورد الألبان');
    });

    it('supplierNameOf يرجّع "—" لمعرّف غير موجود', () => {
      expect(component.supplierNameOf('s999')).toBe('—');
    });

    it('paymentMethodNameOf يرجّع "—" لمعرّف null', () => {
      expect(component.paymentMethodNameOf(null)).toBe('—');
    });

    it('paymentMethodNameOf يرجّع الاسم الصحيح', () => {
      component.paymentMethods.set([{ id: 'pm1', name: 'كاش' }]);
      expect(component.paymentMethodNameOf('pm1')).toBe('كاش');
    });
  });

  describe('unmatchedCount', () => {
    it('يعدّ الأصناف بلا مطابقة منتج فقط', () => {
      component.items = [
        { rawProductName: 'أ', quantity: 1, unitOfMeasure: null, unitCost: null, lineTotal: null, matchedProductId: null, matchedProductName: null, matchedProductUnitId: null, isBatchTracked: false, newBatchNumber: null, newBatchExpiryDate: null, searchTerm: '', searchResults: [], searching: false, barcodeInput: '' },
        { rawProductName: 'ب', quantity: 1, unitOfMeasure: null, unitCost: null, lineTotal: null, matchedProductId: 'p1', matchedProductName: 'ب', matchedProductUnitId: 'u1', isBatchTracked: false, newBatchNumber: null, newBatchExpiryDate: null, searchTerm: '', searchResults: [], searching: false, barcodeInput: '' }
      ];

      expect(component.unmatchedCount).toBe(1);
    });
  });

  describe('searchProduct', () => {
    function item() {
      return { rawProductName: 'أ', quantity: 1, unitOfMeasure: null, unitCost: null, lineTotal: null, matchedProductId: null, matchedProductName: null, matchedProductUnitId: null, isBatchTracked: false, newBatchNumber: null, newBatchExpiryDate: null, searchTerm: 'سك', searchResults: [], searching: false, barcodeInput: '' };
    }

    it('يمسح النتائج لنص أقل من حرفين', async () => {
      const i = item();
      i.searchTerm = 'س';

      await component.searchProduct(i);

      expect(i.searchResults).toEqual([]);
      expect(apiClientSpy.get).not.toHaveBeenCalledWith(jasmine.anything(), 'products-search', jasmine.anything(), jasmine.anything());
    });

    it('يبحث ويحدّث searchResults بنجاح', async () => {
      apiClientSpy.get.and.returnValue(of({ items: [{ id: 'p1', name: 'سكر', isBatchTracked: false }] }));
      const i = item();

      await component.searchProduct(i);

      expect(i.searchResults.length).toBe(1);
      expect(i.searching).toBeFalse();
    });

    it('يمسح النتائج بهدوء عند فشل البحث', async () => {
      apiClientSpy.get.and.returnValue(throwError(() => new Error('network')));
      const i = item();

      await component.searchProduct(i);

      expect(i.searchResults).toEqual([]);
    });
  });

  describe('searchBarcode', () => {
    function item() {
      return {
        rawProductName: 'أ', quantity: 1, unitOfMeasure: null, unitCost: null, lineTotal: null,
        matchedProductId: null as string | null, matchedProductName: null as string | null,
        matchedProductUnitId: null as string | null, isBatchTracked: false, newBatchNumber: null, newBatchExpiryDate: null,
        searchTerm: '', searchResults: [], searching: false, barcodeInput: '123456'
      };
    }

    it('لا يفعل شيئًا لباركود فاضٍ', async () => {
      const i = item();
      i.barcodeInput = '   ';

      await component.searchBarcode(i);

      expect(apiClientSpy.get).not.toHaveBeenCalled();
    });

    it('يطابق المنتج ويمسح حقل الباركود عند النجاح', async () => {
      apiClientSpy.get.and.callFake(((controller: string, operation: string) => {
        if (operation === 'by-barcode/{barcodeValue}') return of({ id: 'p1', name: 'سكر', isBatchTracked: false });
        return of([{ id: 'u1', unitName: 'كيلو', isBaseUnit: true }]);
      }) as unknown as typeof apiClientSpy.get);
      const i = item();

      await component.searchBarcode(i);

      expect(i.matchedProductId).toBe('p1');
      expect(i.barcodeInput).toBe('');
    });

    it('يعرض رسالة خطأ عربية تحمل الباركود لو ما لقى منتج', async () => {
      apiClientSpy.get.and.returnValue(throwError(() => new Error('not found')));
      const i = item();

      await component.searchBarcode(i);

      expect(component.errorMessage()).toBe('لم يُعثر على منتج بالباركود "123456".');
    });
  });

  describe('selectProduct / clearMatch', () => {
    function item() {
      return {
        rawProductName: 'أ', quantity: 1, unitOfMeasure: null, unitCost: null, lineTotal: null,
        matchedProductId: null as string | null, matchedProductName: null as string | null,
        matchedProductUnitId: null as string | null, isBatchTracked: false, newBatchNumber: null, newBatchExpiryDate: null,
        searchTerm: 'سك', searchResults: [{ id: 'p1', name: 'سكر', isBatchTracked: false }], searching: false, barcodeInput: ''
      };
    }

    it('selectProduct يعبّي بيانات المطابقة ويختار الوحدة الأساسية', async () => {
      apiClientSpy.get.and.returnValue(of([{ id: 'u1', unitName: 'كيلو', isBaseUnit: true }]));
      const i = item();

      await component.selectProduct(i, { id: 'p1', name: 'سكر', isBatchTracked: false });

      expect(i.matchedProductId).toBe('p1');
      expect(i.matchedProductUnitId).toBe('u1');
      expect(i.searchTerm).toBe('');
      expect(i.searchResults).toEqual([]);
    });

    it('selectProduct يضبط matchedProductUnitId=null بهدوء لو فشل جلب الوحدات', async () => {
      apiClientSpy.get.and.returnValue(throwError(() => new Error('network')));
      const i = item();

      await component.selectProduct(i, { id: 'p1', name: 'سكر', isBatchTracked: false });

      expect(i.matchedProductUnitId).toBeNull();
    });

    it('clearMatch يمسح كل بيانات المطابقة', () => {
      const i = { ...item(), matchedProductId: 'p1', matchedProductName: 'سكر', matchedProductUnitId: 'u1', isBatchTracked: true };

      component.clearMatch(i);

      expect(i.matchedProductId).toBeNull();
      expect(i.matchedProductName).toBeNull();
      expect(i.matchedProductUnitId).toBeNull();
      expect(i.isBatchTracked).toBeFalse();
    });
  });

  describe('save', () => {
    it('يحفظ التعديلات بنجاح ويرجّع true', async () => {
      apiClientSpy.put.and.returnValue(of({}));

      const result = await component.save();

      expect(result).toBeTrue();
      expect(component.actionMessage()).toBe('تم حفظ التعديلات.');
    });

    it('يعرض رسالة خطأ ويرجّع false عند الفشل', async () => {
      apiClientSpy.put.and.returnValue(throwError(() => ({ error: { detail: 'خطأ بالبيانات.' } })));

      const result = await component.save();

      expect(result).toBeFalse();
      expect(component.errorMessage()).toBe('خطأ بالبيانات.');
    });
  });

  describe('complete', () => {
    it('يرفض الاعتماد لو صنف متتبَّع دفعات بلا رقم دفعة', async () => {
      component.items = [
        { rawProductName: 'أ', quantity: 1, unitOfMeasure: null, unitCost: null, lineTotal: null, matchedProductId: 'p1', matchedProductName: 'منتج دفعات', matchedProductUnitId: 'u1', isBatchTracked: true, newBatchNumber: '', newBatchExpiryDate: null, searchTerm: '', searchResults: [], searching: false, barcodeInput: '' }
      ];

      await component.complete();

      expect(component.errorMessage()).toBe('الصنف "منتج دفعات" يتتبّع دفعات - أدخل رقم الدفعة له قبل الاعتماد.');
      expect(apiClientSpy.put).not.toHaveBeenCalled();
    });

    it('يحفظ ثم يعتمد وينتقل للمشتريات عند النجاح الكامل', async () => {
      apiClientSpy.put.and.returnValue(of({}));
      apiClientSpy.post.and.returnValue(of({}));

      await component.complete();

      expect(routerSpy.navigate).toHaveBeenCalledWith(['/purchases']);
    });

    it('لا يحاول الاعتماد لو فشل الحفظ أولًا', async () => {
      apiClientSpy.put.and.returnValue(throwError(() => new Error('network')));

      await component.complete();

      expect(apiClientSpy.post).not.toHaveBeenCalled();
      expect(routerSpy.navigate).not.toHaveBeenCalled();
    });

    it('يعرض رسالة الخطأ التفصيلية لو فشل الاعتماد نفسه بعد نجاح الحفظ', async () => {
      apiClientSpy.put.and.returnValue(of({}));
      apiClientSpy.post.and.returnValue(throwError(() => ({ error: { detail: 'الفاتورة غير صالحة للاعتماد.' } })));

      await component.complete();

      expect(component.errorMessage()).toBe('الفاتورة غير صالحة للاعتماد.');
    });
  });

  describe('discard', () => {
    it('يحذف المسودة وينتقل لقائمة المسودات عند النجاح', async () => {
      apiClientSpy.delete.and.returnValue(of({}));

      await component.discard();

      expect(routerSpy.navigate).toHaveBeenCalledWith(['/purchases/drafts']);
    });

    it('يعرض رسالة خطأ عربية واضحة عند الفشل', async () => {
      apiClientSpy.delete.and.returnValue(throwError(() => new Error('network')));

      await component.discard();

      expect(component.errorMessage()).toBe('تعذّر تجاهل المسودة.');
    });
  });
});
