import { TestBed } from '@angular/core/testing';
import { of, throwError } from 'rxjs';
import { PermissionsService } from './permissions.service';
import { ApiClient } from '../api/api-client.service';

describe('PermissionsService', () => {
  let service: PermissionsService;
  let apiClientSpy: jasmine.SpyObj<ApiClient>;

  beforeEach(() => {
    apiClientSpy = jasmine.createSpyObj('ApiClient', ['get']);

    TestBed.configureTestingModule({
      providers: [PermissionsService, { provide: ApiClient, useValue: apiClientSpy }]
    });

    service = TestBed.inject(PermissionsService);
  });

  it('يُنشأ بحالة أولية غير محمَّلة وبلا صلاحيات', () => {
    expect(service).toBeTruthy();
    expect(service.loaded()).toBeFalse();
    expect(service.has('Any.Code')).toBeFalse();
  });

  describe('load', () => {
    it('يحمّل رموز الصلاحيات من الـAPI ويعلّم loaded=true', async () => {
      apiClientSpy.get.and.returnValue(of({ permissionCodes: ['Catalog.Edit', 'Sales.View'] }));

      await service.load();

      expect(service.loaded()).toBeTrue();
      expect(service.has('Catalog.Edit')).toBeTrue();
      expect(service.has('Users.Delete')).toBeFalse();
    });

    it('يعلّم loaded=true حتى لو فشل الاستدعاء (fail-safe بصلاحيات فاضية)', async () => {
      apiClientSpy.get.and.returnValue(throwError(() => new Error('network')));

      await service.load();

      expect(service.loaded()).toBeTrue();
      expect(service.has('Catalog.Edit')).toBeFalse();
    });
  });

  describe('hasAny', () => {
    it('يرجّع true لو المستخدم عنده صلاحية واحدة على الأقل من القائمة', async () => {
      apiClientSpy.get.and.returnValue(of({ permissionCodes: ['Sales.View'] }));
      await service.load();

      expect(service.hasAny(['Catalog.Edit', 'Sales.View'])).toBeTrue();
    });

    it('يرجّع false لو ما عنده أي صلاحية من القائمة', async () => {
      apiClientSpy.get.and.returnValue(of({ permissionCodes: [] }));
      await service.load();

      expect(service.hasAny(['Catalog.Edit', 'Sales.View'])).toBeFalse();
    });

    it('يرجّع false على قائمة فاضية', async () => {
      apiClientSpy.get.and.returnValue(of({ permissionCodes: ['Sales.View'] }));
      await service.load();

      expect(service.hasAny([])).toBeFalse();
    });
  });

  describe('reset', () => {
    it('يمسح كل الصلاحيات ويرجّع loaded لـfalse', async () => {
      apiClientSpy.get.and.returnValue(of({ permissionCodes: ['Catalog.Edit'] }));
      await service.load();

      service.reset();

      expect(service.loaded()).toBeFalse();
      expect(service.has('Catalog.Edit')).toBeFalse();
    });
  });
});
