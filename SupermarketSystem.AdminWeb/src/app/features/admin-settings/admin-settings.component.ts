import { Component, OnInit, signal } from '@angular/core';
import { CommonModule } from '@angular/common';
import { FormsModule } from '@angular/forms';
import { firstValueFrom } from 'rxjs';
import { ApiClient } from '../../core/api/api-client.service';
import { ApiController } from '../../core/api/api-controller.enum';
import { SystemOperation } from '../../core/api/operations';

enum AdminSettingDataType {
  Boolean = 1,
  Decimal = 2
}

interface AdminSettingDto {
  key: string;
  label: string;
  value: string;
  dataType: AdminSettingDataType;
}

interface GetAdminSettingsResponse {
  settings: AdminSettingDto[];
}

/** نسخة محلية للتعديل قبل الحفظ — منفصلة عن نسخة السيرفر لعرض "لم يُحفظ بعد" بدقة. */
interface EditableSetting extends AdminSettingDto {
  draftValue: string;
  saving: boolean;
}

/**
 * صفحة إعدادات حسّاسة — تشغيل/إيقاف عمليات زي إلغاء بيع، إرجاع، خصم
 * يدوي، إلخ. القائمة *whitelist صريحة* من الباك إند (GetAdminSettingsHandler.ManagedSettings) -
 * لا يوجد أي إعداد هون غير موجود بهاي القائمة، ولا يوجد أي طريقة لإضافة
 * مفتاح جديد من هالواجهة (الحماية الحقيقية من جهة الباك إند، لا هون).
 */
@Component({
  selector: 'app-admin-settings',
  standalone: true,
  imports: [CommonModule, FormsModule],
  templateUrl: './admin-settings.component.html',
  styleUrl: './admin-settings.component.css'
})
export class AdminSettingsComponent implements OnInit {
  readonly AdminSettingDataType = AdminSettingDataType;

  readonly settings = signal<EditableSetting[]>([]);
  readonly loading = signal(true);
  readonly errorMessage = signal<string | null>(null);
  readonly actionMessage = signal<string | null>(null);

  constructor(private readonly apiClient: ApiClient) {}

  ngOnInit(): void {
    this.loadSettings();
  }

  private async loadSettings(): Promise<void> {
    this.loading.set(true);
    this.errorMessage.set(null);

    try {
      const result = await firstValueFrom(
        this.apiClient.get<GetAdminSettingsResponse>(ApiController.System, SystemOperation.GetAdminSettings)
      );
      this.settings.set(
        result.settings.map(s => ({ ...s, draftValue: s.value, saving: false }))
      );
    } catch {
      this.errorMessage.set('تعذّر تحميل الإعدادات.');
    } finally {
      this.loading.set(false);
    }
  }

  isDirty(setting: EditableSetting): boolean {
    return setting.draftValue !== setting.value;
  }

  toggleBoolean(setting: EditableSetting): void {
    setting.draftValue = setting.draftValue === 'True' ? 'False' : 'True';
    this.save(setting);
  }

  async save(setting: EditableSetting): Promise<void> {
    this.actionMessage.set(null);
    this.errorMessage.set(null);
    setting.saving = true;

    try {
      const result = await firstValueFrom(
        this.apiClient.put<AdminSettingDto>(ApiController.System, SystemOperation.UpdateAdminSetting, {
          key: setting.key,
          value: setting.draftValue
        })
      );

      this.settings.update(list =>
        list.map(s => (s.key === setting.key ? { ...s, value: result.value, draftValue: result.value, saving: false } : s))
      );
      this.actionMessage.set(`تم تحديث "${setting.label}".`);
    } catch (err: unknown) {
      const message =
        err && typeof err === 'object' && 'error' in err
          ? (err as { error?: { detail?: string } }).error?.detail
          : null;
      this.errorMessage.set(message ?? `تعذّر تحديث "${setting.label}".`);
      setting.saving = false;
    }
  }
}
