import { inject } from '@angular/core';
import { CanActivateFn, Router } from '@angular/router';
import { AuthService } from '../services/auth.service';

/**
 * فحص أولي بالواجهة فقط — راحة للمستخدم (يمنع ومضة صفحة محمية قبل
 * التوجيه)، لا آلية أمان حقيقية. الحماية الفعلية دائمًا بالباك إند
 * (RequirePermissionFilter، D11) — أي حد يقدر يتجاوز هذا الحارس بسهولة
 * من أدوات المطوّر، بس الـAPI نفسه بيرفضه بغض النظر.
 */
export const authGuard: CanActivateFn = () => {
  const authService = inject(AuthService);
  const router = inject(Router);

  if (authService.isAuthenticated()) {
    return true;
  }

  router.navigateByUrl('/login');
  return false;
};
