import { Component, OnInit, computed, inject, signal } from '@angular/core';
import { CommonModule } from '@angular/common';
import { ActivatedRoute, Router } from '@angular/router';
import { firstValueFrom } from 'rxjs';
import { ApiClient } from '../../core/api/api-client.service';
import { ApiController } from '../../core/api/api-controller.enum';
import { NotificationsOperation } from '../../core/api/operations';
import { PermissionsService } from '../../core/services/permissions.service';

export interface NotificationItemDto {
  id: string;
  title: string;
  message: string;
  // أسماء NotificationChannel/NotificationStatus بالـC# (JsonStringEnumConverter عام بالباك إند).
  channel: string;
  status: 'Pending' | 'Sent' | 'Read' | 'Failed';
  createdAtUtc: string;
  readAtUtc: string | null;
  severity: NotificationSeverity;
  /** الصفحة المعنية مع فلترها (مثلًا "/sales?search=SI-12") - null = بلا صفحة. */
  linkRoute?: string | null;
}

export type NotificationSeverity = 'Info' | 'Warning' | 'Critical';

/** تبويبات لوحة التنبيهات: درجة وحدة بالضبط، أو الكل. */
export type AlertsTab = 'Critical' | 'Warning' | 'Info' | 'all';

export interface NotificationSummaryDto {
  unreadCritical: number;
  unreadWarning: number;
  unreadInfo: number;
}

const SEVERITY_LABELS: Record<NotificationSeverity, string> = {
  Critical: 'عالية',
  Warning: 'متوسطة',
  Info: 'منخفضة'
};

interface PagedResult<T> {
  items: T[];
  totalCount: number;
}

/**
 * لوحة التنبيهات الموحّدة (ملاحظة صاحب المشروع 23/9، انبنت 28/9/2026): التنبيهات مقسّمة عالية/متوسطة/منخفضة
 * الأولوية بعدّاد غير المقروء لكل وحدة. الكبس على تنبيه بيعلّمه مقروء وبيودّي للصفحة المعنية والفلتر جاهز
 * (Notification.LinkRoute من السيرفر). "تم" بتعلّم بلا ما تنقل.
 */
@Component({
  selector: 'app-notifications',
  standalone: true,
  imports: [CommonModule],
  templateUrl: './notifications.component.html',
  styleUrl: './notifications.component.css'
})
export class NotificationsComponent implements OnInit {
  private readonly router = inject(Router, { optional: true });
  /** "?tab=Warning" (من الرئيسية) بيفتح التبويب مباشرة. اختياري: الاختبارات بلا Router. */
  private readonly route = inject(ActivatedRoute, { optional: true });

  readonly notifications = signal<NotificationItemDto[]>([]);
  readonly summary = signal<NotificationSummaryDto>({ unreadCritical: 0, unreadWarning: 0, unreadInfo: 0 });
  readonly loading = signal(true);
  readonly errorMessage = signal<string | null>(null);
  readonly tab = signal<AlertsTab>('Critical');
  readonly unreadOnly = signal(false);
  readonly severityLabels = SEVERITY_LABELS;

  private readonly permissions = inject(PermissionsService);
  /** الكاشير بيشوف بس ما بيعلّم (Notifications.Manage). لسه ما تحمّلت الصلاحيات = بنعرض (الحماية الحقيقية بالسيرفر). */
  readonly canMarkRead = computed(() => !this.permissions.loaded() || this.permissions.has('Notifications.Manage'));

  readonly unreadInTab = computed(() => this.notifications().filter(n => n.status !== 'Read').length);

  constructor(private readonly apiClient: ApiClient) {}

  ngOnInit(): void {
    const tab = this.route?.snapshot.queryParamMap.get('tab');
    if (tab === 'Critical' || tab === 'Warning' || tab === 'Info' || tab === 'all') this.tab.set(tab);
    void this.load();
  }

  async load(): Promise<void> {
    this.loading.set(true);
    this.errorMessage.set(null);

    const params: Record<string, string | number | boolean> = { pageSize: 100 };
    if (this.tab() !== 'all') params['severity'] = this.tab();
    if (this.unreadOnly()) params['unreadOnly'] = true;

    try {
      const [list, summary] = await Promise.all([
        firstValueFrom(this.apiClient.get<PagedResult<NotificationItemDto>>(
          ApiController.Notifications, NotificationsOperation.List, undefined, params)),
        firstValueFrom(this.apiClient.get<NotificationSummaryDto>(
          ApiController.Notifications, NotificationsOperation.Summary)).catch(() => this.summary())
      ]);
      this.notifications.set(list.items);
      this.summary.set(summary);
    } catch {
      this.errorMessage.set('تعذّر تحميل الإشعارات.');
    } finally {
      this.loading.set(false);
    }
  }

  setTab(tab: AlertsTab): void {
    this.tab.set(tab);
    void this.load();
  }

  toggleUnreadOnly(): void {
    this.unreadOnly.update(v => !v);
    void this.load();
  }

  unreadCount(tab: AlertsTab): number {
    const s = this.summary();
    switch (tab) {
      case 'Critical': return s.unreadCritical;
      case 'Warning': return s.unreadWarning;
      case 'Info': return s.unreadInfo;
      default: return s.unreadCritical + s.unreadWarning + s.unreadInfo;
    }
  }

  /** الكبس على التنبيه: بيتعلّم مقروء، وإذا إله صفحة بيودّي عليها. */
  async open(n: NotificationItemDto): Promise<void> {
    if (this.canMarkRead()) await this.markRead(n);
    if (n.linkRoute) {
      await this.router?.navigateByUrl(n.linkRoute);
    }
  }

  async markRead(n: NotificationItemDto, event?: Event): Promise<void> {
    event?.stopPropagation();
    if (n.status === 'Read') return;
    try {
      await firstValueFrom(this.apiClient.post(ApiController.Notifications, NotificationsOperation.MarkRead, {}, { id: n.id }));
      this.notifications.update(list => list.map(x => (x.id === n.id ? { ...x, status: 'Read' as const, readAtUtc: new Date().toISOString() } : x)));
      this.summary.update(s => ({
        unreadCritical: s.unreadCritical - (n.severity === 'Critical' ? 1 : 0),
        unreadWarning: s.unreadWarning - (n.severity === 'Warning' ? 1 : 0),
        unreadInfo: s.unreadInfo - (n.severity === 'Info' ? 1 : 0)
      }));
    } catch {
      this.errorMessage.set('تعذّر تعليم التنبيه كمقروء.');
    }
  }

  /** تعليم كل تنبيهات التبويب الحالي مقروءة (لتبويب "الكل" = كل شي). */
  async markAllRead(): Promise<void> {
    const tab = this.tab();
    const label = tab === 'all' ? 'كل التنبيهات' : `كل تنبيهات الأولوية ال${SEVERITY_LABELS[tab]}`;
    if (!confirm(`تعليم ${label} كمقروءة؟`)) return;
    try {
      // السيرفر بيعلّم "لحد درجة" - لتبويب درجة وحدة بنعلّم عناصره وحدة وحدة (بس الظاهرة).
      if (tab === 'all') {
        await firstValueFrom(this.apiClient.post(ApiController.Notifications, NotificationsOperation.MarkAllRead, {}));
      } else {
        const unread = this.notifications().filter(n => n.status !== 'Read');
        await Promise.all(unread.map(n =>
          firstValueFrom(this.apiClient.post(ApiController.Notifications, NotificationsOperation.MarkRead, {}, { id: n.id }))));
      }
      await this.load();
    } catch {
      this.errorMessage.set('تعذّر تعليم التنبيهات كمقروءة.');
    }
  }
}
