import { Injectable } from '@angular/core';
import { HttpClient, HttpParams } from '@angular/common/http';
import { Observable } from 'rxjs';
import { environment } from '../../../environments/environment';
import { ApiController } from './api-controller.enum';

export type RouteParams = Record<string, string>;
export type QueryParams = Record<string, string | number | boolean | null | undefined>;

/**
 * كل استدعاء API بالتطبيق لازم يمر من هون — لا HttpClient مباشر بأي
 * خدمة feature. هذا يضمن قاعدة "أسماء الـcontrollers والعمليات من Enum
 * دائمًا" فعليًا مُطبَّقة، لا مجرد اتفاق شفهي: نص حر لمسار API ما بيصير
 * ممكن أصلًا إلا لو تجاوز حدا هذا الصنف عمدًا.
 *
 * operation فارغة ('') تعني "جذر الـcontroller نفسه" (مثال:
 * SalesOperation.Complete = '' يعني POST /sales مباشرة).
 *
 * routeParams: استبدال أنماط {id} بالمسار بقيم فعلية — مثال:
 *   buildUrl(ApiController.Sales, SalesOperation.Void, { id: saleId })
 *   => /api/v1/sales/{saleId}/void
 */
@Injectable({ providedIn: 'root' })
export class ApiClient {
  constructor(private readonly http: HttpClient) {}

  get<T>(
    controller: ApiController,
    operation: string,
    routeParams?: RouteParams,
    queryParams?: QueryParams
  ): Observable<T> {
    return this.http.get<T>(this.buildUrl(controller, operation, routeParams), {
      params: this.buildHttpParams(queryParams)
    });
  }

  post<T>(controller: ApiController, operation: string, body: unknown = {}, routeParams?: RouteParams): Observable<T> {
    return this.http.post<T>(this.buildUrl(controller, operation, routeParams), body);
  }

  put<T>(controller: ApiController, operation: string, body: unknown = {}, routeParams?: RouteParams): Observable<T> {
    return this.http.put<T>(this.buildUrl(controller, operation, routeParams), body);
  }

  delete<T>(controller: ApiController, operation: string, routeParams?: RouteParams): Observable<T> {
    return this.http.delete<T>(this.buildUrl(controller, operation, routeParams));
  }

  private buildUrl(controller: ApiController, operation: string, routeParams?: RouteParams): string {
    let path: string = operation ? `${controller}/${operation}` : controller;

    if (routeParams) {
      for (const [key, value] of Object.entries(routeParams)) {
        path = path.replace(`{${key}}`, encodeURIComponent(value));
      }
    }

    return `${environment.apiBaseUrl}/${path}`;
  }

  private buildHttpParams(queryParams?: QueryParams): HttpParams {
    let params = new HttpParams();

    if (!queryParams) {
      return params;
    }

    for (const [key, value] of Object.entries(queryParams)) {
      if (value !== null && value !== undefined) {
        params = params.set(key, String(value));
      }
    }

    return params;
  }
}
