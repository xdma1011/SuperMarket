import { ComponentFixture, TestBed } from '@angular/core/testing';
import { ActivatedRoute, convertToParamMap, Router } from '@angular/router';
import { LoginComponent } from './login.component';
import { AuthService } from '../../core/services/auth.service';
import { PermissionsService } from '../../core/services/permissions.service';

describe('LoginComponent', () => {
  let fixture: ComponentFixture<LoginComponent>;
  let component: LoginComponent;
  let authServiceSpy: jasmine.SpyObj<AuthService>;
  let permissionsSpy: jasmine.SpyObj<PermissionsService>;
  let routerSpy: jasmine.SpyObj<Router>;
  let queryParamMap: Map<string, string>;

  beforeEach(async () => {
    authServiceSpy = jasmine.createSpyObj('AuthService', ['getPublicBranches', 'login']);
    permissionsSpy = jasmine.createSpyObj('PermissionsService', ['reset', 'load', 'has']);
    routerSpy = jasmine.createSpyObj('Router', ['navigateByUrl']);
    queryParamMap = new Map();

    authServiceSpy.getPublicBranches.and.resolveTo([]);

    await TestBed.configureTestingModule({
      imports: [LoginComponent],
      providers: [
        { provide: AuthService, useValue: authServiceSpy },
        { provide: PermissionsService, useValue: permissionsSpy },
        { provide: Router, useValue: routerSpy },
        {
          provide: ActivatedRoute,
          useValue: { snapshot: { queryParamMap: convertToParamMap(Object.fromEntries(queryParamMap)) } }
        }
      ]
    }).compileComponents();

    fixture = TestBed.createComponent(LoginComponent);
    component = fixture.componentInstance;
  });

  it('يُنشأ المكوّن بنجاح', () => {
    expect(component).toBeTruthy();
  });

  describe('ngOnInit', () => {
    it('يحمّل قائمة الفروع العامة عند الإقلاع', async () => {
      authServiceSpy.getPublicBranches.and.resolveTo([{ id: 'b1', name: 'الفرع الرئيسي' }]);

      await component.ngOnInit();

      expect(component.branches()).toEqual([{ id: 'b1', name: 'الفرع الرئيسي' }]);
    });

    it('لا يمنع الإقلاع لو فشل تحميل الفروع', async () => {
      authServiceSpy.getPublicBranches.and.rejectWith(new Error('network'));

      await expectAsync(component.ngOnInit()).toBeResolved();
      expect(component.branches()).toEqual([]);
    });

    it('يعرض رسالة صريحة لما sessionExpired=1 بالرابط', async () => {
      const routeWithExpiredParam = TestBed.inject(ActivatedRoute);
      (routeWithExpiredParam.snapshot.queryParamMap as unknown) = convertToParamMap({ sessionExpired: '1' });

      await component.ngOnInit();

      expect(component.errorMessage()).toBe('انتهت جلستك أو تم إيقافها. الرجاء تسجيل الدخول من جديد.');
    });
  });

  describe('onSubmit', () => {
    it('يعرض رسالة تحقق لو اسم المستخدم فاضي، بلا استدعاء login', async () => {
      component.username = '';
      component.password = 'x';

      await component.onSubmit();

      expect(component.errorMessage()).toBe('الرجاء إدخال اسم المستخدم وكلمة السر.');
      expect(authServiceSpy.login).not.toHaveBeenCalled();
    });

    it('يعرض رسالة تحقق لو كلمة السر فاضية', async () => {
      component.username = 'ahmad';
      component.password = '';

      await component.onSubmit();

      expect(component.errorMessage()).toBe('الرجاء إدخال اسم المستخدم وكلمة السر.');
      expect(authServiceSpy.login).not.toHaveBeenCalled();
    });

    it('يوجّه للصفحة الرئيسية عند نجاح الدخول لمستخدم عادي', async () => {
      component.username = 'ahmad';
      component.password = 'pass';
      authServiceSpy.login.and.resolveTo({ success: true });
      permissionsSpy.load.and.resolveTo();
      permissionsSpy.has.and.returnValue(false);

      await component.onSubmit();

      expect(permissionsSpy.reset).toHaveBeenCalled();
      expect(permissionsSpy.load).toHaveBeenCalled();
      expect(routerSpy.navigateByUrl).toHaveBeenCalledWith('/');
    });

    it('يوجّه لصفحة السائق /driver لو عنده Orders.Deliver بلا Sales.Create', async () => {
      component.username = 'driver1';
      component.password = 'pass';
      authServiceSpy.login.and.resolveTo({ success: true });
      permissionsSpy.load.and.resolveTo();
      permissionsSpy.has.and.callFake((code: string) => code === 'Orders.Deliver');

      await component.onSubmit();

      expect(routerSpy.navigateByUrl).toHaveBeenCalledWith('/driver');
    });

    it('يعرض رسالة الخطأ ويمسح كلمة السر عند فشل الدخول', async () => {
      component.username = 'ahmad';
      component.password = 'wrong';
      authServiceSpy.login.and.resolveTo({ success: false, message: 'اسم المستخدم أو كلمة السر غير صحيحة.' });

      await component.onSubmit();

      expect(component.errorMessage()).toBe('اسم المستخدم أو كلمة السر غير صحيحة.');
      expect(component.password).toBe('');
      expect(routerSpy.navigateByUrl).not.toHaveBeenCalled();
    });

    it('يعلّم isSubmitting أثناء العملية ويعيده false بعدها', async () => {
      component.username = 'ahmad';
      component.password = 'pass';
      authServiceSpy.login.and.resolveTo({ success: false, message: 'خطأ' });

      const submitPromise = component.onSubmit();
      expect(component.isSubmitting()).toBeTrue();

      await submitPromise;
      expect(component.isSubmitting()).toBeFalse();
    });
  });
});
