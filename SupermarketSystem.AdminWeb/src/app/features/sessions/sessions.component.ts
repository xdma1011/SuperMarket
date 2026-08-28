import { Component, OnInit, signal } from '@angular/core';
import { CommonModule } from '@angular/common';
import { firstValueFrom } from 'rxjs';
import { ApiClient } from '../../core/api/api-client.service';
import { ApiController } from '../../core/api/api-controller.enum';
import { AuthSessionsOperation } from '../../core/api/operations';

interface ActiveSessionItemDto {
  sessionId: string;
  userId: string;
  username: string;
  appType: string;
  branchId: string | null;
  ipAddress: string | null;
  deviceInfo: string | null;
  createdAtUtc: string;
  lastRefreshedAtUtc: string | null;
  expiresAtUtc: string;
}

interface PagedResult<T> {
  items: T[];
  totalCount: number;
}

/**
 * أول استخدام فعلي لـIP والجهاز المسجَّلين بكل جلسة منذ D11 — الغرض
 * الأساسي من هذه الشاشة: ملاحظة جلسة بـIP أو جهاز غريب وإيقافها فورًا.
 */
@Component({
  selector: 'app-sessions',
  standalone: true,
  imports: [CommonModule],
  templateUrl: './sessions.component.html',
  styleUrl: './sessions.component.css'
})
export class SessionsComponent implements OnInit {
  readonly sessions = signal<ActiveSessionItemDto[]>([]);
  readonly loading = signal(true);
  readonly errorMessage = signal<string | null>(null);
  readonly actionMessage = signal<string | null>(null);

  constructor(private readonly apiClient: ApiClient) {}

  ngOnInit(): void {
    this.load();
  }

  async load(): Promise<void> {
    this.loading.set(true);
    this.errorMessage.set(null);

    try {
      const result = await firstValueFrom(
        this.apiClient.get<PagedResult<ActiveSessionItemDto>>(ApiController.AuthSessions, AuthSessionsOperation.List)
      );
      this.sessions.set(result.items);
    } catch {
      this.errorMessage.set('تعذّر تحميل الجلسات النشطة.');
    } finally {
      this.loading.set(false);
    }
  }

  async revoke(session: ActiveSessionItemDto): Promise<void> {
    this.actionMessage.set(null);
    this.errorMessage.set(null);

    try {
      await firstValueFrom(
        this.apiClient.post(ApiController.AuthSessions, AuthSessionsOperation.Revoke, {}, { id: session.sessionId })
      );
      this.actionMessage.set(`تم إيقاف جلسة ${session.username} فورًا.`);
      await this.load();
    } catch {
      this.errorMessage.set('تعذّر إيقاف الجلسة.');
    }
  }
}
