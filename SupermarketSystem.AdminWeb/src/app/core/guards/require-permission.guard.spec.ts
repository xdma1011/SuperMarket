import { TestBed } from '@angular/core/testing';
import { Router } from '@angular/router';
import { requirePermissionGuard } from './require-permission.guard';
import { PermissionsService } from '../services/permissions.service';

describe('requirePermissionGuard', () => {
  let permissionsSpy: jasmine.SpyObj<PermissionsService>;
  let routerSpy: jasmine.SpyObj<Router>;

  beforeEach(() => {
    permissionsSpy = jasmine.createSpyObj('PermissionsService', ['has'], { loaded: () => true });
    routerSpy = jasmine.createSpyObj('Router', ['navigateByUrl']);

    TestBed.configureTestingModule({
      providers: [
        { provide: PermissionsService, useValue: permissionsSpy },
        { provide: Router, useValue: routerSpy }
      ]
    });
  });

  function runGuard(code: string): boolean {
    const guard = requirePermissionGuard(code);
    return TestBed.runInInjectionContext(() => guard({} as never, {} as never)) as boolean;
  }

  it('يسمح بالدخول لما المستخدم عنده الصلاحية المطلوبة', () => {
    permissionsSpy.has.and.returnValue(true);

    const result = runGuard('Catalog.Edit');

    expect(result).toBeTrue();
    expect(routerSpy.navigateByUrl).not.toHaveBeenCalled();
  });

  it('يمنع الدخول ويوجّه للرئيسية لما ما عنده الصلاحية والتحميل خلص', () => {
    permissionsSpy.has.and.returnValue(false);

    const result = runGuard('Catalog.Edit');

    expect(result).toBeFalse();
    expect(routerSpy.navigateByUrl).toHaveBeenCalledWith('/');
  });

  it('يسمح مؤقتًا (fail-open) لو الصلاحيات لسه ما اكتمل تحميلها', () => {
    Object.defineProperty(permissionsSpy, 'loaded', { value: () => false });
    permissionsSpy.has.and.returnValue(false);

    const result = runGuard('Catalog.Edit');

    expect(result).toBeTrue();
    expect(routerSpy.navigateByUrl).not.toHaveBeenCalled();
  });
});
