import { ComponentFixture, TestBed } from '@angular/core/testing';
import { of, throwError } from 'rxjs';
import { NotificationsComponent } from './notifications.component';
import { ApiClient } from '../../core/api/api-client.service';

describe('NotificationsComponent', () => {
  let fixture: ComponentFixture<NotificationsComponent>;
  let component: NotificationsComponent;
  let apiClientSpy: jasmine.SpyObj<ApiClient>;

  beforeEach(async () => {
    apiClientSpy = jasmine.createSpyObj('ApiClient', ['get']);
    apiClientSpy.get.and.returnValue(of({ items: [], totalCount: 0 }));

    await TestBed.configureTestingModule({
      imports: [NotificationsComponent],
      providers: [{ provide: ApiClient, useValue: apiClientSpy }]
    }).compileComponents();

    fixture = TestBed.createComponent(NotificationsComponent);
    component = fixture.componentInstance;
  });

  it('يُنشأ المكوّن بنجاح', () => {
    expect(component).toBeTruthy();
  });

  it('يحمّل الإشعارات تلقائيًا عند ngOnInit', async () => {
    apiClientSpy.get.and.returnValue(
      of({ items: [{ id: '1', title: 'تنبيه', message: 'نص', channel: 'InApp', status: 'Pending' as const, createdAtUtc: '', readAtUtc: null, severity: 'Critical' as const }], totalCount: 1 })
    );

    fixture.detectChanges();
    await fixture.whenStable();

    expect(component.notifications().length).toBe(1);
    expect(component.loading()).toBeFalse();
  });

  describe('load', () => {
    it('يعلّم loading أثناء التحميل ويطفئه بعده', async () => {
      const loadPromise = component.load();
      expect(component.loading()).toBeTrue();

      await loadPromise;

      expect(component.loading()).toBeFalse();
    });

    it('يعرض رسالة خطأ عربية واضحة عند فشل التحميل', async () => {
      apiClientSpy.get.and.returnValue(throwError(() => new Error('network')));

      await component.load();

      expect(component.errorMessage()).toBe('تعذّر تحميل الإشعارات.');
      expect(component.notifications()).toEqual([]);
    });

    it('يمسح رسالة الخطأ القديمة عند إعادة تحميل ناجحة', async () => {
      component.errorMessage.set('خطأ سابق');
      apiClientSpy.get.and.returnValue(of({ items: [], totalCount: 0 }));

      await component.load();

      expect(component.errorMessage()).toBeNull();
    });
  });

  it('فلتر "الخطيرة بس" بيبعت minSeverity=Critical ويعيد التحميل', async () => {
    apiClientSpy.get.and.returnValue(of({ items: [], totalCount: 0 }));

    component.setSeverityFilter('Critical');
    await fixture.whenStable();

    const lastQuery = apiClientSpy.get.calls.mostRecent().args[3] as Record<string, unknown>;
    expect(lastQuery['minSeverity']).toBe('Critical');
    expect(component.minSeverity()).toBe('Critical');
  });

  it('التنبيه الخطير بيتلوّن وبيبين عليه "خطير"', async () => {
    apiClientSpy.get.and.returnValue(of({
      items: [{ id: '1', title: 'تقفيل صندوق — عجز 5.000', message: 'سطر 1\nسطر 2', channel: 'InApp', status: 'Pending', createdAtUtc: '', readAtUtc: null, severity: 'Critical' }],
      totalCount: 1
    }));

    await component.load();
    fixture.detectChanges();

    const card = (fixture.nativeElement as HTMLElement).querySelector('.notif-card');
    expect(card?.classList.contains('critical')).toBeTrue();
    expect(card?.textContent).toContain('خطير');
  });
});
