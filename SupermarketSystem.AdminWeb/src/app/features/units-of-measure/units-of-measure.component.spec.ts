import { ComponentFixture, TestBed } from '@angular/core/testing';
import { of, throwError } from 'rxjs';
import { UnitsOfMeasureComponent } from './units-of-measure.component';
import { ApiClient } from '../../core/api/api-client.service';

describe('UnitsOfMeasureComponent', () => {
  let fixture: ComponentFixture<UnitsOfMeasureComponent>;
  let component: UnitsOfMeasureComponent;
  let apiClientSpy: jasmine.SpyObj<ApiClient>;

  beforeEach(async () => {
    apiClientSpy = jasmine.createSpyObj('ApiClient', ['get', 'post']);
    apiClientSpy.get.and.returnValue(of([]));

    await TestBed.configureTestingModule({
      imports: [UnitsOfMeasureComponent],
      providers: [{ provide: ApiClient, useValue: apiClientSpy }]
    }).compileComponents();

    fixture = TestBed.createComponent(UnitsOfMeasureComponent);
    component = fixture.componentInstance;
  });

  it('يُنشأ المكوّن بنجاح', () => {
    expect(component).toBeTruthy();
  });

  it('يحمّل وحدات القياس عند ngOnInit', async () => {
    apiClientSpy.get.and.returnValue(of([{ id: 'u1', name: 'كيلوغرام', isActive: true }]));

    fixture.detectChanges();
    await fixture.whenStable();

    expect(component.units().length).toBe(1);
  });

  it('يعرض رسالة خطأ عربية واضحة عند فشل التحميل', async () => {
    apiClientSpy.get.and.returnValue(throwError(() => new Error('network')));

    fixture.detectChanges();
    await fixture.whenStable();

    expect(component.errorMessage()).toBe('تعذّر تحميل وحدات القياس.');
  });

  describe('openCreateForm / closeForm', () => {
    it('openCreateForm يفتح النموذج ويصفّر الاسم وخطأ سابق', () => {
      component.formError.set('خطأ سابق');
      component.newUnitName = 'قديم';

      component.openCreateForm();

      expect(component.formOpen()).toBeTrue();
      expect(component.formError()).toBeNull();
      expect(component.newUnitName).toBe('');
    });

    it('closeForm يغلق النموذج', () => {
      component.formOpen.set(true);
      component.closeForm();
      expect(component.formOpen()).toBeFalse();
    });
  });

  describe('submit', () => {
    it('يرفض الإرسال لو الاسم فاضي (بعد trim) بلا استدعاء API', async () => {
      component.newUnitName = '   ';

      await component.submit();

      expect(component.formError()).toBe('اسم وحدة القياس مطلوب.');
      expect(apiClientSpy.post).not.toHaveBeenCalled();
    });

    it('ينشئ وحدة جديدة، يغلق النموذج، ويعيد التحميل عند النجاح', async () => {
      component.newUnitName = ' لتر ';
      apiClientSpy.post.and.returnValue(of({}));

      await component.submit();

      expect(apiClientSpy.post).toHaveBeenCalledWith(jasmine.anything(), jasmine.anything(), { name: 'لتر' });
      expect(component.formOpen()).toBeFalse();
    });

    it('يعرض رسالة الخطأ التفصيلية من الباك إند لو موجودة، بلا إغلاق النموذج', async () => {
      component.formOpen.set(true);
      component.newUnitName = 'لتر';
      apiClientSpy.post.and.returnValue(throwError(() => ({ error: { detail: 'الاسم مستخدم مسبقًا.' } })));

      await component.submit();

      expect(component.formError()).toBe('الاسم مستخدم مسبقًا.');
      expect(component.formOpen()).toBeTrue();
    });

    it('يعرض رسالة عربية عامة لو ما في تفاصيل خطأ', async () => {
      component.newUnitName = 'لتر';
      apiClientSpy.post.and.returnValue(throwError(() => new Error('network')));

      await component.submit();

      expect(component.formError()).toBe('تعذّر إنشاء وحدة القياس.');
    });
  });

  describe('toggleActive', () => {
    it('يرسل عكس الحالة الحالية (isActive: !unit.isActive)', async () => {
      apiClientSpy.post.and.returnValue(of({}));
      const unit = { id: 'u1', name: 'كيلو', isActive: true };

      await component.toggleActive(unit);

      expect(apiClientSpy.post).toHaveBeenCalledWith(
        jasmine.anything(),
        jasmine.anything(),
        { isActive: false },
        { unitOfMeasureId: 'u1' }
      );
      expect(component.togglingId()).toBeNull();
    });

    it('يعرض رسالة خطأ عربية واضحة عند الفشل', async () => {
      apiClientSpy.post.and.returnValue(throwError(() => new Error('network')));

      await component.toggleActive({ id: 'u1', name: 'كيلو', isActive: true });

      expect(component.errorMessage()).toBe('تعذّر تغيير حالة وحدة القياس.');
    });
  });
});
