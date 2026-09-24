import { ComponentFixture, TestBed } from '@angular/core/testing';
import { of, throwError } from 'rxjs';
import { FinanceComponent } from './finance.component';
import { ApiClient } from '../../core/api/api-client.service';

describe('FinanceComponent', () => {
  let fixture: ComponentFixture<FinanceComponent>;
  let component: FinanceComponent;
  let apiClientSpy: jasmine.SpyObj<ApiClient>;

  function mockBranchesSuccess() {
    apiClientSpy.get.and.callFake(((controller: string) => {
      if (controller === 'branches') return of({ items: [{ id: 'b1', name: 'الرئيسي' }], totalCount: 1 });
      return of({ items: [], totalCount: 0 });
    }) as unknown as typeof apiClientSpy.get);
  }

  beforeEach(async () => {
    apiClientSpy = jasmine.createSpyObj('ApiClient', ['get', 'post']);
    apiClientSpy.get.and.returnValue(of({ items: [], totalCount: 0 }));

    await TestBed.configureTestingModule({
      imports: [FinanceComponent],
      providers: [{ provide: ApiClient, useValue: apiClientSpy }]
    }).compileComponents();

    fixture = TestBed.createComponent(FinanceComponent);
    component = fixture.componentInstance;
  });

  it('يُنشأ المكوّن بنجاح', () => {
    expect(component).toBeTruthy();
  });

  it('يحمّل الفروع ويختار أول فرع للكشف والنماذج', async () => {
    mockBranchesSuccess();

    fixture.detectChanges();
    await fixture.whenStable();

    expect(component.statementBranchId).toBe('b1');
    expect(component.newExpenseBranchId).toBe('b1');
    expect(component.newCapitalBranchId).toBe('b1');
  });

  describe('setTab', () => {
    it('يبدّل التبويب النشط', () => {
      component.setTab('expenses');
      expect(component.activeTab()).toBe('expenses');

      component.setTab('capital');
      expect(component.activeTab()).toBe('capital');
    });
  });

  describe('loadStatement', () => {
    it('لا يستدعي الـAPI بلا فرع مختار', async () => {
      component.statementBranchId = '';
      apiClientSpy.get.calls.reset();

      await component.loadStatement();

      expect(apiClientSpy.get).not.toHaveBeenCalled();
    });

    it('يعبّي statement عند النجاح', async () => {
      component.statementBranchId = 'b1';
      const response = {
        branchId: 'b1', year: 2026, month: 9,
        totalSales: 100, totalReturnedAmount: 0, netRevenue: 100,
        costOfGoodsSold: 60, itemsExcludedNoCostHistory: 0, grossProfit: 40,
        totalExpenses: 10, expensesByCategory: [{ category: 'Rent' as const, amount: 10 }],
        stocktakeSurplusValue: 5, stocktakeShortageValue: 15, stocktakeMovementsExcludedNoCostHistory: 1,
        wasteLossValue: 7, wasteMovementsExcludedNoCostHistory: 0,
        netProfit: 30
      };
      apiClientSpy.get.and.returnValue(of(response));

      await component.loadStatement();

      expect(component.statement()?.netProfit).toBe(30);
      expect(component.statement()?.grossProfit).toBe(40);
      expect(component.statement()?.stocktakeSurplusValue).toBe(5);
      expect(component.statement()?.stocktakeShortageValue).toBe(15);
      expect(component.statement()?.stocktakeMovementsExcludedNoCostHistory).toBe(1);
      expect(component.statement()?.wasteLossValue).toBe(7);
      expect(component.statementError()).toBeNull();
    });

    it('يعرض رسالة خطأ تفصيلية من الباك إند لو موجودة', async () => {
      component.statementBranchId = 'b1';
      apiClientSpy.get.and.returnValue(throwError(() => ({ error: { detail: 'الفرع غير موجود.' } })));

      await component.loadStatement();

      expect(component.statement()).toBeNull();
      expect(component.statementError()).toBe('الفرع غير موجود.');
    });
  });

  describe('submitExpense', () => {
    it('يرفض الإرسال بلا فرع أو مبلغ صالح', async () => {
      component.newExpenseBranchId = '';
      component.newExpenseAmount = null;

      await component.submitExpense();

      expect(component.expenseFormError()).toContain('عبّي الفرع');
      expect(apiClientSpy.post).not.toHaveBeenCalled();
    });

    it('يرفض مبلغًا صفريًا أو سالبًا', async () => {
      component.newExpenseBranchId = 'b1';
      component.newExpenseAmount = 0;

      await component.submitExpense();

      expect(apiClientSpy.post).not.toHaveBeenCalled();
    });

    it('ينجح، يغلق النموذج، ويعيد تحميل القائمة من الصفحة 1', async () => {
      component.newExpenseBranchId = 'b1';
      component.newExpenseAmount = 50;
      component.expensesPageNumber.set(3);
      apiClientSpy.post.and.returnValue(of({}));
      apiClientSpy.get.and.returnValue(of({ items: [], totalCount: 0 }));

      await component.submitExpense();

      expect(component.expenseFormOpen()).toBeFalse();
      expect(component.expensesPageNumber()).toBe(1);
    });

    it('يعرض رسالة الخطأ التفصيلية من الباك إند لو موجودة', async () => {
      component.newExpenseBranchId = 'b1';
      component.newExpenseAmount = 50;
      apiClientSpy.post.and.returnValue(throwError(() => ({ error: { detail: 'خطأ ما.' } })));

      await component.submitExpense();

      expect(component.expenseFormError()).toBe('خطأ ما.');
    });
  });

  describe('submitCapitalTransaction', () => {
    it('يرفض الإرسال بلا فرع أو مبلغ صالح', async () => {
      component.newCapitalBranchId = '';
      component.newCapitalAmount = null;

      await component.submitCapitalTransaction();

      expect(component.capitalFormError()).toContain('عبّي الفرع');
      expect(apiClientSpy.post).not.toHaveBeenCalled();
    });

    it('ينجح، يغلق النموذج، ويعيد تحميل القائمة من الصفحة 1', async () => {
      component.newCapitalBranchId = 'b1';
      component.newCapitalAmount = 500;
      component.capitalPageNumber.set(2);
      apiClientSpy.post.and.returnValue(of({}));
      apiClientSpy.get.and.returnValue(of({ items: [], totalCount: 0 }));

      await component.submitCapitalTransaction();

      expect(component.capitalFormOpen()).toBeFalse();
      expect(component.capitalPageNumber()).toBe(1);
    });
  });

  describe('openExpenseForm / openCapitalForm', () => {
    it('openExpenseForm يصفّر حقول النموذج ويفتحه', () => {
      component.newExpenseAmount = 999;
      component.openExpenseForm();

      expect(component.expenseFormOpen()).toBeTrue();
      expect(component.newExpenseAmount).toBeNull();
    });

    it('openCapitalForm يصفّر حقول النموذج ويفتحه', () => {
      component.newCapitalAmount = 999;
      component.openCapitalForm();

      expect(component.capitalFormOpen()).toBeTrue();
      expect(component.newCapitalAmount).toBeNull();
    });
  });

  describe('onExpensesPageChanged / onCapitalPageChanged', () => {
    it('onExpensesPageChanged يحدّث الصفحة والحجم', () => {
      component.onExpensesPageChanged({ pageNumber: 3, pageSize: 50 });
      expect(component.expensesPageNumber()).toBe(3);
      expect(component.expensesPageSize()).toBe(50);
    });

    it('onCapitalPageChanged يحدّث الصفحة والحجم', () => {
      component.onCapitalPageChanged({ pageNumber: 2, pageSize: 30 });
      expect(component.capitalPageNumber()).toBe(2);
      expect(component.capitalPageSize()).toBe(30);
    });
  });

  describe('branchName', () => {
    it('يرجّع اسم الفرع لو موجود، أو المعرّف نفسه لو غير موجود', () => {
      component.branches.set([{ id: 'b1', name: 'الرئيسي' }]);

      expect(component.branchName('b1')).toBe('الرئيسي');
      expect(component.branchName('unknown')).toBe('unknown');
    });
  });

  it('يترجم أسماء التصنيفات وأنواع حركات رأس المال القادمة من الباك إند (أسماء لا أرقام)', () => {
    expect(component.categoryLabels['Rent']).toBe('إيجار');
    expect(component.categoryLabels['Electricity']).toBe('كهرباء');
    expect(component.typeLabels['Withdrawal']).toBe('سحب');
  });
});
