import { routes } from './app.routes';
import { NAV_ITEMS } from './shared/models/nav-item';

/**
 * فحص تكامل بحت — بلا تحميل فعلي لأي مكوّن (lazy loadComponent) ولا
 * أي HTTP. الهدف: يمسك أخطاء واقعية زي مسار جديد بـNAV_ITEMS بلا route
 * مطابق فعليًا مسجَّل هون (رابط بالقائمة الجانبية بيوصل لصفحة 404).
 */
describe('app.routes', () => {
  function childPaths(): string[] {
    const shell = routes.find(r => r.path === '');
    return (shell?.children ?? []).map(c => c.path ?? '');
  }

  it('يحتوي على مسار /login ومسار جذر محمي بـauthGuard', () => {
    expect(routes.some(r => r.path === 'login')).toBeTrue();
    const shell = routes.find(r => r.path === '');
    expect(shell?.canActivate?.length).toBeGreaterThan(0);
  });

  it('يحتوي على مسار wildcard (**) يعيد التوجيه للرئيسية', () => {
    const wildcard = routes.find(r => r.path === '**');
    expect(wildcard?.redirectTo).toBe('');
  });

  it('كل مسار فرعي (بغير الرئيسية الفاضية) فريد، بلا تكرار', () => {
    const paths = childPaths().filter(p => p !== '');
    expect(new Set(paths).size).toBe(paths.length);
  });

  it('كل رابط بـNAV_ITEMS (غير الرئيسية) له مسار فرعي مسجَّل فعليًا بـapp.routes', () => {
    const paths = new Set(childPaths());

    for (const item of NAV_ITEMS) {
      if (item.route === '/') continue;
      const expectedPath = item.route.replace(/^\//, '');
      expect(paths.has(expectedPath)).withContext(`route ${item.route} (nav item ${item.id})`).toBeTrue();
    }
  });

  it('كل مسار فرعي محمي بصلاحية (غير الرئيسية الفاضية) عنده requirePermissionGuard مُفعَّل', () => {
    const shell = routes.find(r => r.path === '');
    for (const child of shell?.children ?? []) {
      if (child.path === '') continue;
      expect(child.canActivate?.length).withContext(`path: ${child.path}`).toBeGreaterThan(0);
    }
  });
});
