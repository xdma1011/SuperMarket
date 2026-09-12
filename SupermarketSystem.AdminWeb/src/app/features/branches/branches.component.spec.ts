import { ComponentFixture, TestBed } from '@angular/core/testing';
import { of, throwError } from 'rxjs';
import { BranchesComponent } from './branches.component';
import { ApiClient } from '../../core/api/api-client.service';

describe('BranchesComponent', () => {
  let fixture: ComponentFixture<BranchesComponent>;
  let component: BranchesComponent;
  let apiClientSpy: jasmine.SpyObj<ApiClient>;

  const sampleBranch = { id: 'b1', name: 'الرئيسي', code: 'MAIN', isActive: true, phoneNumber: '079' };

  beforeEach(async () => {
    apiClientSpy = jasmine.createSpyObj('ApiClient', ['get', 'post', 'put']);
    apiClientSpy.get.and.returnValue(of({ items: [], totalCount: 0 }));

    await TestBed.configureTestingModule({
      imports: [BranchesComponent],
      providers: [{ provide: ApiClient, useValue: apiClientSpy }]
    }).compileComponents();

    fixture = TestBed.createComponent(BranchesComponent);
    component = fixture.componentInstance;
  });

  it('يُنشأ المكوّن بنجاح', () => {
    expect(component).toBeTruthy();
  });

  it('يحمّل الفروع عند ngOnInit', async () => {
    apiClientSpy.get.and.returnValue(of({ items: [sampleBranch], totalCount: 1 }));

    fixture.detectChanges();
    await fixture.whenStable();

    expect(component.branches().length).toBe(1);
  });

  it('يعرض رسالة خطأ عربية واضحة عند فشل التحميل', async () => {
    apiClientSpy.get.and.returnValue(throwError(() => new Error('network')));

    fixture.detectChanges();
    await fixture.whenStable();

    expect(component.errorMessage()).toBe('تعذّر تحميل قائمة الفروع.');
  });

  describe('openCreateForm / openEditForm / closeForm', () => {
    it('openCreateForm يصفّر الحقول ويفتح بوضع إنشاء', () => {
      component.name = 'قديم';
      component.openCreateForm();

      expect(component.isEditMode()).toBeFalse();
      expect(component.name).toBe('');
      expect(component.formOpen()).toBeTrue();
    });

    it('openEditForm يعبّي الحقول من الفرع ويفعّل وضع التعديل', () => {
      component.openEditForm(sampleBranch);

      expect(component.isEditMode()).toBeTrue();
      expect(component.editingBranchId).toBe('b1');
      expect(component.code).toBe('MAIN');
    });

    it('closeForm يصفّر كل الحقول', () => {
      component.openEditForm(sampleBranch);
      component.closeForm();

      expect(component.name).toBe('');
      expect(component.code).toBe('');
      expect(component.editingBranchId).toBe('');
    });
  });

  describe('submit', () => {
    it('يرفض الإرسال بلا اسم', async () => {
      component.name = '';

      await component.submit();

      expect(component.formError()).toBe('اسم الفرع مطلوب.');
      expect(apiClientSpy.post).not.toHaveBeenCalled();
    });

    it('يرفض الإنشاء بلا كود (مطلوب فقط بوضع الإنشاء)', async () => {
      component.name = 'فرع جديد';
      component.code = '';

      await component.submit();

      expect(component.formError()).toBe('كود الفرع مطلوب عند الإنشاء.');
      expect(apiClientSpy.post).not.toHaveBeenCalled();
    });

    it('لا يتطلب كود عند التعديل', async () => {
      component.openEditForm(sampleBranch);
      component.code = '';
      apiClientSpy.put.and.returnValue(of({}));

      await component.submit();

      expect(component.formError()).toBeNull();
      expect(apiClientSpy.put).toHaveBeenCalled();
    });

    it('ينشئ فرعًا جديدًا بوضع الإنشاء (post)', async () => {
      component.name = 'فرع جديد';
      component.code = 'NEW';
      apiClientSpy.post.and.returnValue(of({}));

      await component.submit();

      expect(apiClientSpy.post).toHaveBeenCalled();
      expect(component.formOpen()).toBeFalse();
    });

    it('يعدّل فرعًا موجودًا بمعرّفه الصحيح (put)', async () => {
      component.openEditForm(sampleBranch);
      component.name = 'اسم معدَّل';
      apiClientSpy.put.and.returnValue(of({}));

      await component.submit();

      expect(apiClientSpy.put).toHaveBeenCalledWith(
        jasmine.anything(),
        jasmine.anything(),
        jasmine.objectContaining({ name: 'اسم معدَّل' }),
        { branchId: 'b1' }
      );
    });

    it('يعرض رسالة الخطأ التفصيلية من الباك إند لو موجودة', async () => {
      component.name = 'فرع';
      component.code = 'C1';
      apiClientSpy.post.and.returnValue(throwError(() => ({ error: { detail: 'الكود مستخدم مسبقًا.' } })));

      await component.submit();

      expect(component.formError()).toBe('الكود مستخدم مسبقًا.');
    });

    it('يعرض رسالة عربية عامة مختلفة بين الإنشاء والتعديل عند غياب تفاصيل الخطأ', async () => {
      component.name = 'فرع';
      component.code = 'C1';
      apiClientSpy.post.and.returnValue(throwError(() => new Error('network')));

      await component.submit();

      expect(component.formError()).toBe('تعذّر إنشاء الفرع.');
    });
  });

  describe('toggleActive', () => {
    it('يرسل عكس الحالة الحالية ويعيد التحميل عند النجاح', async () => {
      apiClientSpy.post.and.returnValue(of({}));

      await component.toggleActive(sampleBranch);

      expect(apiClientSpy.post).toHaveBeenCalledWith(
        jasmine.anything(),
        jasmine.anything(),
        { isActive: false },
        { branchId: 'b1' }
      );
      expect(component.togglingId()).toBeNull();
    });

    it('يعرض رسالة خطأ عربية واضحة عند الفشل', async () => {
      apiClientSpy.post.and.returnValue(throwError(() => new Error('network')));

      await component.toggleActive(sampleBranch);

      expect(component.errorMessage()).toBe('تعذّر تغيير حالة الفرع.');
    });
  });
});
