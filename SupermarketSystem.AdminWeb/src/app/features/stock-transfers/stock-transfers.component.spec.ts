import { ComponentFixture, TestBed } from '@angular/core/testing';
import { of, throwError } from 'rxjs';
import { StockTransfersComponent } from './stock-transfers.component';
import { ApiClient } from '../../core/api/api-client.service';

describe('StockTransfersComponent', () => {
  let fixture: ComponentFixture<StockTransfersComponent>;
  let component: StockTransfersComponent;
  let apiClientSpy: jasmine.SpyObj<ApiClient>;

  const product = { id: 'p1', name: 'سكر', isBatchTracked: false };
  const batchedProduct = { id: 'p2', name: 'لبن', isBatchTracked: true };

  function mockLoadAllSuccess(products = [product]) {
    apiClientSpy.get.and.callFake(((controller: string) => {
      if (controller === 'branches') return of({ items: [{ id: 'b1', name: 'الرئيسي' }, { id: 'b2', name: 'الفرعي' }], totalCount: 2 });
      if (controller === 'products') return of({ items: products, totalCount: products.length });
      return of({ items: [], totalCount: 0 });
    }) as unknown as typeof apiClientSpy.get);
  }

  beforeEach(async () => {
    apiClientSpy = jasmine.createSpyObj('ApiClient', ['get', 'post']);
    apiClientSpy.get.and.returnValue(of({ items: [], totalCount: 0 }));

    await TestBed.configureTestingModule({
      imports: [StockTransfersComponent],
      providers: [{ provide: ApiClient, useValue: apiClientSpy }]
    }).compileComponents();

    fixture = TestBed.createComponent(StockTransfersComponent);
    component = fixture.componentInstance;
  });

  it('يُنشأ المكوّن بنجاح', () => {
    expect(component).toBeTruthy();
  });

  it('يختار فرعين مختلفين (مصدر/وجهة) تلقائيًا لو متوفرين', async () => {
    mockLoadAllSuccess();

    fixture.detectChanges();
    await fixture.whenStable();

    expect(component.sourceBranchId).toBe('b1');
    expect(component.destinationBranchId).toBe('b2');
    expect(component.newLineProductId).toBe('p1');
  });

  it('يستخدم نفس الفرع لمصدر ووجهة لو فرع واحد فقط متوفر', async () => {
    apiClientSpy.get.and.callFake(((controller: string) => {
      if (controller === 'branches') return of({ items: [{ id: 'b1', name: 'الوحيد' }], totalCount: 1 });
      return of({ items: [], totalCount: 0 });
    }) as unknown as typeof apiClientSpy.get);

    fixture.detectChanges();
    await fixture.whenStable();

    expect(component.sourceBranchId).toBe('b1');
    expect(component.destinationBranchId).toBe('b1');
  });

  it('يعرض رسالة خطأ عربية واضحة عند فشل التحميل', async () => {
    apiClientSpy.get.and.returnValue(throwError(() => new Error('network')));

    fixture.detectChanges();
    await fixture.whenStable();

    expect(component.errorMessage()).toBe('تعذّر تحميل عمليات النقل.');
  });

  describe('openForm / closeForm', () => {
    it('openForm يفتح النموذج ويصفّر الأسطر', () => {
      component.lines = [{ productId: 'p1', productName: 'سكر', isBatchTracked: false, unitId: 'u1', units: [], quantity: 1, batches: [], selectedBatchId: '' }];

      component.openForm();

      expect(component.formOpen()).toBeTrue();
      expect(component.lines).toEqual([]);
    });

    it('closeForm يغلق النموذج', () => {
      component.formOpen.set(true);
      component.closeForm();
      expect(component.formOpen()).toBeFalse();
    });
  });

  describe('addLine', () => {
    beforeEach(() => {
      mockLoadAllSuccess([product, batchedProduct]);
    });

    it('لا يضيف شيئًا بلا منتج أو فرع مصدر محدَّد', async () => {
      component.newLineProductId = '';
      component.sourceBranchId = 'b1';

      await component.addLine();

      expect(component.lines.length).toBe(0);
    });

    it('يضيف سطرًا جديدًا لمنتج غير متتبَّع بالدفعات، ويختار الوحدة الأساسية', async () => {
      fixture.detectChanges();
      await fixture.whenStable();
      component.newLineProductId = 'p1';
      apiClientSpy.get.and.returnValue(of([{ id: 'u1', unitName: 'كيلو', isBaseUnit: true }]));

      await component.addLine();

      expect(component.lines.length).toBe(1);
      expect(component.lines[0].unitId).toBe('u1');
      expect(component.lines[0].isBatchTracked).toBeFalse();
    });

    it('يعرض رسالة خطأ عربية واضحة لو المنتج بلا وحدة معرَّفة', async () => {
      fixture.detectChanges();
      await fixture.whenStable();
      component.newLineProductId = 'p1';
      apiClientSpy.get.and.returnValue(of([]));

      await component.addLine();

      expect(component.formError()).toBe('المنتج "سكر" ليس له وحدة معرَّفة.');
      expect(component.lines.length).toBe(0);
    });

    it('يجلب الدفعات لمنتج متتبَّع بالدفعات ويختار أول دفعة', async () => {
      fixture.detectChanges();
      await fixture.whenStable();
      component.newLineProductId = 'p2';
      apiClientSpy.get.and.callFake(((controller: string, operation: string) => {
        if (operation === '{productId}/units') return of([{ id: 'u1', unitName: 'قطعة', isBaseUnit: true }]);
        if (controller === 'stock-transfers') return of([{ productBatchId: 'batch1', batchNumber: 'B1', expiryDate: null, quantityOnHand: 5 }]);
        return of([]);
      }) as unknown as typeof apiClientSpy.get);

      await component.addLine();

      expect(component.lines[0].selectedBatchId).toBe('batch1');
    });

    it('يعرض رسالة خطأ عربية واضحة لو ما في دفعات برصيد بالفرع المصدر', async () => {
      fixture.detectChanges();
      await fixture.whenStable();
      component.newLineProductId = 'p2';
      apiClientSpy.get.and.callFake(((controller: string, operation: string) => {
        if (operation === '{productId}/units') return of([{ id: 'u1', unitName: 'قطعة', isBaseUnit: true }]);
        if (controller === 'stock-transfers') return of([]);
        return of([]);
      }) as unknown as typeof apiClientSpy.get);

      await component.addLine();

      expect(component.formError()).toBe('المنتج "لبن" يتتبّع دفعات - لا يوجد أي دفعة فيها رصيد بالفرع المصدر المختار.');
      expect(component.lines.length).toBe(0);
    });

    it('يعرض رسالة خطأ عربية واضحة عند فشل جلب بيانات المنتج', async () => {
      fixture.detectChanges();
      await fixture.whenStable();
      component.newLineProductId = 'p1';
      apiClientSpy.get.and.returnValue(throwError(() => new Error('network')));

      await component.addLine();

      expect(component.formError()).toBe('تعذّر جلب بيانات المنتج.');
    });
  });

  describe('removeLine', () => {
    it('يحذف السطر بالمؤشر الصحيح بس', () => {
      component.lines = [
        { productId: 'p1', productName: 'أ', isBatchTracked: false, unitId: 'u1', units: [], quantity: 1, batches: [], selectedBatchId: '' },
        { productId: 'p2', productName: 'ب', isBatchTracked: false, unitId: 'u1', units: [], quantity: 1, batches: [], selectedBatchId: '' }
      ];

      component.removeLine(0);

      expect(component.lines.length).toBe(1);
      expect(component.lines[0].productId).toBe('p2');
    });
  });

  describe('submit', () => {
    const validLine = { productId: 'p1', productName: 'سكر', isBatchTracked: false, unitId: 'u1', units: [], quantity: 2, batches: [], selectedBatchId: '' };

    it('يرفض بلا فرع مصدر أو وجهة', async () => {
      component.sourceBranchId = '';
      component.destinationBranchId = '';

      await component.submit();

      expect(component.formError()).toBe('حدّد الفرع المصدر والوجهة.');
      expect(apiClientSpy.post).not.toHaveBeenCalled();
    });

    it('يرفض لو المصدر والوجهة نفس الفرع', async () => {
      component.sourceBranchId = 'b1';
      component.destinationBranchId = 'b1';

      await component.submit();

      expect(component.formError()).toBe('الفرع المصدر والوجهة لازم يكونوا مختلفين.');
    });

    it('يرفض بلا أي سطر', async () => {
      component.sourceBranchId = 'b1';
      component.destinationBranchId = 'b2';
      component.lines = [];

      await component.submit();

      expect(component.formError()).toBe('أضف سطرًا واحدًا على الأقل.');
    });

    it('يرفض سطرًا بكمية صفر أو سالبة', async () => {
      component.sourceBranchId = 'b1';
      component.destinationBranchId = 'b2';
      component.lines = [{ ...validLine, quantity: 0 }];

      await component.submit();

      expect(component.formError()).toBe('كل سطر يحتاج كمية موجبة.');
    });

    it('يرفض صنف متتبَّع دفعات بلا دفعة محدَّدة', async () => {
      component.sourceBranchId = 'b1';
      component.destinationBranchId = 'b2';
      component.lines = [{ ...validLine, isBatchTracked: true, selectedBatchId: '' }];

      await component.submit();

      expect(component.formError()).toBe('حدّد الدفعة لكل صنف يتتبّع دفعات.');
    });

    it('يرسل النقل بنجاح ويغلق النموذج', async () => {
      component.sourceBranchId = 'b1';
      component.destinationBranchId = 'b2';
      component.lines = [validLine];
      apiClientSpy.post.and.returnValue(of({}));

      await component.submit();

      expect(component.formOpen()).toBeFalse();
    });

    it('يعرض رسالة الخطأ التفصيلية من الباك إند لو موجودة', async () => {
      component.sourceBranchId = 'b1';
      component.destinationBranchId = 'b2';
      component.lines = [validLine];
      apiClientSpy.post.and.returnValue(throwError(() => ({ error: { detail: 'رصيد المخزون بالمصدر غير كافٍ.' } })));

      await component.submit();

      expect(component.formError()).toBe('رصيد المخزون بالمصدر غير كافٍ.');
    });
  });

  describe('openReceiveModal / closeReceiveModal / confirmReceive', () => {
    const transfer = { id: 't1', transferNumber: 'ST-1', sourceBranchName: 'أ', destinationBranchName: 'ب', statusCode: 1, statusTitle: 'قيد النقل', dispatchedAtUtc: '', receivedAtUtc: null, itemCount: 1 };

    it('openReceiveModal يحمّل التفاصيل بنجاح', async () => {
      apiClientSpy.get.and.returnValue(of({ id: 't1', transferNumber: 'ST-1', sourceBranchName: 'أ', destinationBranchName: 'ب', statusTitle: 'قيد النقل', items: [] }));

      await component.openReceiveModal(transfer);

      expect(component.receiveModalOpen()).toBeTrue();
      expect(component.receiveDetail()).not.toBeNull();
    });

    it('openReceiveModal يعرض رسالة خطأ عربية واضحة عند الفشل', async () => {
      apiClientSpy.get.and.returnValue(throwError(() => new Error('network')));

      await component.openReceiveModal(transfer);

      expect(component.receiveError()).toBe('تعذّر تحميل تفاصيل عملية النقل.');
    });

    it('closeReceiveModal يغلق النافذة', () => {
      component.receiveModalOpen.set(true);
      component.closeReceiveModal();
      expect(component.receiveModalOpen()).toBeFalse();
    });

    it('confirmReceive يؤكد الاستلام ويغلق النافذة عند النجاح', async () => {
      apiClientSpy.get.and.returnValue(of({ id: 't1', transferNumber: 'ST-1', sourceBranchName: 'أ', destinationBranchName: 'ب', statusTitle: 'قيد النقل', items: [] }));
      await component.openReceiveModal(transfer);
      apiClientSpy.post.and.returnValue(of({}));

      await component.confirmReceive();

      expect(component.receiveModalOpen()).toBeFalse();
    });

    it('confirmReceive يعرض رسالة الخطأ التفصيلية من الباك إند لو موجودة', async () => {
      apiClientSpy.post.and.returnValue(throwError(() => ({ error: { detail: 'عملية النقل مستلَمة أصلًا.' } })));

      await component.confirmReceive();

      expect(component.receiveError()).toBe('عملية النقل مستلَمة أصلًا.');
    });
  });
});
