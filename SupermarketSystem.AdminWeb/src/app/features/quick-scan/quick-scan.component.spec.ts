import { ComponentFixture, TestBed } from '@angular/core/testing';
import { of, throwError } from 'rxjs';
import { QuickScanComponent, ScanLookupItem } from './quick-scan.component';
import { ApiClient } from '../../core/api/api-client.service';
import { AuthService } from '../../core/services/auth.service';
import { PermissionsService } from '../../core/services/permissions.service';

const MILK: ScanLookupItem = {
  productId: 'p1', productUnitId: 'u1', productName: 'حليب', unitName: 'حبة', unitPrice: 1.25, isBatchTracked: false, barcode: '625'
};
const RICE: ScanLookupItem = {
  productId: 'p2', productUnitId: 'u2', productName: 'رز', unitName: 'كيس', unitPrice: 3.5, isBatchTracked: false, barcode: null
};

describe('QuickScanComponent', () => {
  let fixture: ComponentFixture<QuickScanComponent>;
  let component: QuickScanComponent;
  let apiClientSpy: jasmine.SpyObj<ApiClient>;
  let atCostAllowed: boolean;

  beforeEach(async () => {
    atCostAllowed = true;
    apiClientSpy = jasmine.createSpyObj('ApiClient', ['get', 'post']);
    apiClientSpy.get.and.returnValue(of([]));

    const authStub = {
      getPublicBranches: () => Promise.resolve([{ id: 'b1', name: 'الرئيسي' }]),
      defaultBranchId: (branches: { id: string }[]) => branches[0]?.id ?? '',
      currentBranchId: () => 'b1'
    };
    const permissionsStub = { has: (code: string) => code !== 'Sales.AtCostWithdrawal' || atCostAllowed, loaded: () => true };

    await TestBed.configureTestingModule({
      imports: [QuickScanComponent],
      providers: [
        { provide: ApiClient, useValue: apiClientSpy },
        { provide: AuthService, useValue: authStub },
        { provide: PermissionsService, useValue: permissionsStub }
      ]
    }).compileComponents();

    fixture = TestBed.createComponent(QuickScanComponent);
    component = fixture.componentInstance;
    fixture.detectChanges();
    await fixture.whenStable();
  });

  it('بياخد فرع المستخدم وبيجيب الطلبات الجاهزة المفتوحة', () => {
    expect(component.branchId).toBe('b1');
    expect(apiClientSpy.get).toHaveBeenCalledWith('prepared-orders' as never, '', undefined, { branchId: 'b1' });
  });

  it('باركود بنتيجة وحدة بينضاف للسلة، ونفس الصنف مرة تانية بيزيد الكمية', async () => {
    apiClientSpy.get.and.returnValue(of([MILK]));
    component.term = '625';
    await component.onSubmitTerm();
    component.term = '625';
    await component.onSubmitTerm();

    expect(component.lines().length).toBe(1);
    expect(component.lines()[0].quantity).toBe(2);
    expect(component.estimatedTotal()).toBeCloseTo(2.5, 3);
    expect(component.term).toBe('');
  });

  it('بحث بالاسم بأكتر من نتيجة بيعرض خيارات بدل ما يضيف', async () => {
    apiClientSpy.get.and.returnValue(of([MILK, RICE]));
    component.term = 'ح';
    await component.onSubmitTerm();

    expect(component.lines().length).toBe(0);
    expect(component.choices().length).toBe(2);

    component.addItem(RICE);
    expect(component.lines()[0].productName).toBe('رز');
    expect(component.choices().length).toBe(0);
  });

  it('ما في نتيجة = رسالة عربية واضحة', async () => {
    apiClientSpy.get.and.returnValue(of([]));
    component.term = 'xyz';
    await component.onSubmitTerm();
    expect(component.errorMessage()).toContain('ما لقيت');
  });

  it('نفس الكود من الكاميرا خلال ثانية ونص بينحسب مرة وحدة', async () => {
    apiClientSpy.get.and.returnValue(of([MILK]));
    component.onCameraScanned('625');
    component.onCameraScanned('625');
    await fixture.whenStable();
    expect(component.lines()[0].quantity).toBe(1);
  });

  it('كمية صفر بتحذف السطر', () => {
    component.addItem(MILK);
    component.setQuantity(component.lines()[0], 0);
    expect(component.lines().length).toBe(0);
  });

  it('إرسال للكاشير بيبعت الأصناف وبيعرض رقم الطلب وبيفرّغ السلة', async () => {
    component.addItem(MILK);
    component.setQuantity(component.lines()[0], 3);
    component.note = 'الكيس الأزرق';
    apiClientSpy.post.and.returnValue(of({ preparedOrderId: 'o1', ticketNumber: 17, itemCount: 1, estimatedTotal: 3.75 }));

    await component.sendToCashier();

    expect(apiClientSpy.post).toHaveBeenCalledWith('prepared-orders' as never, '', {
      branchId: 'b1', note: 'الكيس الأزرق', items: [{ productId: 'p1', productUnitId: 'u1', quantity: 3 }]
    });
    expect(component.lastTicket()?.ticketNumber).toBe(17);
    expect(component.lines().length).toBe(0);
  });

  it('فشل الإرسال بيعرض رسالة السيرفر', async () => {
    component.addItem(MILK);
    apiClientSpy.post.and.returnValue(throwError(() => ({ error: { detail: 'ما بتقدر تجهّز طلب لفرع غير فرعك.' } })));
    await component.sendToCashier();
    expect(component.errorMessage()).toBe('ما بتقدر تجهّز طلب لفرع غير فرعك.');
    expect(component.lines().length).toBe(1);
  });

  describe('سحب بسعر التكلفة', () => {
    it('وضع السحب ما بيبين لمستخدم بلا صلاحية', () => {
      atCostAllowed = false;
      const f = TestBed.createComponent(QuickScanComponent);
      expect(f.componentInstance.canWithdrawAtCost()).toBeFalse();
      expect(component.canWithdrawAtCost()).toBeTrue();
    });

    it('عرض السعر بيعبّي التكلفة لكل سطر، وصنف بلا تكلفة بيمنع التأكيد', async () => {
      component.setMode('at-cost');
      component.addItem(MILK);
      component.addItem(RICE);
      apiClientSpy.post.and.returnValue(of({
        lines: [
          { productId: 'p1', productUnitId: 'u1', productBatchId: null, unitCost: 0.8, lineTotal: 0.8 },
          { productId: 'p2', productUnitId: 'u2', productBatchId: null, unitCost: null, lineTotal: null }
        ],
        total: 0.8, sellingValue: 4.75, missingCost: ['رز']
      }));

      await component.refreshQuote();

      expect(component.lines()[0].unitCost).toBe(0.8);
      expect(component.lines()[1].unitCost).toBeNull();
      expect(component.canConfirmAtCost()).toBeFalse();
    });

    it('"اخصمها مني" بتبعت بلا طريقة دفع وبالدفعة اللي رجعت من عرض السعر', async () => {
      spyOn(window, 'confirm').and.returnValue(true);
      component.setMode('at-cost');
      component.addItem(MILK);
      component.setQuantity(component.lines()[0], 2);
      apiClientSpy.post.and.returnValue(of({
        lines: [{ productId: 'p1', productUnitId: 'u1', productBatchId: 'batch1', unitCost: 0.8, lineTotal: 1.6 }],
        total: 1.6, sellingValue: 2.5, missingCost: []
      }));
      await component.refreshQuote();
      expect(component.canConfirmAtCost()).toBeTrue();

      component.settlement = 'deduct';
      apiClientSpy.post.and.returnValue(of({ saleInvoiceId: 's1', invoiceNumber: 'SI-9', totalAmount: 1.6, deductFromShare: true }));
      await component.confirmAtCost();

      const [, operation, body] = apiClientSpy.post.calls.mostRecent().args as [string, string, Record<string, unknown>];
      expect(operation).toBe('at-cost');
      expect(body['deductFromShare']).toBeTrue();
      expect(body['paymentMethodId']).toBeNull();
      expect(body['items']).toEqual([{ productId: 'p1', productUnitId: 'u1', quantity: 2, productBatchId: 'batch1' }]);
      expect(component.successMessage()).toContain('SI-9');
      expect(component.lines().length).toBe(0);
    });
  });
});
