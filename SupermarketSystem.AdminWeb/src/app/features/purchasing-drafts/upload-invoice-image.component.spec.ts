import { ComponentFixture, TestBed } from '@angular/core/testing';
import { of, throwError } from 'rxjs';
import { UploadInvoiceImageComponent } from './upload-invoice-image.component';
import { ApiClient } from '../../core/api/api-client.service';

describe('UploadInvoiceImageComponent', () => {
  let fixture: ComponentFixture<UploadInvoiceImageComponent>;
  let component: UploadInvoiceImageComponent;
  let apiClientSpy: jasmine.SpyObj<ApiClient>;

  beforeEach(async () => {
    apiClientSpy = jasmine.createSpyObj('ApiClient', ['get', 'post']);
    apiClientSpy.get.and.returnValue(of({ items: [], totalCount: 0 }));

    await TestBed.configureTestingModule({
      imports: [UploadInvoiceImageComponent],
      providers: [{ provide: ApiClient, useValue: apiClientSpy }]
    }).compileComponents();

    fixture = TestBed.createComponent(UploadInvoiceImageComponent);
    component = fixture.componentInstance;
  });

  it('يُنشأ المكوّن بنجاح', () => {
    expect(component).toBeTruthy();
  });

  it('يختار أول فرع تلقائيًا عند تحميل قائمة الفروع', async () => {
    apiClientSpy.get.and.callFake(((controller: string) => {
      if (controller === 'branches') return of({ items: [{ id: 'b1', name: 'الرئيسي' }], totalCount: 1 });
      return of([]);
    }) as unknown as typeof apiClientSpy.get);

    fixture.detectChanges();
    await fixture.whenStable();

    expect(component.selectedBranchId).toBe('b1');
  });

  it('يعرض رسالة خطأ عربية واضحة لو فشل تحميل الفروع', async () => {
    apiClientSpy.get.and.callFake(((controller: string) => {
      if (controller === 'branches') return throwError(() => new Error('down'));
      return of([]);
    }) as unknown as typeof apiClientSpy.get);

    fixture.detectChanges();
    await fixture.whenStable();

    expect(component.errorMessage()).toBe('تعذّر تحميل قائمة الفروع.');
  });

  it('فشل تحميل طرق الدفع لا يوقف الصفحة (اختيارية)', async () => {
    apiClientSpy.get.and.callFake(((controller: string) => {
      if (controller === 'payment-methods') return throwError(() => new Error('down'));
      return of({ items: [], totalCount: 0 });
    }) as unknown as typeof apiClientSpy.get);

    fixture.detectChanges();
    await fixture.whenStable();

    expect(component.errorMessage()).toBeNull();
    expect(component.paymentMethods()).toEqual([]);
  });

  describe('onFileSelected', () => {
    it('يحفظ الملف المختار', () => {
      const file = new File(['x'], 'invoice.jpg', { type: 'image/jpeg' });
      const input = document.createElement('input');
      input.type = 'file';
      Object.defineProperty(input, 'files', { value: [file] });

      component.onFileSelected({ target: input } as unknown as Event);

      expect(component.selectedFile).toBe(file);
    });

    it('يضبط null لو ما في ملف مختار', () => {
      const input = document.createElement('input');
      input.type = 'file';

      component.onFileSelected({ target: input } as unknown as Event);

      expect(component.selectedFile).toBeNull();
    });
  });

  describe('upload', () => {
    it('يرفض الرفع بلا فرع أو صورة مختارة', async () => {
      component.selectedBranchId = '';
      component.selectedFile = null;

      await component.upload();

      expect(component.errorMessage()).toBe('اختر الفرع والصورة أولًا.');
      expect(apiClientSpy.post).not.toHaveBeenCalled();
    });

    it('يرفض الرفع لو "دفعت الآن" مفعّلة بلا مبلغ أو طريقة دفع صالحة', async () => {
      component.selectedBranchId = 'b1';
      component.selectedFile = new File(['x'], 'a.jpg');
      component.paidNow = true;
      component.paidNowAmount = 0;

      await component.upload();

      expect(component.errorMessage()).toBe('حدّد مبلغًا موجبًا وطريقة الدفع لو دفعت للمورد الآن.');
      expect(apiClientSpy.post).not.toHaveBeenCalled();
    });

    it('يرفع الفاتورة بنجاح ويعرض رسالة تأكيد عادية (بلا دفعة)', async () => {
      component.selectedBranchId = 'b1';
      component.selectedFile = new File(['x'], 'a.jpg');
      apiClientSpy.post.and.returnValue(of({}));

      await component.upload();

      expect(apiClientSpy.post).toHaveBeenCalled();
      expect(component.successMessage()).toBe('تم رفع الفاتورة وقراءتها - بانتظار مراجعة الإدارة قبل اعتمادها.');
      expect(component.selectedFile).toBeNull();
    });

    it('يرفع الفاتورة مع دفعة، ويضيف queryParams الدفع، ويعرض رسالة مختلفة', async () => {
      component.selectedBranchId = 'b1';
      component.selectedFile = new File(['x'], 'a.jpg');
      component.paidNow = true;
      component.paidNowAmount = 50;
      component.paidNowPaymentMethodId = 'pm1';
      apiClientSpy.post.and.returnValue(of({}));

      await component.upload();

      const [, , , , queryParams] = apiClientSpy.post.calls.mostRecent().args;
      expect((queryParams as Record<string, string>)['paidNowAmount']).toBe('50');
      expect((queryParams as Record<string, string>)['paidNowPaymentMethodId']).toBe('pm1');
      expect(component.successMessage()).toContain('سُجّل المبلغ المدفوع بدرج الكاش فورًا');
    });

    it('يعرض رسالة الخطأ التفصيلية من الباك إند لو موجودة', async () => {
      component.selectedBranchId = 'b1';
      component.selectedFile = new File(['x'], 'a.jpg');
      apiClientSpy.post.and.returnValue(throwError(() => ({ error: { detail: 'الصورة غير واضحة.' } })));

      await component.upload();

      expect(component.errorMessage()).toBe('الصورة غير واضحة.');
    });

    it('يعرض رسالة عربية عامة لو ما في تفاصيل خطأ', async () => {
      component.selectedBranchId = 'b1';
      component.selectedFile = new File(['x'], 'a.jpg');
      apiClientSpy.post.and.returnValue(throwError(() => new Error('network')));

      await component.upload();

      expect(component.errorMessage()).toBe('تعذّرت قراءة الفاتورة آليًا - جرّب صورة أوضح.');
    });
  });
});
