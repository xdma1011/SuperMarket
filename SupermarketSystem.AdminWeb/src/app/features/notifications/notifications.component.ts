import { Component, OnInit, signal } from '@angular/core';
import { CommonModule } from '@angular/common';
import { firstValueFrom } from 'rxjs';
import { ApiClient } from '../../core/api/api-client.service';
import { ApiController } from '../../core/api/api-controller.enum';
import { NotificationsOperation } from '../../core/api/operations';

interface NotificationItemDto {
  id: string;
  title: string;
  message: string;
  channel: number;
  status: number;
  createdAtUtc: string;
  readAtUtc: string | null;
}

interface PagedResult<T> {
  items: T[];
  totalCount: number;
}

/** لا polling تلقائي — تحديث يدوي عبر زر، تفاديًا لطلبات دورية بلا داعٍ بهذه المرحلة. */
@Component({
  selector: 'app-notifications',
  standalone: true,
  imports: [CommonModule],
  templateUrl: './notifications.component.html',
  styleUrl: './notifications.component.css'
})
export class NotificationsComponent implements OnInit {
  readonly notifications = signal<NotificationItemDto[]>([]);
  readonly loading = signal(true);
  readonly errorMessage = signal<string | null>(null);

  constructor(private readonly apiClient: ApiClient) {}

  ngOnInit(): void {
    this.load();
  }

  async load(): Promise<void> {
    this.loading.set(true);
    this.errorMessage.set(null);

    try {
      const result = await firstValueFrom(
        this.apiClient.get<PagedResult<NotificationItemDto>>(
          ApiController.Notifications, NotificationsOperation.List
        )
      );
      this.notifications.set(result.items);
    } catch {
      this.errorMessage.set('تعذّر تحميل الإشعارات.');
    } finally {
      this.loading.set(false);
    }
  }
}
