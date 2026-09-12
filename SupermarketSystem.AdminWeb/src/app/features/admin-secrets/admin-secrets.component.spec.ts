import { ComponentFixture, TestBed } from '@angular/core/testing';
import { of, throwError } from 'rxjs';
import { AdminSecretsComponent } from './admin-secrets.component';
import { ApiClient } from '../../core/api/api-client.service';

describe('AdminSecretsComponent', () => {
  let fixture: ComponentFixture<AdminSecretsComponent>;
  let component: AdminSecretsComponent;
  let apiClientSpy: jasmine.SpyObj<ApiClient>;

  beforeEach(async () => {
    apiClientSpy = jasmine.createSpyObj('ApiClient', ['get', 'put']);
    apiClientSpy.get.and.returnValue(of({ secrets: [] }));

    await TestBed.configureTestingModule({
      imports: [AdminSecretsComponent],
      providers: [{ provide: ApiClient, useValue: apiClientSpy }]
    }).compileComponents();

    fixture = TestBed.createComponent(AdminSecretsComponent);
    component = fixture.componentInstance;
  });

  it('يُنشأ المكوّن بنجاح', () => {
    expect(component).toBeTruthy();
  });

  it('يحمّل قائمة المفاتيح ويضيف حقول التعديل المؤقتة (draftValue/editing/saving)', async () => {
    apiClientSpy.get.and.returnValue(of({ secrets: [{ key: 'GEMINI_KEY', label: 'مفتاح Gemini', isSet: true }] }));

    fixture.detectChanges();
    await fixture.whenStable();

    expect(component.secrets().length).toBe(1);
    expect(component.secrets()[0].draftValue).toBe('');
    expect(component.secrets()[0].editing).toBeFalse();
    expect(component.secrets()[0].saving).toBeFalse();
  });

  it('يعرض رسالة خطأ عربية واضحة عند فشل تحميل المفاتيح', async () => {
    apiClientSpy.get.and.returnValue(throwError(() => new Error('network')));

    fixture.detectChanges();
    await fixture.whenStable();

    expect(component.errorMessage()).toBe('تعذّر تحميل قائمة المفاتيح.');
  });

  describe('startEdit / cancelEdit', () => {
    it('startEdit يفعّل وضع التعديل ويصفّر draftValue', () => {
      const secret = { key: 'K', label: 'مفتاح', isSet: true, draftValue: 'قديم', editing: false, saving: false };

      component.startEdit(secret);

      expect(secret.editing).toBeTrue();
      expect(secret.draftValue).toBe('');
    });

    it('cancelEdit يلغي وضع التعديل ويصفّر draftValue', () => {
      const secret = { key: 'K', label: 'مفتاح', isSet: true, draftValue: 'مسودة', editing: true, saving: false };

      component.cancelEdit(secret);

      expect(secret.editing).toBeFalse();
      expect(secret.draftValue).toBe('');
    });
  });

  describe('save', () => {
    it('يرفض الحفظ لو draftValue فاضية (بعد trim) بلا استدعاء API', async () => {
      const secret = { key: 'K', label: 'مفتاح', isSet: false, draftValue: '   ', editing: true, saving: false };

      await component.save(secret);

      expect(component.errorMessage()).toBe('أدخل قيمة قبل الحفظ.');
      expect(apiClientSpy.put).not.toHaveBeenCalled();
    });

    it('يحفظ القيمة الجديدة وينهي وضع التعديل عند النجاح', async () => {
      apiClientSpy.get.and.returnValue(of({ secrets: [{ key: 'K', label: 'مفتاح', isSet: false }] }));
      fixture.detectChanges();
      await fixture.whenStable();

      apiClientSpy.put.and.returnValue(of({}));
      const secret = component.secrets()[0];
      secret.draftValue = ' قيمة سرية ';
      secret.editing = true;

      await component.save(secret);

      expect(apiClientSpy.put).toHaveBeenCalledWith(jasmine.anything(), jasmine.anything(), {
        key: 'K',
        value: 'قيمة سرية'
      });
      expect(component.secrets()[0].isSet).toBeTrue();
      expect(component.secrets()[0].editing).toBeFalse();
      expect(component.actionMessage()).toBe('تم تحديث "مفتاح".');
    });

    it('يعرض رسالة خطأ عربية واضحة تحمل اسم المفتاح عند فشل الحفظ', async () => {
      apiClientSpy.put.and.returnValue(throwError(() => new Error('network')));
      const secret = { key: 'K', label: 'مفتاح', isSet: false, draftValue: 'قيمة', editing: true, saving: false };

      await component.save(secret);

      expect(component.errorMessage()).toBe('تعذّر تحديث "مفتاح".');
      expect(secret.saving).toBeFalse();
    });
  });
});
