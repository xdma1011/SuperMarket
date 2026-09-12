import { ComponentFixture, TestBed } from '@angular/core/testing';
import { Router } from '@angular/router';
import { of, throwError } from 'rxjs';
import { PurchasingDraftsComponent } from './purchasing-drafts.component';
import { ApiClient } from '../../core/api/api-client.service';

describe('PurchasingDraftsComponent', () => {
  let fixture: ComponentFixture<PurchasingDraftsComponent>;
  let component: PurchasingDraftsComponent;
  let apiClientSpy: jasmine.SpyObj<ApiClient>;
  let routerSpy: jasmine.SpyObj<Router>;

  beforeEach(async () => {
    apiClientSpy = jasmine.createSpyObj('ApiClient', ['get', 'post']);
    apiClientSpy.get.and.returnValue(of({ items: [], totalCount: 0 }));
    routerSpy = jasmine.createSpyObj('Router', ['navigate']);
    routerSpy.navigate.and.resolveTo(true);

    await TestBed.configureTestingModule({
      imports: [PurchasingDraftsComponent],
      providers: [{ provide: ApiClient, useValue: apiClientSpy }, { provide: Router, useValue: routerSpy }]
    }).compileComponents();

    fixture = TestBed.createComponent(PurchasingDraftsComponent);
    component = fixture.componentInstance;
  });

  it('يُنشأ المكوّن بنجاح', () => {
    expect(component).toBeTruthy();
  });

  it('يحمّل المسودات والفروع وطرق الدفع، ويختار أول فرع وطريقة دفع', async () => {
    apiClientSpy.get.and.callFake(((controller: string) => {
      if (controller === 'branches') return of({ items: [{ id: 'b1', name: 'الرئيسي' }], totalCount: 1 });
      if (controller === 'payment-methods') return of([{ id: 'pm1', name: 'كاش' }]);
      return of({ items: [], totalCount: 0 });
    }) as unknown as typeof apiClientSpy.get);

    fixture.detectChanges();
    await fixture.whenStable();

    expect(component.selectedBranchId).toBe('b1');
    expect(component.paidNowPaymentMethodId).toBe('pm1');
  });

  it('يعرض رسالة خطأ عربية واضحة عند فشل التحميل', async () => {
    apiClientSpy.get.and.returnValue(throwError(() => new Error('network')));

    fixture.detectChanges();
    await fixture.whenStable();

    expect(component.errorMessage()).toBe('تعذّر تحميل مسودات فواتير الشراء.');
  });

  describe('branchNameOf', () => {
    it('يرجّع اسم الفرع الصحيح من المعرّف', () => {
      component.branches.set([{ id: 'b1', name: 'الرئيسي' }]);
      expect(component.branchNameOf('b1')).toBe('الرئيسي');
    });

    it('يرجّع "—" لمعرّف null', () => {
      expect(component.branchNameOf(null)).toBe('—');
    });

    it('يرجّع "—" لمعرّف غير موجود بقائمة الفروع المحمَّلة', () => {
      component.branches.set([{ id: 'b1', name: 'الرئيسي' }]);
      expect(component.branchNameOf('b999')).toBe('—');
    });
  });

  describe('openUploadModal / closeUploadModal', () => {
    it('openUploadModal يفتح النافذة ويصفّر الملف وحالة الدفع', () => {
      component.selectedFile = new File(['x'], 'a.jpg');
      component.paidNow = true;

      component.openUploadModal();

      expect(component.uploadModalOpen()).toBeTrue();
      expect(component.selectedFile).toBeNull();
      expect(component.paidNow).toBeFalse();
    });

    it('closeUploadModal يغلق النافذة', () => {
      component.uploadModalOpen.set(true);
      component.closeUploadModal();
      expect(component.uploadModalOpen()).toBeFalse();
    });
  });

  describe('onFileSelected', () => {
    it('يحفظ الملف المختار', () => {
      const file = new File(['x'], 'a.jpg');
      const input = document.createElement('input');
      Object.defineProperty(input, 'files', { value: [file] });

      component.onFileSelected({ target: input } as unknown as Event);

      expect(component.selectedFile).toBe(file);
    });
  });

  describe('upload', () => {
    it('يرفض الرفع بلا فرع أو ملف', async () => {
      component.selectedBranchId = '';
      component.selectedFile = null;

      await component.upload();

      expect(component.uploadError()).toBe('اختر الفرع والصورة أولًا.');
      expect(apiClientSpy.post).not.toHaveBeenCalled();
    });

    it('ينتقل لصفحة تفاصيل المسودة الجديدة عند نجاح الرفع', async () => {
      component.selectedBranchId = 'b1';
      component.selectedFile = new File(['x'], 'a.jpg');
      apiClientSpy.post.and.returnValue(of({ draftId: 'd1' }));

      await component.upload();

      expect(routerSpy.navigate).toHaveBeenCalledWith(['/purchases/drafts', 'd1']);
      expect(component.uploadModalOpen()).toBeFalse();
    });

    it('يعرض رسالة الخطأ التفصيلية من الباك إند لو موجودة', async () => {
      component.selectedBranchId = 'b1';
      component.selectedFile = new File(['x'], 'a.jpg');
      apiClientSpy.post.and.returnValue(throwError(() => ({ error: { detail: 'صورة غير واضحة.' } })));

      await component.upload();

      expect(component.uploadError()).toBe('صورة غير واضحة.');
      expect(routerSpy.navigate).not.toHaveBeenCalled();
    });
  });

  describe('openDraft', () => {
    it('ينتقل لصفحة تفاصيل المسودة بالمعرّف الصحيح', () => {
      component.openDraft({
        id: 'd9',
        rawSupplierName: null,
        matchedSupplierId: null,
        supplierInvoiceReference: null,
        providerName: null,
        extractionConfidence: null,
        itemCount: 1,
        unmatchedItemCount: 0,
        status: 1,
        paidNowAmount: null,
        createdAtUtc: ''
      });

      expect(routerSpy.navigate).toHaveBeenCalledWith(['/purchases/drafts', 'd9']);
    });
  });
});
