import { ComponentFixture, TestBed } from '@angular/core/testing';
import { of, throwError } from 'rxjs';
import { AdminSettingsComponent } from './admin-settings.component';
import { ApiClient } from '../../core/api/api-client.service';

describe('AdminSettingsComponent', () => {
  let fixture: ComponentFixture<AdminSettingsComponent>;
  let component: AdminSettingsComponent;
  let apiClientSpy: jasmine.SpyObj<ApiClient>;

  beforeEach(async () => {
    apiClientSpy = jasmine.createSpyObj('ApiClient', ['get', 'put']);
    apiClientSpy.get.and.returnValue(of({ settings: [] }));

    await TestBed.configureTestingModule({
      imports: [AdminSettingsComponent],
      providers: [{ provide: ApiClient, useValue: apiClientSpy }]
    }).compileComponents();

    fixture = TestBed.createComponent(AdminSettingsComponent);
    component = fixture.componentInstance;
  });

  it('يُنشأ المكوّن بنجاح', () => {
    expect(component).toBeTruthy();
  });

  it('يحمّل الإعدادات ويضيف draftValue مطابقة للقيمة الأصلية', async () => {
    apiClientSpy.get.and.returnValue(
      of({ settings: [{ key: 'AllowNegativeStock', label: 'سماح برصيد سالب', value: 'False', dataType: 1 }] })
    );

    fixture.detectChanges();
    await fixture.whenStable();

    expect(component.settings()[0].draftValue).toBe('False');
  });

  it('يعرض رسالة خطأ عربية واضحة عند فشل تحميل الإعدادات', async () => {
    apiClientSpy.get.and.returnValue(throwError(() => new Error('network')));

    fixture.detectChanges();
    await fixture.whenStable();

    expect(component.errorMessage()).toBe('تعذّر تحميل الإعدادات.');
  });

  describe('isDirty', () => {
    it('يرجّع true لو draftValue مختلفة عن value المحفوظة', () => {
      const setting = { key: 'K', label: 'إعداد', value: '5', dataType: 2, draftValue: '10', saving: false };
      expect(component.isDirty(setting)).toBeTrue();
    });

    it('يرجّع false لو draftValue مطابقة لـvalue', () => {
      const setting = { key: 'K', label: 'إعداد', value: '5', dataType: 2, draftValue: '5', saving: false };
      expect(component.isDirty(setting)).toBeFalse();
    });
  });

  describe('toggleBoolean', () => {
    it('يبدّل True إلى False ويحفظ فورًا', () => {
      apiClientSpy.put.and.returnValue(of({ key: 'K', label: 'إعداد', value: 'False', dataType: 1 }));
      const setting = { key: 'K', label: 'إعداد', value: 'True', dataType: 1, draftValue: 'True', saving: false };

      component.toggleBoolean(setting);

      expect(setting.draftValue).toBe('False');
      expect(apiClientSpy.put).toHaveBeenCalled();
    });

    it('يبدّل False إلى True', () => {
      apiClientSpy.put.and.returnValue(of({ key: 'K', label: 'إعداد', value: 'True', dataType: 1 }));
      const setting = { key: 'K', label: 'إعداد', value: 'False', dataType: 1, draftValue: 'False', saving: false };

      component.toggleBoolean(setting);

      expect(setting.draftValue).toBe('True');
    });
  });

  describe('save', () => {
    it('يحفظ القيمة الجديدة ويحدّث value وdraftValue من رد السيرفر', async () => {
      apiClientSpy.get.and.returnValue(of({ settings: [{ key: 'K', label: 'إعداد', value: '3', dataType: 2 }] }));
      fixture.detectChanges();
      await fixture.whenStable();

      apiClientSpy.put.and.returnValue(of({ key: 'K', label: 'إعداد', value: '7', dataType: 2 }));
      const setting = component.settings()[0];
      setting.draftValue = '7';

      await component.save(setting);

      expect(component.settings()[0].value).toBe('7');
      expect(component.settings()[0].draftValue).toBe('7');
      expect(component.actionMessage()).toBe('تم تحديث "إعداد".');
    });

    it('يعرض رسالة الخطأ التفصيلية من الباك إند (err.error.detail) لو موجودة', async () => {
      apiClientSpy.put.and.returnValue(throwError(() => ({ error: { detail: 'القيمة يجب أن تكون بين 1 و100.' } })));
      const setting = { key: 'K', label: 'إعداد', value: '3', dataType: 2, draftValue: '999', saving: false };

      await component.save(setting);

      expect(component.errorMessage()).toBe('القيمة يجب أن تكون بين 1 و100.');
      expect(setting.saving).toBeFalse();
    });

    it('يعرض رسالة عربية عامة لو ما في تفاصيل خطأ من الباك إند', async () => {
      apiClientSpy.put.and.returnValue(throwError(() => new Error('network')));
      const setting = { key: 'K', label: 'إعداد', value: '3', dataType: 2, draftValue: '5', saving: false };

      await component.save(setting);

      expect(component.errorMessage()).toBe('تعذّر تحديث "إعداد".');
    });
  });
});
