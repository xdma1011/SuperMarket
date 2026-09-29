import { Injectable, inject, signal } from '@angular/core';
import { firstValueFrom } from 'rxjs';
import { ApiClient } from '../api/api-client.service';
import { ApiController } from '../api/api-controller.enum';
import { SystemOperation } from '../api/operations';

export interface TimeSettingsDto {
  timeZoneId: string;
  fixedUtcOffsetMinutes: number | null;
  currentOffsetMinutes: number;
  isTimeZoneKnown: boolean;
  serverUtcNow: string;
  localNow: string;
}

/** الافتراضي لو السيرفر مش متاح بلحظة الإقلاع - الأردن +3. */
const DEFAULT_OFFSET_MINUTES = 180;

/**
 * توقيت المحل (29/9/2026) - مش توقيت جهاز اللي فاتح الصفحة. كل الأوقات بالسيرفر UTC، وهون التحويل:
 *   - عرض الأوقات: DatePipe بياخد `timezone()` افتراضيًا (DATE_PIPE_DEFAULT_OPTIONS بـmain.ts) - فأي `| date`
 *     بالصفحات بيعرض بتوقيت المحل بلا تعديل الصفحات.
 *   - فترات التقارير: "من/إلى" أيام محلية كاملة بتوقيت المحل (`dayStartUtc`/`dayEndUtc`).
 * الفرق بينجاب من /system/time-settings (بلا دخول) وقت الإقلاع، وبيتحدّث بعد تعديل الإعدادات.
 */
@Injectable({ providedIn: 'root' })
export class BusinessTimeService {
  private readonly api = inject(ApiClient);

  readonly settings = signal<TimeSettingsDto | null>(null);
  readonly offsetMinutes = signal(DEFAULT_OFFSET_MINUTES);

  async load(): Promise<void> {
    try {
      this.apply(await firstValueFrom(this.api.get<TimeSettingsDto>(ApiController.System, SystemOperation.TimeSettings)));
    } catch {
      // الإقلاع ما بيوقف لو السيرفر مش متاح - بنضل على الافتراضي.
    }
  }

  apply(settings: TimeSettingsDto): void {
    this.settings.set(settings);
    this.offsetMinutes.set(settings.currentOffsetMinutes);
  }

  /** بصيغة DatePipe (مثلًا "+0300"). */
  timezone(): string {
    return BusinessTimeService.formatOffset(this.offsetMinutes());
  }

  static formatOffset(minutes: number): string {
    const sign = minutes < 0 ? '-' : '+';
    const abs = Math.abs(minutes);
    const pad = (n: number) => String(n).padStart(2, '0');
    return `${sign}${pad(Math.floor(abs / 60))}${pad(abs % 60)}`;
  }

  /** "UTC+3" / "UTC+5:30" للعرض. */
  static offsetLabel(minutes: number): string {
    const sign = minutes < 0 ? '−' : '+';
    const abs = Math.abs(minutes);
    const h = Math.floor(abs / 60);
    const m = abs % 60;
    return `UTC${sign}${h}${m ? ':' + String(m).padStart(2, '0') : ''}`;
  }

  /** التاريخ المحلي (yyyy-mm-dd) بتوقيت المحل للحظة معيّنة (افتراضي هلق). */
  localDate(at: Date = new Date()): string {
    const shifted = new Date(at.getTime() + this.offsetMinutes() * 60_000);
    return shifted.toISOString().slice(0, 10);
  }

  /** السنة والشهر بتوقيت المحل. */
  localYearMonth(at: Date = new Date()): { year: number; month: number } {
    const shifted = new Date(at.getTime() + this.offsetMinutes() * 60_000);
    return { year: shifted.getUTCFullYear(), month: shifted.getUTCMonth() + 1 };
  }

  /** "dd/mm/yyyy, hh:mm" بتوقيت المحل (للأماكن اللي ما بتستخدم DatePipe). */
  formatDateTime(value: string | Date): string {
    const t = typeof value === 'string' ? Date.parse(value) : value.getTime();
    return new Date(t + this.offsetMinutes() * 60_000).toLocaleString('en-GB', {
      timeZone: 'UTC', year: 'numeric', month: '2-digit', day: '2-digit', hour: '2-digit', minute: '2-digit'
    });
  }

  /** تاريخ محلي قبل/بعد عدد أيام. */
  addDays(localDate: string, days: number): string {
    const d = new Date(`${localDate}T00:00:00Z`);
    d.setUTCDate(d.getUTCDate() + days);
    return d.toISOString().slice(0, 10);
  }

  /** بداية اليوم المحلي بـUTC (ISO). */
  dayStartUtc(localDate: string): string {
    return new Date(Date.parse(`${localDate}T00:00:00Z`) - this.offsetMinutes() * 60_000).toISOString();
  }

  /** آخر لحظة باليوم المحلي بـUTC (ISO) - الفترة بتشمل يوم "إلى" كامل. */
  dayEndUtc(localDate: string): string {
    return new Date(Date.parse(`${localDate}T00:00:00Z`) + 86_400_000 - 1 - this.offsetMinutes() * 60_000).toISOString();
  }
}
