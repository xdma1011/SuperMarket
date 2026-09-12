import { TestBed } from '@angular/core/testing';
import { Router } from '@angular/router';
import { authGuard } from './auth.guard';
import { AuthService } from '../services/auth.service';

describe('authGuard', () => {
  let authServiceSpy: jasmine.SpyObj<AuthService>;
  let routerSpy: jasmine.SpyObj<Router>;

  beforeEach(() => {
    authServiceSpy = jasmine.createSpyObj('AuthService', [], { isAuthenticated: () => false });
    routerSpy = jasmine.createSpyObj('Router', ['navigateByUrl']);

    TestBed.configureTestingModule({
      providers: [
        { provide: AuthService, useValue: authServiceSpy },
        { provide: Router, useValue: routerSpy }
      ]
    });
  });

  function runGuard(): boolean {
    return TestBed.runInInjectionContext(() => authGuard({} as never, {} as never)) as boolean;
  }

  it('يسمح بالدخول لما المستخدم مسجَّل دخول', () => {
    Object.defineProperty(authServiceSpy, 'isAuthenticated', { value: () => true });

    const result = runGuard();

    expect(result).toBeTrue();
    expect(routerSpy.navigateByUrl).not.toHaveBeenCalled();
  });

  it('يمنع الدخول ويوجّه لصفحة /login لما ما في جلسة فعّالة', () => {
    Object.defineProperty(authServiceSpy, 'isAuthenticated', { value: () => false });

    const result = runGuard();

    expect(result).toBeFalse();
    expect(routerSpy.navigateByUrl).toHaveBeenCalledWith('/login');
  });
});
