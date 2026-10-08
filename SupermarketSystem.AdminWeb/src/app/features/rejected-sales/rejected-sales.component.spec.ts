import { ComponentFixture, TestBed } from '@angular/core/testing';
import { of, throwError } from 'rxjs';
import { RejectedSalesComponent } from './rejected-sales.component';
import { ApiClient } from '../../core/api/api-client.service';

describe('RejectedSalesComponent', () => {
  let fixture: ComponentFixture<RejectedSalesComponent>;
  let component: RejectedSalesComponent;
  let apiClientSpy: jasmine.SpyObj<ApiClient>;

  function rejected(id = 'r1', isResolved = false) {
    return {
      id,
      clientRequestId: 'c1',
      branchId: 'b1',
      branchName: 'الفرع الرئيسي',
      cashierName: 'كاشير',
      errorCode: 'Sale.PaymentsExceedTotal',
      errorMessage: 'الدفعات أكبر من الإجمالي',
      paidAmountHint: 5,
      itemCount: 2,
      attemptCount: 3,
      firstAttemptAtUtc: '2026-10-08T10:00:00Z',
      lastAttemptAtUtc: '2026-10-08T10:03:00Z',
      isResolved,
      resolvedAtUtc: null,
      resolvedByName: null,
      resolutionNote: null,
      resolvedAutomatically: false
    };
  }

  beforeEach(async () => {
    apiClientSpy = jasmine.createSpyObj('ApiClient', ['get', 'post']);
    apiClientSpy.get.and.returnValue(of({ items: [], totalCount: 0, pageNumber: 1, pageSize: 100 }));
    apiClientSpy.post.and.returnValue(of({}));

    await TestBed.configureTestingModule({
      imports: [RejectedSalesComponent],
      providers: [{ provide: ApiClient, useValue: apiClientSpy }]
    }).compileComponents();

    fixture = TestBed.createComponent(RejectedSalesComponent);
    component = fixture.componentInstance;
  });

  it('يحمّل البيعات المرفوضة المفتوحة عند ngOnInit', async () => {
    apiClientSpy.get.and.returnValue(of({ items: [rejected()], totalCount: 1, pageNumber: 1, pageSize: 100 }));

    fixture.detectChanges();
    await fixture.whenStable();

    expect(component.items().length).toBe(1);
    const queryParams = apiClientSpy.get.calls.mostRecent().args[3] as Record<string, unknown>;
    expect(queryParams['includeResolved']).toBeFalse();
  });

  it('يعرض رسالة خطأ عربية عند فشل التحميل', async () => {
    apiClientSpy.get.and.returnValue(throwError(() => new Error('network')));

    await component.load();

    expect(component.errorMessage()).toBe('تعذّر تحميل البيعات المرفوضة.');
  });

  it('زر عرض المعالَجة كمان بيبعت includeResolved=true', async () => {
    await component.load();
    component.toggleIncludeResolved();
    await fixture.whenStable();

    const queryParams = apiClientSpy.get.calls.mostRecent().args[3] as Record<string, unknown>;
    expect(queryParams['includeResolved']).toBeTrue();
  });

  it('تأكيد المعالجة بيبعت الملاحظة ويعيد التحميل', async () => {
    const item = rejected();
    component.startResolve(item);
    component.resolveNote.set('  دخلتها يدويًا  ');

    await component.confirmResolve(item);

    const args = apiClientSpy.post.calls.mostRecent().args;
    expect(args[2]).toEqual({ note: 'دخلتها يدويًا' });
    expect(args[3]).toEqual({ id: 'r1' });
    expect(component.resolvingId()).toBeNull();
  });

  it('فشل المعالجة بيطلع رسالة ولا يخفي البيعة', async () => {
    apiClientSpy.post.and.returnValue(throwError(() => new Error('x')));
    const item = rejected();
    component.items.set([item]);

    await component.confirmResolve(item);

    expect(component.errorMessage()).toBe('تعذّر تعليم البيعة كمعالَجة.');
    expect(component.items().length).toBe(1);
  });
});
