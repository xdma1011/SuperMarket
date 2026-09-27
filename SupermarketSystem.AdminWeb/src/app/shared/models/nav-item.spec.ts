import { NAV_GROUPS, NAV_ICONS, NAV_ITEMS } from './nav-item';

describe('NAV_ITEMS', () => {
  it('كل عنصر عنده id ومسار (route) فريدين (بلا تكرار)', () => {
    const ids = NAV_ITEMS.map(i => i.id);
    const routes = NAV_ITEMS.map(i => i.route);

    expect(new Set(ids).size).toBe(ids.length);
    expect(new Set(routes).size).toBe(routes.length);
  });

  it('كل عنصر عنده label غير فاضٍ', () => {
    for (const item of NAV_ITEMS) {
      expect(item.label.length).toBeGreaterThan(0);
    }
  });

  it('عنصر "الرئيسية" فقط بدون صلاحية مطلوبة (requiredPermission=null)', () => {
    const home = NAV_ITEMS.find(i => i.id === 'home');
    expect(home?.requiredPermission).toBeNull();

    const othersWithoutPermission = NAV_ITEMS.filter(i => i.id !== 'home' && i.requiredPermission === null);
    expect(othersWithoutPermission.length).toBe(0);
  });

  it('كل مسار route يبدأ بـ / (مسار مطلق)', () => {
    for (const item of NAV_ITEMS) {
      expect(item.route.startsWith('/')).toBeTrue();
    }
  });

  it('كل عنصر مربوط بمجموعة موجودة وأيقونة موجودة', () => {
    const groupIds = new Set(NAV_GROUPS.map(g => g.id));
    for (const item of NAV_ITEMS) {
      expect(groupIds.has(item.group)).withContext(item.id).toBeTrue();
      expect(NAV_ICONS[item.icon]?.length).withContext(item.id).toBeGreaterThan(0);
    }
  });

  it('ما في شارة (badge) ثابتة وهمية على أي عنصر', () => {
    expect(NAV_ITEMS.filter(i => i.badge).length).toBe(0);
  });
});
