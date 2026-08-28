import { Component, OnInit, signal } from '@angular/core';
import { CommonModule } from '@angular/common';
import { firstValueFrom } from 'rxjs';
import { ApiClient } from '../../core/api/api-client.service';
import { ApiController } from '../../core/api/api-controller.enum';
import { BackupsOperation } from '../../core/api/operations';
import { environment } from '../../../environments/environment';
import { AuthService } from '../../core/services/auth.service';

interface BackupItemDto {
  id: string;
  fileName: string;
  fileSizeBytes: number;
  statusCode: number;
  statusTitle: string;
  errorMessage: string | null;
  createdAtUtc: string;
}

interface BackupStatsDto {
  totalCount: number;
  totalSizeBytes: number;
}

interface PagedResult<T> {
  items: T[];
  totalCount: number;
}

interface GetBackupsResponse {
  items: PagedResult<BackupItemDto>;
  stats: BackupStatsDto;
}

@Component({
  selector: 'app-backup',
  standalone: true,
  imports: [CommonModule],
  templateUrl: './backup.component.html',
  styleUrl: './backup.component.css'
})
export class BackupComponent implements OnInit {
  readonly backups = signal<BackupItemDto[]>([]);
  readonly stats = signal<BackupStatsDto>({ totalCount: 0, totalSizeBytes: 0 });
  readonly loading = signal(true);
  readonly triggering = signal(false);
  readonly downloadingDirectly = signal(false);
  readonly errorMessage = signal<string | null>(null);
  readonly actionMessage = signal<string | null>(null);

  /** true لو فيه نسخة ناجحة (statusCode=1) بتاريخ اليوم فعليًا - يخفي زر "إنشاء نسخة الآن" تفاديًا لنسخة يدوية بلا داعٍ لو التلقائية اليومية أصلًا اشتغلت. */
  get hasFreshBackupToday(): boolean {
    const today = new Date().toDateString();
    return this.backups().some(b => b.statusCode === 1 && new Date(b.createdAtUtc).toDateString() === today);
  }

  constructor(private readonly apiClient: ApiClient, private readonly authService: AuthService) {}

  ngOnInit(): void {
    this.loadBackups();
  }

  private async loadBackups(): Promise<void> {
    this.loading.set(true);
    this.errorMessage.set(null);

    try {
      const result = await firstValueFrom(
        this.apiClient.get<GetBackupsResponse>(ApiController.Backups, BackupsOperation.List)
      );
      this.backups.set(result.items.items);
      this.stats.set(result.stats);
    } catch {
      this.errorMessage.set('تعذّر تحميل قائمة النسخ الاحتياطية.');
    } finally {
      this.loading.set(false);
    }
  }

  async triggerBackup(): Promise<void> {
    this.triggering.set(true);
    this.actionMessage.set(null);
    this.errorMessage.set(null);

    try {
      await firstValueFrom(this.apiClient.post(ApiController.Backups, BackupsOperation.Trigger));
      this.actionMessage.set('تم إنشاء نسخة احتياطية جديدة بنجاح.');
      await this.loadBackups();
    } catch {
      this.errorMessage.set('فشل إنشاء النسخة الاحتياطية — تأكد إعدادات السيرفر (BACKUP DATABASE).');
    } finally {
      this.triggering.set(false);
    }
  }

  /**
   * التنزيل يحتاج التوكن بالـheader (Authorization: Bearer)، وما يقدر
   * وسم <a href> عادي يرفقه. الحل: نجيب الملف كـblob عبر fetch يدوي
   * (مع نفس التوكن)، وبعدين نولّد رابط تنزيل محلي مؤقت للـblob.
   */
  /**
   * ينشئ نسخة جديدة *وينزّلها مباشرة بنفس الطلب* — لا خطوتين منفصلتين.
   * نفس ملاحظة الباك إند بالضبط: الطلب يضل مفتوح لحد ما BACKUP DATABASE
   * يخلص فعليًا (نفس مدة الإنشاء العادي، بلا فرق زمني إضافي) — لقاعدة
   * بيانات كبيرة ممكن ياخد دقائق، تحمّل انتظار مماثل متوقَّع.
   */
  async createAndDownload(): Promise<void> {
    this.downloadingDirectly.set(true);
    this.errorMessage.set(null);
    this.actionMessage.set(null);

    try {
      const token = this.authService.accessToken();
      const url = `${environment.apiBaseUrl}/backups/download`;

      const response = await fetch(url, {
        method: 'POST',
        headers: token ? { Authorization: `Bearer ${token}` } : {}
      });

      if (!response.ok) {
        throw new Error('فشل الإنشاء والتنزيل المباشر');
      }

      const disposition = response.headers.get('Content-Disposition');
      const fileNameMatch = disposition?.match(/filename="?([^"]+)"?/);
      const fileName = fileNameMatch?.[1] ?? `backup_${Date.now()}.bak`;

      const blob = await response.blob();
      const blobUrl = window.URL.createObjectURL(blob);
      const link = document.createElement('a');
      link.href = blobUrl;
      link.download = fileName;
      link.click();
      window.URL.revokeObjectURL(blobUrl);

      this.actionMessage.set('تم إنشاء نسخة احتياطية وتنزيلها مباشرة.');
      await this.loadBackups();
    } catch {
      this.errorMessage.set('فشل الإنشاء والتنزيل المباشر — لقاعدة بيانات كبيرة، العملية قد تأخذ وقتًا أطول.');
    } finally {
      this.downloadingDirectly.set(false);
    }
  }

  async download(backup: BackupItemDto): Promise<void> {
    try {
      const token = this.authService.accessToken();
      const url = `${environment.apiBaseUrl}/backups/${backup.id}/download`;

      const response = await fetch(url, {
        headers: token ? { Authorization: `Bearer ${token}` } : {}
      });

      if (!response.ok) {
        throw new Error('فشل التنزيل');
      }

      const blob = await response.blob();
      const blobUrl = window.URL.createObjectURL(blob);
      const link = document.createElement('a');
      link.href = blobUrl;
      link.download = backup.fileName;
      link.click();
      window.URL.revokeObjectURL(blobUrl);
    } catch {
      this.errorMessage.set('تعذّر تنزيل النسخة الاحتياطية.');
    }
  }

  async deleteBackup(backup: BackupItemDto): Promise<void> {
    this.actionMessage.set(null);
    this.errorMessage.set(null);

    try {
      await firstValueFrom(this.apiClient.delete(ApiController.Backups, BackupsOperation.Delete, { id: backup.id }));
      this.actionMessage.set('تم حذف النسخة الاحتياطية.');
      await this.loadBackups();
    } catch (err: unknown) {
      const message =
        err && typeof err === 'object' && 'error' in err
          ? (err as { error?: { detail?: string } }).error?.detail
          : null;
      this.errorMessage.set(message ?? 'تعذّر حذف النسخة الاحتياطية.');
    }
  }

  formatSize(bytes: number): string {
    if (bytes < 1024) return `${bytes} B`;
    if (bytes < 1024 * 1024) return `${(bytes / 1024).toFixed(1)} KB`;
    return `${(bytes / (1024 * 1024)).toFixed(1)} MB`;
  }
}
