import { TestBed } from '@angular/core/testing';
import { of, throwError } from 'rxjs';
import { AuthService } from './auth.service';
import { ApiClient } from '../api/api-client.service';

describe('AuthService', () => {
  let service: AuthService;
  let apiClientSpy: jasmine.SpyObj<ApiClient>;

  beforeEach(() => {
    apiClientSpy = jasmine.createSpyObj('ApiClient', ['get', 'post']);

    TestBed.configureTestingModule({
      providers: [AuthService, { provide: ApiClient, useValue: apiClientSpy }]
    });

    service = TestBed.inject(AuthService);
    sessionStorage.clear();
  });

  afterEach(() => sessionStorage.clear());

  it('يُنشأ بنجاح وبحالة أولية غير مسجَّل دخول', () => {
    expect(service).toBeTruthy();
    expect(service.isAuthenticated()).toBeFalse();
    expect(service.accessToken()).toBeNull();
  });

  describe('getPublicBranches', () => {
    it('يرجّع قائمة الفروع العامة من الـAPI', async () => {
      apiClientSpy.get.and.returnValue(of([{ id: '1', name: 'الفرع الرئيسي' }]));

      const branches = await service.getPublicBranches();

      expect(branches.length).toBe(1);
      expect(branches[0].name).toBe('الفرع الرئيسي');
    });
  });

  describe('login', () => {
    it('يحفظ الجلسة وينجح لما بيانات الاعتماد صحيحة', async () => {
      apiClientSpy.post.and.returnValue(
        of({
          accessToken: 'tok',
          accessTokenExpiresAtUtc: '',
          refreshToken: 'refresh-tok',
          refreshTokenExpiresAtUtc: '',
          userId: 'u1',
          fullName: 'أحمد',
          branchId: 'b1',
          previousSessionRevoked: false
        })
      );

      const result = await service.login('ahmad', 'pass', 'b1');

      expect(result).toEqual({ success: true });
      expect(service.isAuthenticated()).toBeTrue();
      expect(service.accessToken()).toBe('tok');
      expect(service.currentUserFullName()).toBe('أحمد');
      expect(sessionStorage.getItem('refresh_token')).toBe('refresh-tok');
    });

    it('يرجّع رسالة خطأ عربية موحّدة لما تفشل بيانات الاعتماد', async () => {
      apiClientSpy.post.and.returnValue(throwError(() => new Error('401')));

      const result = await service.login('ahmad', 'wrong', null);

      expect(result).toEqual({ success: false, message: 'اسم المستخدم أو كلمة السر غير صحيحة.' });
      expect(service.isAuthenticated()).toBeFalse();
    });
  });

  describe('forceLogoutLocally', () => {
    it('يمسح الجلسة محليًا فورًا بلا أي استدعاء شبكة', () => {
      sessionStorage.setItem('refresh_token', 'x');
      service.forceLogoutLocally();

      expect(service.isAuthenticated()).toBeFalse();
      expect(service.accessToken()).toBeNull();
      expect(sessionStorage.getItem('refresh_token')).toBeNull();
      expect(apiClientSpy.post).not.toHaveBeenCalled();
    });
  });

  describe('logout', () => {
    it('يستدعي endpoint الخروج لو في refreshToken محفوظ ويمسح الجلسة', async () => {
      sessionStorage.setItem('refresh_token', 'refresh-tok');
      apiClientSpy.post.and.returnValue(of({}));

      await service.logout();

      expect(apiClientSpy.post).toHaveBeenCalled();
      expect(service.isAuthenticated()).toBeFalse();
      expect(sessionStorage.getItem('refresh_token')).toBeNull();
    });

    it('ما يفشل حتى لو استدعاء السيرفر فشل (الخروج بالواجهة أولوية)', async () => {
      sessionStorage.setItem('refresh_token', 'refresh-tok');
      apiClientSpy.post.and.returnValue(throwError(() => new Error('network')));

      await expectAsync(service.logout()).toBeResolved();
      expect(service.isAuthenticated()).toBeFalse();
    });

    it('ما يستدعي API إطلاقًا لو ما في refreshToken محفوظ', async () => {
      await service.logout();
      expect(apiClientSpy.post).not.toHaveBeenCalled();
    });
  });

  describe('restoreSession', () => {
    it('ما يسوي شي لو ما في refreshToken محفوظ بـsessionStorage', async () => {
      await service.restoreSession();

      expect(apiClientSpy.post).not.toHaveBeenCalled();
      expect(service.isAuthenticated()).toBeFalse();
    });

    it('يستعيد الجلسة بنجاح ويقرأ اسم المستخدم من الـJWT payload', async () => {
      sessionStorage.setItem('refresh_token', 'old-refresh');
      const payload = { unique_name: 'ahmad.k' };
      const encodedPayload = btoa(JSON.stringify(payload));
      const fakeToken = `header.${encodedPayload}.signature`;

      apiClientSpy.post.and.returnValue(
        of({
          accessToken: fakeToken,
          accessTokenExpiresAtUtc: '',
          refreshToken: 'new-refresh',
          refreshTokenExpiresAtUtc: ''
        })
      );

      await service.restoreSession();

      expect(service.isAuthenticated()).toBeTrue();
      expect(service.currentUserFullName()).toBe('ahmad.k');
      expect(sessionStorage.getItem('refresh_token')).toBe('new-refresh');
    });

    it('يمسح الجلسة بهدوء لو فشل التجديد (توكن منتهي/مُبطَل)', async () => {
      sessionStorage.setItem('refresh_token', 'expired');
      apiClientSpy.post.and.returnValue(throwError(() => new Error('401')));

      await service.restoreSession();

      expect(service.isAuthenticated()).toBeFalse();
      expect(sessionStorage.getItem('refresh_token')).toBeNull();
    });
  });
});
