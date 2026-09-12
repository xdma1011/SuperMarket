import { TestBed } from '@angular/core/testing';
import { HttpTestingController, provideHttpClientTesting } from '@angular/common/http/testing';
import { provideHttpClient } from '@angular/common/http';
import { ApiClient } from './api-client.service';
import { ApiController } from './api-controller.enum';
import { environment } from '../../../environments/environment';

describe('ApiClient', () => {
  let apiClient: ApiClient;
  let httpMock: HttpTestingController;

  beforeEach(() => {
    TestBed.configureTestingModule({
      providers: [ApiClient, provideHttpClient(), provideHttpClientTesting()]
    });

    apiClient = TestBed.inject(ApiClient);
    httpMock = TestBed.inject(HttpTestingController);
  });

  afterEach(() => {
    httpMock.verify();
  });

  it('يُنشأ بنجاح', () => {
    expect(apiClient).toBeTruthy();
  });

  describe('get', () => {
    it('يبني الرابط الصحيح بدون operation (جذر الـcontroller)', () => {
      apiClient.get<unknown>(ApiController.Branches, '').subscribe();

      const req = httpMock.expectOne(`${environment.apiBaseUrl}/${ApiController.Branches}`);
      expect(req.request.method).toBe('GET');
      req.flush([]);
    });

    it('يبني الرابط الصحيح مع operation فرعية', () => {
      apiClient.get<unknown>(ApiController.Auth, 'my-permissions').subscribe();

      const req = httpMock.expectOne(`${environment.apiBaseUrl}/${ApiController.Auth}/my-permissions`);
      expect(req.request.method).toBe('GET');
      req.flush({ permissionCodes: [] });
    });

    it('يستبدل routeParams بالمسار فعليًا', () => {
      apiClient.get<unknown>(ApiController.Products, '{productId}/units', { productId: 'p1' }).subscribe();

      const req = httpMock.expectOne(`${environment.apiBaseUrl}/${ApiController.Products}/p1/units`);
      expect(req.request.url).toBe(`${environment.apiBaseUrl}/${ApiController.Products}/p1/units`);
      req.flush([]);
    });

    it('يرمّز قيم routeParams (encodeURIComponent)', () => {
      apiClient.get<unknown>(ApiController.Products, 'by-barcode/{barcodeValue}', { barcodeValue: 'a b/c' }).subscribe();

      const expectedUrl = `${environment.apiBaseUrl}/${ApiController.Products}/by-barcode/${encodeURIComponent('a b/c')}`;
      const req = httpMock.expectOne(expectedUrl);
      expect(req.request.url).toBe(expectedUrl);
      req.flush({});
    });

    it('يضيف queryParams فعليًا للطلب', () => {
      apiClient
        .get<unknown>(ApiController.Sales, '', undefined, { pageNumber: 2, pageSize: 20 })
        .subscribe();

      const req = httpMock.expectOne(
        (r) => r.url === `${environment.apiBaseUrl}/${ApiController.Sales}` && r.params.get('pageNumber') === '2' && r.params.get('pageSize') === '20'
      );
      expect(req.request.params.get('pageNumber')).toBe('2');
      expect(req.request.params.get('pageSize')).toBe('20');
      req.flush({});
    });

    it('يتجاهل قيم queryParams الفاضية (null/undefined)', () => {
      apiClient
        .get<unknown>(ApiController.Sales, '', undefined, { pageNumber: 1, search: null, branchId: undefined })
        .subscribe();

      const req = httpMock.expectOne(
        (r) => r.url === `${environment.apiBaseUrl}/${ApiController.Sales}`
      );
      expect(req.request.params.has('search')).toBeFalse();
      expect(req.request.params.has('branchId')).toBeFalse();
      expect(req.request.params.get('pageNumber')).toBe('1');
      req.flush({});
    });
  });

  describe('post', () => {
    it('يرسل POST بالـbody الصحيح', () => {
      const body = { username: 'a', password: 'b' };
      apiClient.post<unknown>(ApiController.Auth, 'login', body).subscribe();

      const req = httpMock.expectOne(`${environment.apiBaseUrl}/${ApiController.Auth}/login`);
      expect(req.request.method).toBe('POST');
      expect(req.request.body).toEqual(body);
      req.flush({});
    });

    it('يستخدم body فاضي افتراضيًا لو ما انبعث', () => {
      apiClient.post<unknown>(ApiController.Auth, 'logout').subscribe();

      const req = httpMock.expectOne(`${environment.apiBaseUrl}/${ApiController.Auth}/logout`);
      expect(req.request.body).toEqual({});
      req.flush({});
    });
  });

  describe('put', () => {
    it('يرسل PUT بالمسار الصحيح بعد استبدال routeParams', () => {
      apiClient.put<unknown>(ApiController.Products, '{productId}', { name: 'x' }, { productId: 'p9' }).subscribe();

      const req = httpMock.expectOne(`${environment.apiBaseUrl}/${ApiController.Products}/p9`);
      expect(req.request.method).toBe('PUT');
      req.flush({});
    });
  });

  describe('delete', () => {
    it('يرسل DELETE بالمسار الصحيح', () => {
      apiClient.delete<unknown>(ApiController.Branches, '{id}', { id: 'b1' }).subscribe();

      const req = httpMock.expectOne(`${environment.apiBaseUrl}/${ApiController.Branches}/b1`);
      expect(req.request.method).toBe('DELETE');
      req.flush({});
    });
  });
});
