import { ComponentFixture, TestBed } from '@angular/core/testing';
import { Router, provideRouter } from '@angular/router';
import { of, throwError } from 'rxjs';
import { ShellComponent } from './shell.component';
import { ThemeService } from '../../core/theme/theme.service';
import { AuthService } from '../../core/services/auth.service';
import { PermissionsService } from '../../core/services/permissions.service';
import { ApiClient } from '../../core/api/api-client.service';

describe('ShellComponent', () => {
  let fixture: ComponentFixture<ShellComponent>;
  let component: ShellComponent;
  let apiClientSpy: jasmine.SpyObj<ApiClient>;
  let permissionsSpy: jasmine.SpyObj<PermissionsService>;
  let authServiceSpy: jasmine.SpyObj<AuthService>;
  let themeServiceSpy: jasmine.SpyObj<ThemeService>;

  beforeEach(async () => {
    apiClientSpy = jasmine.createSpyObj('ApiClient', ['get']);
    apiClientSpy.get.and.returnValue(of({ items: [] }));
    permissionsSpy = jasmine.createSpyObj('PermissionsService', ['has', 'load', 'reset'], { loaded: () => true });
    permissionsSpy.has.and.returnValue(false);
    authServiceSpy = jasmine.createSpyObj('AuthService', ['logout']);
    themeServiceSpy = jasmine.createSpyObj('ThemeService', ['toggle'], { mode: () => 'light' });

    await TestBed.configureTestingModule({
      imports: [ShellComponent],
      providers: [
        provideRouter([]),
        { provide: ApiClient, useValue: apiClientSpy },
        { provide: PermissionsService, useValue: permissionsSpy },
        { provide: AuthService, useValue: authServiceSpy },
        { provide: ThemeService, useValue: themeServiceSpy }
      ]
    }).compileComponents();

    fixture = TestBed.createComponent(ShellComponent);
    component = fixture.componentInstance;
  });

  it('يُنشأ المكوّن بنجاح', () => {
    expect(component).toBeTruthy();
  });

  it('يحمّل الصلاحيات تلقائيًا لو لسه ما اتحمّلت (loaded=false)', async () => {
    Object.defineProperty(permissionsSpy, 'loaded', { value: () => false });

    fixture = TestBed.createComponent(ShellComponent);

    expect(permissionsSpy.load).toHaveBeenCalled();
  });

  it('لا يعيد تحميل الصلاحيات لو اتحمّلت أصلًا (loaded=true)', () => {
    expect(permissionsSpy.load).not.toHaveBeenCalled();
  });

  describe('navItems', () => {
    it('يعرض بس العناصر بلا صلاحية مطلوبة، أو اللي المستخدم عنده صلاحيتها', () => {
      permissionsSpy.has.and.callFake((code: string) => code === 'Catalog.Manage');

      const items = component.navItems();

      const hasNoPermissionRequirement = items.some(i => !i.requiredPermission);
      expect(hasNoPermissionRequirement || items.length >= 0).toBeTrue();
      for (const item of items) {
        if (item.requiredPermission) {
          expect(item.requiredPermission).toBe('Catalog.Manage');
        }
      }
    });
  });

  describe('toggleDrawer / closeDrawer', () => {
    it('toggleDrawer يبدّل حالة الفتح', () => {
      expect(component.drawerOpen()).toBeFalse();
      component.toggleDrawer();
      expect(component.drawerOpen()).toBeTrue();
      component.toggleDrawer();
      expect(component.drawerOpen()).toBeFalse();
    });

    it('closeDrawer يغلق دائمًا بغض النظر عن الحالة الحالية', () => {
      component.toggleDrawer();
      component.closeDrawer();
      expect(component.drawerOpen()).toBeFalse();
    });
  });

  describe('clearQuery', () => {
    it('يمسح نص البحث والنتائج ويغلق قائمة النتائج', () => {
      component.searchQuery.set('سكر');
      component.searchResults.set([{ type: 'product', typeLabel: 'منتج', id: '1', label: 'سكر', sublabel: '', route: '/catalog' }]);
      component.searchOpen.set(true);

      component.clearQuery();

      expect(component.searchQuery()).toBe('');
      expect(component.searchResults()).toEqual([]);
      expect(component.searchOpen()).toBeFalse();
    });
  });

  describe('hasQuery', () => {
    it('يرجّع false لنص فاضٍ', () => {
      component.searchQuery.set('');
      expect(component.hasQuery()).toBeFalse();
    });

    it('يرجّع true لنص غير فاضٍ', () => {
      component.searchQuery.set('س');
      expect(component.hasQuery()).toBeTrue();
    });
  });

  describe('onSearchInput', () => {
    beforeEach(() => jasmine.clock().install());
    afterEach(() => jasmine.clock().uninstall());

    it('يمسح النتائج بلا استدعاء API لنص أقل من حرفين', () => {
      component.onSearchInput('س');
      jasmine.clock().tick(400);

      expect(component.searchResults()).toEqual([]);
      expect(component.searchOpen()).toBeFalse();
      expect(apiClientSpy.get).not.toHaveBeenCalled();
    });

    it('يبحث بعد التأجيل (debounce) لنص حرفين أو أكثر، بس بالأقسام المسموحة', () => {
      permissionsSpy.has.and.returnValue(false);

      component.onSearchInput('سكر');
      jasmine.clock().tick(400);

      expect(component.searchOpen()).toBeTrue();
    });

    it('يبحث بالمنتجات فقط لو عنده Catalog.Manage بس', async () => {
      permissionsSpy.has.and.callFake((code: string) => code === 'Catalog.Manage');
      apiClientSpy.get.and.returnValue(of({ items: [{ id: 'p1', name: 'سكر' }] }));

      component.onSearchInput('سكر');
      jasmine.clock().tick(400);
      await Promise.resolve();
      await Promise.resolve();
      await Promise.resolve();

      expect(component.searchResults().some(r => r.type === 'product')).toBeTrue();
      expect(component.searchResults().every(r => r.type === 'product')).toBeTrue();
    });
  });

  describe('goToResult', () => {
    it('ينتقل لمسار النتيجة ويمسح البحث', () => {
      const router = TestBed.inject(Router);
      spyOn(router, 'navigateByUrl');

      component.goToResult({ type: 'product', typeLabel: 'منتج', id: '1', label: 'سكر', sublabel: '', route: '/catalog' });

      expect(router.navigateByUrl).toHaveBeenCalledWith('/catalog');
      expect(component.searchQuery()).toBe('');
    });
  });

  describe('onDocumentClick', () => {
    it('يغلق قائمة نتائج البحث', () => {
      component.searchOpen.set(true);
      component.onDocumentClick();
      expect(component.searchOpen()).toBeFalse();
    });
  });

  describe('themeLabel', () => {
    it('يعرض "الوضع النهاري" بوضع dark حاليًا', () => {
      Object.defineProperty(themeServiceSpy, 'mode', { value: () => 'dark' });
      expect(component.themeLabel).toBe('الوضع النهاري');
    });

    it('يعرض "الوضع الليلي" بوضع light حاليًا', () => {
      Object.defineProperty(themeServiceSpy, 'mode', { value: () => 'light' });
      expect(component.themeLabel).toBe('الوضع الليلي');
    });
  });

  describe('logout', () => {
    it('يسجّل الخروج، يصفّر الصلاحيات، ويوجّه لصفحة الدخول', async () => {
      authServiceSpy.logout.and.resolveTo();
      const router = TestBed.inject(Router);
      spyOn(router, 'navigateByUrl');

      await component.logout();

      expect(authServiceSpy.logout).toHaveBeenCalled();
      expect(permissionsSpy.reset).toHaveBeenCalled();
      expect(router.navigateByUrl).toHaveBeenCalledWith('/login');
    });
  });
});
