import { ComponentFixture, TestBed } from '@angular/core/testing';
import { Router } from '@angular/router';
import { of, throwError } from 'rxjs';
import { NotificationItemDto, NotificationsComponent, NotificationSummaryDto } from './notifications.component';
import { ApiClient } from '../../core/api/api-client.service';
import { NotificationsOperation } from '../../core/api/operations';

function item(overrides: Partial<NotificationItemDto> = {}): NotificationItemDto {
  return {
    id: '1', title: 'تنبيه', message: 'نص', channel: 'InApp', status: 'Pending',
    createdAtUtc: '', readAtUtc: null, severity: 'Critical', linkRoute: null, ...overrides
  };
}

describe('NotificationsComponent', () => {
  let fixture: ComponentFixture<NotificationsComponent>;
  let component: NotificationsComponent;
  let apiClientSpy: jasmine.SpyObj<ApiClient>;
  let routerSpy: jasmine.SpyObj<Router>;
  let listItems: NotificationItemDto[];
  let summary: NotificationSummaryDto;

  beforeEach(async () => {
    listItems = [];
    summary = { unreadCritical: 2, unreadWarning: 5, unreadInfo: 1 };
    apiClientSpy = jasmine.createSpyObj('ApiClient', ['get', 'post']);
    apiClientSpy.get.and.callFake(((_c: unknown, operation: string) =>
      operation === NotificationsOperation.Summary
        ? of(summary)
        : of({ items: listItems, totalCount: listItems.length })) as never);
    apiClientSpy.post.and.returnValue(of({}));
    routerSpy = jasmine.createSpyObj('Router', ['navigateByUrl']);
    routerSpy.navigateByUrl.and.resolveTo(true);

    await TestBed.configureTestingModule({
      imports: [NotificationsComponent],
      providers: [
        { provide: ApiClient, useValue: apiClientSpy },
        { provide: Router, useValue: routerSpy }
      ]
    }).compileComponents();

    fixture = TestBed.createComponent(NotificationsComponent);
    component = fixture.componentInstance;
  });

  it('يُنشأ المكوّن بنجاح', () => {
    expect(component).toBeTruthy();
  });

  it('بلا تبويب محدد: بيفتح أعلى أولوية فيها غير مقروء', async () => {
    summary = { unreadCritical: 0, unreadWarning: 3, unreadInfo: 1 };

    fixture.detectChanges();
    await fixture.whenStable();

    expect(component.tab()).toBe('Warning');
  });

  it('يحمّل التنبيهات والعدّادات تلقائيًا عند ngOnInit - عالية الأولوية أول لو فيها غير مقروء', async () => {
    listItems = [item()];

    fixture.detectChanges();
    await fixture.whenStable();

    expect(component.notifications().length).toBe(1);
    expect(component.tab()).toBe('Critical');
    expect(component.unreadCount('Critical')).toBe(2);
    expect(component.unreadCount('all')).toBe(8);
    expect(component.loading()).toBeFalse();

    const listCall = apiClientSpy.get.calls.all().find(c => c.args[1] === NotificationsOperation.List)!;
    expect((listCall.args[3] as Record<string, unknown>)['severity']).toBe('Critical');
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

      await component.load();

      expect(component.errorMessage()).toBeNull();
    });
  });

  it('تبويب "الكل" ما بيبعت severity، و"غير المقروءة بس" بيبعت unreadOnly', async () => {
    component.setTab('all');
    await fixture.whenStable();
    let query = apiClientSpy.get.calls.all().filter(c => c.args[1] === NotificationsOperation.List).pop()!.args[3] as Record<string, unknown>;
    expect(query['severity']).toBeUndefined();

    component.toggleUnreadOnly();
    await fixture.whenStable();
    query = apiClientSpy.get.calls.all().filter(c => c.args[1] === NotificationsOperation.List).pop()!.args[3] as Record<string, unknown>;
    expect(query['unreadOnly']).toBeTrue();
  });

  it('الكبس على تنبيه بيعلّمه مقروء وبيودّي على صفحته والفلتر جاهز', async () => {
    listItems = [item({ id: 'n1', linkRoute: '/sales?search=SI-12' })];
    await component.load();

    await component.open(component.notifications()[0]);

    expect(apiClientSpy.post).toHaveBeenCalledWith(
      jasmine.anything(), NotificationsOperation.MarkRead, {}, { id: 'n1' });
    expect(routerSpy.navigateByUrl).toHaveBeenCalledWith('/sales?search=SI-12');
    expect(component.notifications()[0].status).toBe('Read');
    expect(component.unreadCount('Critical')).toBe(1);
  });

  it('"تم" بتعلّم مقروء بلا ما تنقل، وتنبيه مقروء أصلًا ما بيبعت طلب', async () => {
    listItems = [item({ id: 'n1', linkRoute: '/reviews', severity: 'Warning' }), item({ id: 'n2', status: 'Read' })];
    await component.load();

    await component.markRead(component.notifications()[0], new Event('click'));
    await component.markRead(component.notifications()[1]);

    expect(apiClientSpy.post).toHaveBeenCalledTimes(1);
    expect(routerSpy.navigateByUrl).not.toHaveBeenCalled();
    expect(component.unreadCount('Warning')).toBe(4);
  });

  it('تنبيه بلا صفحة: بيتعلّم مقروء بس', async () => {
    listItems = [item({ id: 'n1', linkRoute: null })];
    await component.load();

    await component.open(component.notifications()[0]);

    expect(apiClientSpy.post).toHaveBeenCalledTimes(1);
    expect(routerSpy.navigateByUrl).not.toHaveBeenCalled();
  });

  it('تعليم الكل بتبويب "الكل" بيستدعي read-all ويعيد التحميل', async () => {
    spyOn(window, 'confirm').and.returnValue(true);
    component.setTab('all');
    await fixture.whenStable();

    await component.markAllRead();

    expect(apiClientSpy.post).toHaveBeenCalledWith(jasmine.anything(), NotificationsOperation.MarkAllRead, {});
  });

  it('التنبيه عالي الأولوية بيتلوّن وبيبين عليه "عالية"', async () => {
    listItems = [item({ title: 'تقفيل صندوق — عجز 5.000', message: 'سطر 1\nسطر 2' })];

    await component.load();
    fixture.detectChanges();

    const card = (fixture.nativeElement as HTMLElement).querySelector('.notif-card');
    expect(card?.classList.contains('critical')).toBeTrue();
    expect(card?.textContent).toContain('عالية');
  });
});
