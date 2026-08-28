import { Injectable, signal } from '@angular/core';
import { firstValueFrom } from 'rxjs';
import { ApiClient } from '../api/api-client.service';
import { ApiController } from '../api/api-controller.enum';
import { AuthOperation } from '../api/operations';

interface MyPermissionsResponse {
  permissionCodes: string[];
}

/**
 * إخفاء بصري بس، لا حماية فعلية — الحماية الحقيقية دائمًا بالباك إند
 * (RequirePermissionFilter). هذا يمنع مستخدم صلاحياته محدودة من الاصطدام
 * بأزرار/صفحات هو أصلًا ممنوع منها.
 */
@Injectable({ providedIn: 'root' })
export class PermissionsService {
  private readonly codes = signal<Set<string>>(new Set());
  readonly loaded = signal(false);

  constructor(private readonly apiClient: ApiClient) {}

  async load(): Promise<void> {
    try {
      const response = await firstValueFrom(
        this.apiClient.get<MyPermissionsResponse>(ApiController.Auth, AuthOperation.MyPermissions)
      );
      this.codes.set(new Set(response.permissionCodes));
    } catch {
      this.codes.set(new Set());
    } finally {
      this.loaded.set(true);
    }
  }

  has(code: string): boolean {
    return this.codes().has(code);
  }

  hasAny(codes: string[]): boolean {
    return codes.some(c => this.codes().has(c));
  }

  reset(): void {
    this.codes.set(new Set());
    this.loaded.set(false);
  }
}
