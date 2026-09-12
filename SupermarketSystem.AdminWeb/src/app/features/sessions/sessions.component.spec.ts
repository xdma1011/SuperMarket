import { ComponentFixture, TestBed } from '@angular/core/testing';
import { of, throwError } from 'rxjs';
import { SessionsComponent } from './sessions.component';
import { ApiClient } from '../../core/api/api-client.service';

describe('SessionsComponent', () => {
  let fixture: ComponentFixture<SessionsComponent>;
  let component: SessionsComponent;
  let apiClientSpy: jasmine.SpyObj<ApiClient>;

  const sampleSession = {
    sessionId: 's1',
    userId: 'u1',
    username: 'ahmad',
    appType: 'Admin',
    branchId: 'b1',
    ipAddress: '1.2.3.4',
    deviceInfo: 'Chrome',
    createdAtUtc: '',
    lastRefreshedAtUtc: null,
    expiresAtUtc: ''
  };

  beforeEach(async () => {
    apiClientSpy = jasmine.createSpyObj('ApiClient', ['get', 'post']);
    apiClientSpy.get.and.returnValue(of({ items: [], totalCount: 0 }));

    await TestBed.configureTestingModule({
      imports: [SessionsComponent],
      providers: [{ provide: ApiClient, useValue: apiClientSpy }]
    }).compileComponents();

    fixture = TestBed.createComponent(SessionsComponent);
    component = fixture.componentInstance;
  });

  it('يُنشأ المكوّن بنجاح', () => {
    expect(component).toBeTruthy();
  });

  it('يحمّل الجلسات النشطة تلقائيًا عند ngOnInit', async () => {
    apiClientSpy.get.and.returnValue(of({ items: [sampleSession], totalCount: 1 }));

    fixture.detectChanges();
    await fixture.whenStable();

    expect(component.sessions().length).toBe(1);
  });

  describe('load', () => {
    it('يعرض رسالة خطأ عربية واضحة عند فشل التحميل', async () => {
      apiClientSpy.get.and.returnValue(throwError(() => new Error('network')));

      await component.load();

      expect(component.errorMessage()).toBe('تعذّر تحميل الجلسات النشطة.');
    });
  });

  describe('revoke', () => {
    it('يستدعي endpoint الإيقاف بمعرّف الجلسة الصحيح، ثم يعيد التحميل', async () => {
      apiClientSpy.post.and.returnValue(of({}));
      apiClientSpy.get.and.returnValue(of({ items: [], totalCount: 0 }));

      await component.revoke(sampleSession);

      expect(apiClientSpy.post).toHaveBeenCalledWith(jasmine.anything(), jasmine.anything(), {}, { id: 's1' });
      expect(component.actionMessage()).toBe('تم إيقاف جلسة ahmad فورًا.');
    });

    it('يعرض رسالة خطأ عربية واضحة لو فشل الإيقاف', async () => {
      apiClientSpy.post.and.returnValue(throwError(() => new Error('network')));

      await component.revoke(sampleSession);

      expect(component.errorMessage()).toBe('تعذّر إيقاف الجلسة.');
      expect(component.actionMessage()).toBeNull();
    });
  });
});
