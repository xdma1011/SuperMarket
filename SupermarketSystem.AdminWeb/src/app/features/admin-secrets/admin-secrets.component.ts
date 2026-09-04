import { Component, OnInit, signal } from '@angular/core';
import { CommonModule } from '@angular/common';
import { FormsModule } from '@angular/forms';
import { firstValueFrom } from 'rxjs';
import { ApiClient } from '../../core/api/api-client.service';
import { ApiController } from '../../core/api/api-controller.enum';
import { SystemOperation } from '../../core/api/operations';

interface SecretSettingDto {
  key: string;
  label: string;
  isSet: boolean;
}

interface GetSecretSettingsResponse {
  secrets: SecretSettingDto[];
}

/** حقل إدخال مؤقت لكل سر - مش من الرد، القيمة الحقيقية ما ترجع أبدًا من السيرفر. */
interface EditableSecret extends SecretSettingDto {
  draftValue: string;
  editing: boolean;
  saving: boolean;
}

/**
 * صفحة المفاتيح المقنَّعة (Gemini/Claude/تلغرام/Firebase) - منفصلة
 * عمدًا عن صفحة "الإعدادات الحسّاسة" العادية (راجع GetAdminSettingsHandler)
 * لأنها أسرار: القيمة الحقيقية ما تنعرض أبدًا، بس "مُعدّ ✓" أو "غير
 * مُعدّ" - وإدخال قيمة جديدة يستبدل القديمة بالكامل، بلا تعديل جزئي.
 */
@Component({
  selector: 'app-admin-secrets',
  standalone: true,
  imports: [CommonModule, FormsModule],
  templateUrl: './admin-secrets.component.html',
  styleUrl: './admin-secrets.component.css'
})
export class AdminSecretsComponent implements OnInit {
  readonly secrets = signal<EditableSecret[]>([]);
  readonly loading = signal(true);
  readonly errorMessage = signal<string | null>(null);
  readonly actionMessage = signal<string | null>(null);

  constructor(private readonly apiClient: ApiClient) {}

  ngOnInit(): void {
    this.loadSecrets();
  }

  private async loadSecrets(): Promise<void> {
    this.loading.set(true);
    this.errorMessage.set(null);

    try {
      const result = await firstValueFrom(
        this.apiClient.get<GetSecretSettingsResponse>(ApiController.System, SystemOperation.GetSecretSettings)
      );
      this.secrets.set(result.secrets.map(s => ({ ...s, draftValue: '', editing: false, saving: false })));
    } catch {
      this.errorMessage.set('تعذّر تحميل قائمة المفاتيح.');
    } finally {
      this.loading.set(false);
    }
  }

  startEdit(secret: EditableSecret): void {
    secret.editing = true;
    secret.draftValue = '';
  }

  cancelEdit(secret: EditableSecret): void {
    secret.editing = false;
    secret.draftValue = '';
  }

  async save(secret: EditableSecret): Promise<void> {
    if (!secret.draftValue.trim()) {
      this.errorMessage.set('أدخل قيمة قبل الحفظ.');
      return;
    }

    this.actionMessage.set(null);
    this.errorMessage.set(null);
    secret.saving = true;

    try {
      await firstValueFrom(
        this.apiClient.put(ApiController.System, SystemOperation.UpdateSecretSetting, {
          key: secret.key,
          value: secret.draftValue.trim()
        })
      );

      this.secrets.update(list =>
        list.map(s => (s.key === secret.key ? { ...s, isSet: true, editing: false, draftValue: '', saving: false } : s))
      );
      this.actionMessage.set(`تم تحديث "${secret.label}".`);
    } catch {
      this.errorMessage.set(`تعذّر تحديث "${secret.label}".`);
      secret.saving = false;
    }
  }
}
