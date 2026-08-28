import { Injectable, signal } from '@angular/core';
import { firstValueFrom } from 'rxjs';
import { ApiClient } from '../api/api-client.service';
import { ApiController } from '../api/api-controller.enum';
import { AuthOperation } from '../api/operations';

export type ClientAppType = 'Cashier' | 'Admin';

export interface LoginRequest {
  username: string;
  password: string;
  appType: ClientAppType;
  branchId: string | null;
}

export interface LoginResponse {
  accessToken: string;
  accessTokenExpiresAtUtc: string;
  refreshToken: string;
  refreshTokenExpiresAtUtc: string;
  userId: string;
  fullName: string;
  branchId: string | null;
  previousSessionRevoked: boolean;
}

export interface RefreshTokenResponse {
  accessToken: string;
  accessTokenExpiresAtUtc: string;
  refreshToken: string;
  refreshTokenExpiresAtUtc: string;
}

export interface PublicBranchDto {
  id: string;
  name: string;
}

/**
 * التوكن الحي (accessToken) يُحفظ بالذاكرة فقط (signal)، لا بـlocalStorage
 * ولا sessionStorage — لو تسرّبت الصفحة لـXSS، ما في توكن جاهز يُقرأ من
 * التخزين. refreshToken وحده يُحفظ بـsessionStorage (يُمحى تلقائيًا لما
 * التبويب يُقفل).
 *
 * استمرارية الجلسة عند F5: الذاكرة (وبالتالي accessToken) تُمحى تلقائيًا
 * عند أي تحديث للصفحة — هذا طبيعي بـJavaScript. الحل: عند إقلاع التطبيق
 * (main.ts، عبر APP_INITIALIZER)، نحاول تجديد صامت باستخدام refreshToken
 * المخزَّن بـsessionStorage *قبل* ما الموجّه (Router) يقرر أي صفحة يفتح.
 * لو نجح التجديد، المستخدم يضل داخل التطبيق بلا ما يحس بأي انقطاع.
 */
@Injectable({ providedIn: 'root' })
export class AuthService {
  private readonly refreshTokenStorageKey = 'refresh_token';

  readonly accessToken = signal<string | null>(null);
  readonly currentUserFullName = signal<string | null>(null);
  readonly isAuthenticated = signal<boolean>(false);

  constructor(private readonly apiClient: ApiClient) {}

  async getPublicBranches(): Promise<PublicBranchDto[]> {
    return firstValueFrom(
      this.apiClient.get<PublicBranchDto[]>(ApiController.Auth, AuthOperation.PublicBranches)
    );
  }

  async login(
    username: string,
    password: string,
    branchId: string | null
  ): Promise<{ success: true } | { success: false; message: string }> {
    const body: LoginRequest = { username, password, appType: 'Admin', branchId };

    try {
      const response = await firstValueFrom(
        this.apiClient.post<LoginResponse>(ApiController.Auth, AuthOperation.Login, body)
      );

      this.applySession(response.accessToken, response.refreshToken, response.fullName);

      return { success: true };
    } catch {
      // نفس رسالة الباك إند الموحّدة بالضبط — بلا تمييز بين الأسباب.
      return { success: false, message: 'اسم المستخدم أو كلمة السر غير صحيحة.' };
    }
  }

  /**
   * مسح محلي فوري بلا أي استدعاء شبكة — يُستخدم لما الباك إند نفسه رجّع
   * 401 (يعني الجلسة أصلًا مش صالحة عنده، استدعاء /auth/logout كان رح
   * يفشل بنفس السبب). بخلاف logout() العادية، ما فيها async ولا محاولة
   * إبطال بالسيرفر — الجلسة أصلًا مُبطَلة هناك.
   */
  forceLogoutLocally(): void {
    this.clearSession();
  }

  async logout(): Promise<void> {
    const refreshToken = sessionStorage.getItem(this.refreshTokenStorageKey);

    this.clearSession();

    if (refreshToken) {
      try {
        await firstValueFrom(this.apiClient.post(ApiController.Auth, AuthOperation.Logout, { refreshToken }));
      } catch {
        /* متعمَّد: فشل استدعاء الخروج بالسيرفر لا يوقف الخروج بالواجهة. */
      }
    }
  }

  /**
   * يُستدعى مرة وحدة عند إقلاع التطبيق (APP_INITIALIZER) — قبل ما
   * الموجّه يقرر أي مسار يفعّل. لو ما في refreshToken محفوظ، أو فشل
   * التجديد (منتهي، مُبطَل، إلخ)، الجلسة تبقى فاضية بهدوء — المستخدم
   * ببساطة يوصله authGuard لصفحة الدخول، بلا أي رسالة خطأ مزعجة.
   */
  async restoreSession(): Promise<void> {
    const refreshToken = sessionStorage.getItem(this.refreshTokenStorageKey);
    if (!refreshToken) {
      return;
    }

    try {
      const response = await firstValueFrom(
        this.apiClient.post<RefreshTokenResponse>(ApiController.Auth, AuthOperation.Refresh, { refreshToken })
      );

      const username = this.extractUsernameFromToken(response.accessToken);
      this.applySession(response.accessToken, response.refreshToken, username ?? '');
    } catch {
      this.clearSession();
    }
  }

  private applySession(accessToken: string, refreshToken: string, fullNameOrUsername: string): void {
    this.accessToken.set(accessToken);
    this.currentUserFullName.set(fullNameOrUsername);
    this.isAuthenticated.set(true);
    sessionStorage.setItem(this.refreshTokenStorageKey, refreshToken);
  }

  private clearSession(): void {
    this.accessToken.set(null);
    this.currentUserFullName.set(null);
    this.isAuthenticated.set(false);
    sessionStorage.removeItem(this.refreshTokenStorageKey);
  }

  /**
   * فك ترميز جزء الـpayload من الـJWT محليًا (بلا أي استدعاء شبكة) لقراءة
   * اسم المستخدم (unique_name) — endpoint التجديد لا يرجّع الاسم الكامل
   * أصلًا (تصميم الباك إند: التجديد يرجّع توكنات فقط)، والتوكن نفسه أصلًا
   * حامل الاسم بداخله. هذا عرض واجهة بحت، لا قرار أمني — القيمة المعروضة
   * لا تُستخدم لأي فحص صلاحية، فك الترميز بلا تحقق من التوقيع كافٍ تمامًا
   * هون (التحقق الفعلي من صحة التوكن دائمًا مسؤولية الباك إند).
   */
  private extractUsernameFromToken(token: string): string | null {
    try {
      const payloadBase64 = token.split('.')[1];
      const payloadJson = atob(payloadBase64.replace(/-/g, '+').replace(/_/g, '/'));
      const payload = JSON.parse(payloadJson) as Record<string, unknown>;
      return (payload['unique_name'] as string) ?? null;
    } catch {
      return null;
    }
  }
}
