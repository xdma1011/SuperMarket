import { Component, OnInit, computed, inject, signal } from '@angular/core';
import { CommonModule } from '@angular/common';
import { FormsModule } from '@angular/forms';
import { firstValueFrom } from 'rxjs';
import { ApiClient } from '../../core/api/api-client.service';
import { ApiController } from '../../core/api/api-controller.enum';
import { SystemOperation } from '../../core/api/operations';
import { BusinessTimeService, TimeSettingsDto } from '../../core/services/business-time.service';

/** مناطق شائعة لو المتصفح ما بيدعم Intl.supportedValuesOf. */
const FALLBACK_ZONES = [
  'Asia/Amman', 'Asia/Riyadh', 'Asia/Dubai', 'Asia/Baghdad', 'Asia/Damascus', 'Asia/Beirut', 'Asia/Jerusalem',
  'Asia/Kuwait', 'Asia/Qatar', 'Asia/Bahrain', 'Asia/Muscat', 'Africa/Cairo', 'Europe/Istanbul', 'Europe/London', 'UTC'
];

/**
 * توقيت المحل (29/9/2026): المنطقة (تلقائي مع الصيفي/الشتوي) أو فرق يدوي ثابت بيغلبها. الاكتشاف من المتصفح نفسه
 * (Intl) - بلا API خارجي ولا موقع جغرافي: الجهاز أصلًا بيعرف منطقته.
 */
@Component({
  selector: 'app-time-settings-panel',
  standalone: true,
  imports: [CommonModule, FormsModule],
  templateUrl: './time-settings-panel.component.html',
  styleUrl: './time-settings-panel.component.css'
})
export class TimeSettingsPanelComponent implements OnInit {
  private readonly api = inject(ApiClient);
  readonly businessTime = inject(BusinessTimeService);

  readonly zones: string[] = TimeSettingsPanelComponent.availableZones();
  /** -12:00 .. +14:00 كل نص ساعة. */
  readonly offsetOptions = Array.from({ length: 53 }, (_, i) => -720 + i * 30);
  readonly deviceZone = TimeSettingsPanelComponent.deviceZone();

  zoneId = 'Asia/Amman';
  mode: 'zone' | 'fixed' = 'zone';
  fixedOffset = 180;

  readonly saving = signal(false);
  readonly message = signal<string | null>(null);
  readonly error = signal<string | null>(null);
  readonly now = signal(new Date());
  readonly current = computed(() => this.businessTime.settings());

  static availableZones(): string[] {
    try {
      const values = (Intl as unknown as { supportedValuesOf?: (k: string) => string[] }).supportedValuesOf?.('timeZone');
      if (values && values.length > 0) {
        return values.includes('UTC') ? values : [...values, 'UTC'];
      }
    } catch {
      /* متصفح قديم */
    }
    return FALLBACK_ZONES;
  }

  static deviceZone(): string | null {
    try {
      return Intl.DateTimeFormat().resolvedOptions().timeZone || null;
    } catch {
      return null;
    }
  }

  ngOnInit(): void {
    const s = this.businessTime.settings();
    if (s) {
      this.fromSettings(s);
    }
    void this.reload();
  }

  private fromSettings(s: TimeSettingsDto): void {
    this.zoneId = s.timeZoneId;
    this.mode = s.fixedUtcOffsetMinutes === null ? 'zone' : 'fixed';
    this.fixedOffset = s.fixedUtcOffsetMinutes ?? s.currentOffsetMinutes;
  }

  async reload(): Promise<void> {
    try {
      const s = await firstValueFrom(this.api.get<TimeSettingsDto>(ApiController.System, SystemOperation.TimeSettings));
      this.businessTime.apply(s);
      this.fromSettings(s);
      this.now.set(new Date());
    } catch {
      this.error.set('تعذّر تحميل إعدادات التوقيت.');
    }
  }

  useDeviceZone(): void {
    if (this.deviceZone) {
      this.zoneId = this.deviceZone;
      this.mode = 'zone';
    }
  }

  offsetLabel(minutes: number): string {
    return BusinessTimeService.offsetLabel(minutes);
  }

  /** الوقت هلق بالمحل حسب الإعدادات المحفوظة. */
  storeTime(): string {
    return this.businessTime.formatDateTime(this.now());
  }

  async save(): Promise<void> {
    this.saving.set(true);
    this.message.set(null);
    this.error.set(null);
    try {
      const result = await firstValueFrom(this.api.put<TimeSettingsDto>(ApiController.System, SystemOperation.UpdateTimeSettings, {
        timeZoneId: this.zoneId,
        fixedUtcOffsetMinutes: this.mode === 'fixed' ? Number(this.fixedOffset) : null
      }));
      this.businessTime.apply(result);
      this.fromSettings(result);
      this.now.set(new Date());
      this.message.set(`انحفظ - توقيت المحل هلق ${BusinessTimeService.offsetLabel(result.currentOffsetMinutes)}.`);
    } catch (err: unknown) {
      const detail = err && typeof err === 'object' && 'error' in err ? (err as { error?: { detail?: string } }).error?.detail : null;
      this.error.set(detail ?? 'تعذّر حفظ إعدادات التوقيت.');
    } finally {
      this.saving.set(false);
    }
  }
}
