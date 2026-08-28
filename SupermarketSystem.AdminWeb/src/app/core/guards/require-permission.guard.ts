import { inject } from '@angular/core';
import { CanActivateFn, Router } from '@angular/router';
import { PermissionsService } from '../services/permissions.service';

/**
 * دفاع بعمق إضافي فوق إخفاء عناصر الـSidebar — بلا هذا الحارس، إخفاء
 * رابط بالقائمة الجانبية ما يمنع مستخدم يكتب الرابط يدويًا بالمتصفح من
 * فتح الصفحة.
 *
 * يفترض permissionsService محمَّلة أصلًا (ShellComponent بيحمّلها عند
 * أول دخول). لو لسه ما اكتمل التحميل، يسمح مؤقتًا (fail-open بالواجهة
 * بس) — الحماية الحقيقية دائمًا بالباك إند بغض النظر.
 */
export function requirePermissionGuard(permissionCode: string): CanActivateFn {
  return () => {
    const permissionsService = inject(PermissionsService);
    const router = inject(Router);

    if (!permissionsService.loaded() || permissionsService.has(permissionCode)) {
      return true;
    }

    router.navigateByUrl('/');
    return false;
  };
}
