import { TestBed } from '@angular/core/testing';
import { HttpErrorResponse, HttpEvent, HttpHandlerFn, HttpRequest, HttpResponse } from '@angular/common/http';
import { Router } from '@angular/router';
import { Observable, of, throwError } from 'rxjs';
import { authInterceptor } from './auth.interceptor';
import { AuthService } from '../services/auth.service';

describe('authInterceptor', () => {
  let authServiceSpy: jasmine.SpyObj<AuthService>;
  let routerSpy: jasmine.SpyObj<Router>;

  beforeEach(() => {
    authServiceSpy = jasmine.createSpyObj('AuthService', ['forceLogoutLocally'], { accessToken: () => null });
    routerSpy = jasmine.createSpyObj('Router', ['navigateByUrl']);

    TestBed.configureTestingModule({
      providers: [
        { provide: AuthService, useValue: authServiceSpy },
        { provide: Router, useValue: routerSpy }
      ]
    });
  });

  function run(req: HttpRequest<unknown>, next: HttpHandlerFn) {
    return TestBed.runInInjectionContext(() => authInterceptor(req, next));
  }

  it('يرفق Authorization: Bearer لما في توكن حالي', (done) => {
    Object.defineProperty(authServiceSpy, 'accessToken', { value: () => 'token-123' });
    const req = new HttpRequest('GET', '/api/v1/branches');
    const next: HttpHandlerFn = (r): Observable<HttpEvent<unknown>> => {
      expect(r.headers.get('Authorization')).toBe('Bearer token-123');
      return of(new HttpResponse({ status: 200 }));
    };

    run(req, next).subscribe(() => done());
  });

  it('ما يرفق أي Authorization header لو ما في توكن', (done) => {
    Object.defineProperty(authServiceSpy, 'accessToken', { value: () => null });
    const req = new HttpRequest('GET', '/api/v1/branches');
    const next: HttpHandlerFn = (r): Observable<HttpEvent<unknown>> => {
      expect(r.headers.has('Authorization')).toBeFalse();
      return of(new HttpResponse({ status: 200 }));
    };

    run(req, next).subscribe(() => done());
  });

  it('يمسح الجلسة ويوجّه لصفحة الدخول عند 401 على طلب غير login', (done) => {
    const req = new HttpRequest('GET', '/api/v1/branches');
    const error = new HttpErrorResponse({ status: 401 });
    const next: HttpHandlerFn = () => throwError(() => error);

    run(req, next).subscribe({
      error: () => {
        expect(authServiceSpy.forceLogoutLocally).toHaveBeenCalled();
        expect(routerSpy.navigateByUrl).toHaveBeenCalledWith('/login?sessionExpired=1');
        done();
      }
    });
  });

  it('ما يمسح الجلسة عند 401 لطلب /auth/login نفسه (LoginComponent بيتعامل معه)', (done) => {
    const req = new HttpRequest('POST', '/api/v1/auth/login', {});
    const error = new HttpErrorResponse({ status: 401 });
    const next: HttpHandlerFn = () => throwError(() => error);

    run(req, next).subscribe({
      error: () => {
        expect(authServiceSpy.forceLogoutLocally).not.toHaveBeenCalled();
        expect(routerSpy.navigateByUrl).not.toHaveBeenCalled();
        done();
      }
    });
  });

  it('ما يتعامل مع أخطاء غير 401 (يمررها كما هي بلا مسح جلسة)', (done) => {
    const req = new HttpRequest('GET', '/api/v1/branches');
    const error = new HttpErrorResponse({ status: 500 });
    const next: HttpHandlerFn = () => throwError(() => error);

    run(req, next).subscribe({
      error: (e) => {
        expect(e).toBe(error);
        expect(authServiceSpy.forceLogoutLocally).not.toHaveBeenCalled();
        done();
      }
    });
  });
});
