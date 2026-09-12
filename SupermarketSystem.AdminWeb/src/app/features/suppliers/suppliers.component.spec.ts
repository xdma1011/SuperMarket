import { ComponentFixture, TestBed } from '@angular/core/testing';
import { of, throwError } from 'rxjs';
import { SuppliersComponent } from './suppliers.component';
import { ApiClient } from '../../core/api/api-client.service';

describe('SuppliersComponent', () => {
  let fixture: ComponentFixture<SuppliersComponent>;
  let component: SuppliersComponent;
  let apiClientSpy: jasmine.SpyObj<ApiClient>;

  const sampleSupplier = {
    id: 'sup1',
    name: 'مورد الألبان',
    contactName: 'خالد',
    phone: '079',
    email: 'k@example.com',
    isActive: true
  };

  beforeEach(async () => {
    apiClientSpy = jasmine.createSpyObj('ApiClient', ['get', 'post', 'put']);
    apiClientSpy.get.and.returnValue(of({ items: [], totalCount: 0 }));

    await TestBed.configureTestingModule({
      imports: [SuppliersComponent],
      providers: [{ provide: ApiClient, useValue: apiClientSpy }]
    }).compileComponents();

    fixture = TestBed.createComponent(SuppliersComponent);
    component = fixture.componentInstance;
  });

  it('يُنشأ المكوّن بنجاح', () => {
    expect(component).toBeTruthy();
  });

  it('يحمّل الموردين عند ngOnInit', async () => {
    apiClientSpy.get.and.returnValue(of({ items: [sampleSupplier], totalCount: 1 }));

    fixture.detectChanges();
    await fixture.whenStable();

    expect(component.suppliers().length).toBe(1);
  });

  it('يعرض رسالة خطأ عربية واضحة عند فشل التحميل', async () => {
    apiClientSpy.get.and.returnValue(throwError(() => new Error('network')));

    fixture.detectChanges();
    await fixture.whenStable();

    expect(component.errorMessage()).toBe('تعذّر تحميل قائمة الموردين.');
  });

  describe('onPageChanged', () => {
    it('يحدّث الصفحة ويعيد التحميل', () => {
      apiClientSpy.get.calls.reset();

      component.onPageChanged({ pageNumber: 2, pageSize: 10 });

      expect(component.pageNumber()).toBe(2);
      expect(component.pageSize()).toBe(10);
      expect(apiClientSpy.get).toHaveBeenCalled();
    });
  });

  describe('openCreateForm / openEditForm / closeForm', () => {
    it('openCreateForm يفتح نموذج فاضٍ بوضع إنشاء', () => {
      component.openCreateForm();

      expect(component.isEditMode()).toBeFalse();
      expect(component.formOpen()).toBeTrue();
    });

    it('openEditForm يعبّي الحقول من المورد ويفعّل وضع التعديل', () => {
      component.openEditForm(sampleSupplier);

      expect(component.isEditMode()).toBeTrue();
      expect(component.editingSupplierId).toBe('sup1');
      expect(component.name).toBe('مورد الألبان');
      expect(component.contactName).toBe('خالد');
      expect(component.formOpen()).toBeTrue();
    });

    it('openEditForm يستخدم نص فاضٍ للحقول الفارغة (null) بدل عرض "null"', () => {
      component.openEditForm({ ...sampleSupplier, contactName: null, phone: null, email: null });

      expect(component.contactName).toBe('');
      expect(component.phone).toBe('');
      expect(component.email).toBe('');
    });

    it('closeForm يصفّر كل الحقول', () => {
      component.openEditForm(sampleSupplier);
      component.closeForm();

      expect(component.formOpen()).toBeFalse();
      expect(component.name).toBe('');
      expect(component.editingSupplierId).toBe('');
    });
  });

  describe('submit', () => {
    it('يرفض الإرسال لو الاسم فاضٍ', async () => {
      component.name = '   ';

      await component.submit();

      expect(component.formError()).toBe('اسم المورد مطلوب.');
      expect(apiClientSpy.post).not.toHaveBeenCalled();
    });

    it('ينشئ مورد جديد بوضع الإنشاء (post)', async () => {
      component.name = 'مورد جديد';
      apiClientSpy.post.and.returnValue(of({}));

      await component.submit();

      expect(apiClientSpy.post).toHaveBeenCalled();
      expect(apiClientSpy.put).not.toHaveBeenCalled();
      expect(component.formOpen()).toBeFalse();
    });

    it('يعدّل موردًا موجودًا بوضع التعديل (put) بمعرّفه الصحيح', async () => {
      component.openEditForm(sampleSupplier);
      component.name = 'اسم معدَّل';
      apiClientSpy.put.and.returnValue(of({}));

      await component.submit();

      expect(apiClientSpy.put).toHaveBeenCalledWith(
        jasmine.anything(),
        jasmine.anything(),
        jasmine.objectContaining({ name: 'اسم معدَّل' }),
        { supplierId: 'sup1' }
      );
      expect(apiClientSpy.post).not.toHaveBeenCalled();
    });

    it('يعرض رسالة خطأ مناسبة لوضع الإنشاء عند الفشل', async () => {
      component.name = 'مورد';
      apiClientSpy.post.and.returnValue(throwError(() => new Error('network')));

      await component.submit();

      expect(component.formError()).toBe('تعذّر إنشاء المورد.');
    });

    it('يعرض رسالة خطأ مناسبة لوضع التعديل عند الفشل', async () => {
      component.openEditForm(sampleSupplier);
      apiClientSpy.put.and.returnValue(throwError(() => new Error('network')));

      await component.submit();

      expect(component.formError()).toBe('تعذّر تعديل المورد.');
    });
  });
});
