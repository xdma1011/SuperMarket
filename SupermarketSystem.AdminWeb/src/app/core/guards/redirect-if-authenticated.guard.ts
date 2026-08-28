import { inject } from '@angular/core';
import { CanActivateFn, Router } from '@angular/router';
import { AuthService } from '../services/auth.service';

/**
 * عكس authGuard تمامًا. يُستخدم على مسار /login فقط — لو المستخدم عنده
 * جلسة فعّالة أصلًا (سواء من تسجيل دخول بنفس الجلسة، أو من استعادة
 * ناجحة عند F5 عبر APP_INITIALIZER)، يتوجّه مباشرة للـDashboard بدل ما
 * يشوف نموذج الدخول من جديد.
 */
export const redirectIfAuthenticatedGuard: CanActivateFn = () => {
  const authService = inject(AuthService);
  const router = inject(Router);

  if (authService.isAuthenticated()) {
    router.navigateByUrl('/');
    return false;
  }

  return true;
};
