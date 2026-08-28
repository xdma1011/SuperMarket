import { HttpInterceptorFn, HttpErrorResponse } from '@angular/common/http';
import { inject } from '@angular/core';
import { Router } from '@angular/router';
import { catchError, throwError } from 'rxjs';
import { AuthService } from '../services/auth.service';

/**
 * يرفق Authorization: Bearer تلقائيًا لكل طلب، طالما في توكن حالي
 * بالذاكرة. endpoints المعفاة بالباك إند (login) ما بيتأثروا سلبًا برفق
 * توكن فاضٍ — الوسيط هناك أصلًا AllowAnonymous، بيتجاهله.
 *
 * ═══════════════════════════════════════════════════════════════════
 * التعامل العالمي مع 401 — هون بالذات مصدر "تجربة إيقاف الجلسة الحقيقية"
 * ═══════════════════════════════════════════════════════════════════
 * أي طلب يرجع 401 (توكن منتهي، جلسة أوقفها الأدمن، أو أي سبب مصادقة)
 * بيمسح الجلسة محليًا فورًا ويوجّه لصفحة الدخول — بلا ما ننتظر أي مكوّن
 * لحاله يتعامل مع الخطأ. هذا يضمن نفس السلوك بكل مكان بالتطبيق، بدل ما
 * كل صفحة تتعامل مع 401 بطريقتها الخاصة (أو تنساها).
 *
 * استثناء واحد مقصود: طلب /auth/login نفسه لو رجّع 401 (بيانات اعتماد
 * خاطئة) ما لازم يوجّه لصفحة أصلًا هو فيها — LoginComponent بيتعامل مع
 * هذا الخطأ بنفسه (رسالة "اسم مستخدم أو كلمة سر غير صحيحة").
 */
export const authInterceptor: HttpInterceptorFn = (req, next) => {
  const authService = inject(AuthService);
  const router = inject(Router);
  const token = authService.accessToken();

  const authorizedReq = token
    ? req.clone({ setHeaders: { Authorization: `Bearer ${token}` } })
    : req;

  return next(authorizedReq).pipe(
    catchError((error: unknown) => {
      const isUnauthorized = error instanceof HttpErrorResponse && error.status === 401;
      const isLoginRequest = req.url.includes('/auth/login');

      if (isUnauthorized && !isLoginRequest) {
        authService.forceLogoutLocally();
        router.navigateByUrl('/login?sessionExpired=1');
      }

      return throwError(() => error);
    })
  );
};
