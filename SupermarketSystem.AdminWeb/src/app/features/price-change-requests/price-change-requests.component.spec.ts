import { ComponentFixture, TestBed } from '@angular/core/testing';
import { of, throwError } from 'rxjs';
import { PriceChangeRequestsComponent } from './price-change-requests.component';
import { ApiClient } from '../../core/api/api-client.service';

describe('PriceChangeRequestsComponent', () => {
  let fixture: ComponentFixture<PriceChangeRequestsComponent>;
  let component: PriceChangeRequestsComponent;
  let apiClientSpy: jasmine.SpyObj<ApiClient>;

  const sampleRequest = {
    id: 'r1',
    productBranchId: 'pb1',
    productId: 'p1',
    productName: 'سكر',
    branchName: 'الفرع الرئيسي',
    previousPrice: 5,
    requestedPrice: 6,
    requestedByUserName: 'ahmad',
    requestedAtUtc: ''
  };

  beforeEach(async () => {
    apiClientSpy = jasmine.createSpyObj('ApiClient', ['get', 'post']);
    apiClientSpy.get.and.returnValue(of([]));

    await TestBed.configureTestingModule({
      imports: [PriceChangeRequestsComponent],
      providers: [{ provide: ApiClient, useValue: apiClientSpy }]
    }).compileComponents();

    fixture = TestBed.createComponent(PriceChangeRequestsComponent);
    component = fixture.componentInstance;
  });

  it('يُنشأ المكوّن بنجاح', () => {
    expect(component).toBeTruthy();
  });

  it('يحمّل طلبات تعديل السعر تلقائيًا عند ngOnInit', async () => {
    apiClientSpy.get.and.returnValue(of([sampleRequest]));

    fixture.detectChanges();
    await fixture.whenStable();

    expect(component.requests().length).toBe(1);
    expect(component.loading()).toBeFalse();
  });

  it('يعرض رسالة خطأ عربية واضحة عند فشل تحميل الطلبات', async () => {
    apiClientSpy.get.and.returnValue(throwError(() => new Error('network')));

    fixture.detectChanges();
    await fixture.whenStable();

    expect(component.errorMessage()).toBe('تعذّر تحميل طلبات تعديل السعر.');
  });

  describe('approve', () => {
    it('يوافق على الطلب ويعيد تحميل القائمة', async () => {
      apiClientSpy.post.and.returnValue(of({}));

      await component.approve(sampleRequest);

      expect(apiClientSpy.post).toHaveBeenCalledWith(
        jasmine.anything(),
        jasmine.anything(),
        {},
        { requestId: 'r1' }
      );
      expect(apiClientSpy.get).toHaveBeenCalled();
      expect(component.decidingId()).toBeNull();
    });

    it('يعرض رسالة خطأ عربية واضحة عند فشل الموافقة', async () => {
      apiClientSpy.post.and.returnValue(throwError(() => new Error('network')));

      await component.approve(sampleRequest);

      expect(component.errorMessage()).toBe('تعذّر الموافقة على الطلب.');
      expect(component.decidingId()).toBeNull();
    });
  });

  describe('reject', () => {
    it('يرفض الطلب ويعيد تحميل القائمة', async () => {
      apiClientSpy.post.and.returnValue(of({}));

      await component.reject(sampleRequest);

      expect(apiClientSpy.post).toHaveBeenCalledWith(
        jasmine.anything(),
        jasmine.anything(),
        {},
        { requestId: 'r1' }
      );
    });

    it('يعرض رسالة خطأ عربية واضحة عند فشل الرفض', async () => {
      apiClientSpy.post.and.returnValue(throwError(() => new Error('network')));

      await component.reject(sampleRequest);

      expect(component.errorMessage()).toBe('تعذّر رفض الطلب.');
    });
  });
});
