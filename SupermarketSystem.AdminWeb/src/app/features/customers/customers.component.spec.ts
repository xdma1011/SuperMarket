import { ComponentFixture, TestBed } from '@angular/core/testing';
import { of, throwError } from 'rxjs';
import { CustomersComponent } from './customers.component';
import { ApiClient } from '../../core/api/api-client.service';

describe('CustomersComponent', () => {
  let fixture: ComponentFixture<CustomersComponent>;
  let component: CustomersComponent;
  let apiClientSpy: jasmine.SpyObj<ApiClient>;

  const sampleCustomer = {
    id: 'c1',
    fullName: 'أحمد',
    phone: '0790000000',
    email: null,
    isBlocked: false,
    createdAtUtc: ''
  };

  beforeEach(async () => {
    apiClientSpy = jasmine.createSpyObj('ApiClient', ['get', 'post']);
    apiClientSpy.get.and.returnValue(of({ items: [], totalCount: 0 }));

    await TestBed.configureTestingModule({
      imports: [CustomersComponent],
      providers: [{ provide: ApiClient, useValue: apiClientSpy }]
    }).compileComponents();

    fixture = TestBed.createComponent(CustomersComponent);
    component = fixture.componentInstance;
    jasmine.clock().install();
  });

  afterEach(() => {
    jasmine.clock().uninstall();
  });

  it('يُنشأ المكوّن بنجاح', () => {
    expect(component).toBeTruthy();
  });

  describe('loadCustomers', () => {
    it('يحمّل قائمة الزبائن ويحدّث totalCount', async () => {
      apiClientSpy.get.and.returnValue(of({ items: [sampleCustomer], totalCount: 1 }));

      await component.loadCustomers();

      expect(component.customers().length).toBe(1);
      expect(component.totalCount()).toBe(1);
      expect(component.loading()).toBeFalse();
    });

    it('يعرض رسالة خطأ عربية واضحة عند فشل التحميل', async () => {
      apiClientSpy.get.and.returnValue(throwError(() => new Error('network')));

      await component.loadCustomers();

      expect(component.errorMessage()).toBe('تعذّر تحميل قائمة الزبائن.');
    });
  });

  describe('onSearchChange', () => {
    it('يحدّث searchQuery فورًا', () => {
      component.onSearchChange('أحمد');
      expect(component.searchQuery()).toBe('أحمد');
    });

    it('يؤجّل استدعاء التحميل (debounce) ويرجع لصفحة 1', () => {
      component.pageNumber.set(3);
      apiClientSpy.get.calls.reset();

      component.onSearchChange('سكر');
      expect(apiClientSpy.get).not.toHaveBeenCalled();

      jasmine.clock().tick(350);

      expect(component.pageNumber()).toBe(1);
      expect(apiClientSpy.get).toHaveBeenCalled();
    });

    it('يلغي المؤقت السابق لو انبعث بحث جديد قبل انتهاء التأجيل', () => {
      apiClientSpy.get.calls.reset();

      component.onSearchChange('أ');
      jasmine.clock().tick(100);
      component.onSearchChange('أح');
      jasmine.clock().tick(100);

      expect(apiClientSpy.get).not.toHaveBeenCalled();

      jasmine.clock().tick(250);
      expect(apiClientSpy.get).toHaveBeenCalledTimes(1);
    });
  });

  describe('onPageChanged', () => {
    it('يحدّث رقم وحجم الصفحة ويعيد التحميل فورًا (بلا debounce)', () => {
      apiClientSpy.get.calls.reset();

      component.onPageChanged({ pageNumber: 2, pageSize: 50 });

      expect(component.pageNumber()).toBe(2);
      expect(component.pageSize()).toBe(50);
      expect(apiClientSpy.get).toHaveBeenCalled();
    });
  });

  describe('toggleBlock', () => {
    it('يستدعي عملية الحظر لزبون غير محظور', async () => {
      apiClientSpy.post.and.returnValue(of({}));

      await component.toggleBlock(sampleCustomer);

      expect(apiClientSpy.post).toHaveBeenCalledWith(jasmine.anything(), '{customerId}/block', {}, { customerId: 'c1' });
    });

    it('يستدعي عملية إلغاء الحظر لزبون محظور أصلًا', async () => {
      apiClientSpy.post.and.returnValue(of({}));

      await component.toggleBlock({ ...sampleCustomer, isBlocked: true });

      expect(apiClientSpy.post).toHaveBeenCalledWith(jasmine.anything(), '{customerId}/unblock', {}, { customerId: 'c1' });
    });

    it('يعرض رسالة خطأ عربية واضحة عند فشل تغيير الحظر', async () => {
      apiClientSpy.post.and.returnValue(throwError(() => new Error('network')));

      await component.toggleBlock(sampleCustomer);

      expect(component.errorMessage()).toBe('تعذّر تغيير حالة حظر الزبون.');
      expect(component.togglingId()).toBeNull();
    });
  });
});
