import { TestBed } from '@angular/core/testing';
import { Router } from '@angular/router';
import { redirectIfAuthenticatedGuard } from './redirect-if-authenticated.guard';
import { AuthService } from '../services/auth.service';

describe('redirectIfAuthenticatedGuard', () => {
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
    return TestBed.runInInjectionContext(() => redirectIfAuthenticatedGuard({} as never, {} as never)) as boolean;
  }

  it('يوجّه للصفحة الرئيسية ويمنع فتح /login لما في جلسة فعّالة أصلًا', () => {
    Object.defineProperty(authServiceSpy, 'isAuthenticated', { value: () => true });

    const result = runGuard();

    expect(result).toBeFalse();
    expect(routerSpy.navigateByUrl).toHaveBeenCalledWith('/');
  });

  it('يسمح بفتح /login لما ما في جلسة فعّالة', () => {
    Object.defineProperty(authServiceSpy, 'isAuthenticated', { value: () => false });

    const result = runGuard();

    expect(result).toBeTrue();
    expect(routerSpy.navigateByUrl).not.toHaveBeenCalled();
  });
});
