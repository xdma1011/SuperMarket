import { ComponentFixture, TestBed } from '@angular/core/testing';
import { of, throwError } from 'rxjs';
import { EmployeeDto, EmployeesComponent } from './employees.component';
import { ApiClient } from '../../core/api/api-client.service';
import { AuthService } from '../../core/services/auth.service';
import { EmployeesOperation } from '../../core/api/operations';

const KHALED: EmployeeDto = {
  id: 'e1', branchId: 'b1', fullName: 'خالد', phone: null, monthlySalary: 300, isActive: true, notes: null,
  outstandingAdvance: 70, totalSalariesPaid: 0, totalAdvancesGiven: 70, lastSalaryYear: null, lastSalaryMonth: null
};

describe('EmployeesComponent', () => {
  let fixture: ComponentFixture<EmployeesComponent>;
  let component: EmployeesComponent;
  let api: jasmine.SpyObj<ApiClient>;

  beforeEach(async () => {
    api = jasmine.createSpyObj('ApiClient', ['get', 'post', 'put']);
    api.get.and.callFake(((_c: string, operation: string) =>
      operation === EmployeesOperation.List ? of([KHALED, { ...KHALED, id: 'e2', fullName: 'سامر', monthlySalary: 250, outstandingAdvance: 0 }]) : of([])
    ) as unknown as typeof api.get);
    api.post.and.returnValue(of({ netPaid: 230, outstandingAdvance: 0, paidBeforeForPeriod: 0 }));
    api.put.and.returnValue(of({}));

    const authStub = {
      getPublicBranches: () => Promise.resolve([{ id: 'b1', name: 'الرئيسي' }]),
      defaultBranchId: (b: { id: string }[]) => b[0].id,
      currentBranchId: () => 'b1'
    };

    await TestBed.configureTestingModule({
      imports: [EmployeesComponent],
      providers: [{ provide: ApiClient, useValue: api }, { provide: AuthService, useValue: authStub }]
    }).compileComponents();

    fixture = TestBed.createComponent(EmployeesComponent);
    component = fixture.componentInstance;
    fixture.detectChanges();
    await fixture.whenStable();
  });

  it('بيحمّل موظفين الفرع والمجاميع', () => {
    expect(component.branchId).toBe('b1');
    expect(component.employees().length).toBe(2);
    expect(component.totalMonthlySalaries()).toBe(550);
    expect(component.totalOutstandingAdvances()).toBe(70);
  });

  it('صرف راتب: بيعبّي الراتب الشهري وبيخصم السلفة المتبقية افتراضيًا', () => {
    component.openPayForm(KHALED, 'Salary');

    expect(component.payAmount).toBe(300);
    expect(component.payDeduct).toBe(70);
    expect(component.payNet).toBe(230);
    expect(component.payFromDrawer).toBeTrue();
  });

  it('بيبعت الراتب بالشهر والخصم ومفتاح تكرار ثابت للنموذج', async () => {
    component.openPayForm(KHALED, 'Salary');
    component.payMonth = '2026-09';

    await component.submitPayment();

    const [, operation, body, params] = api.post.calls.mostRecent().args as [string, string, Record<string, unknown>, Record<string, string>];
    expect(operation).toBe(EmployeesOperation.RecordPayment);
    expect(params).toEqual({ id: 'e1' });
    expect(body['type']).toBe('Salary');
    expect(body['amount']).toBe(300);
    expect(body['advanceDeducted']).toBe(70);
    expect(body['periodYear']).toBe(2026);
    expect(body['periodMonth']).toBe(9);
    expect(body['paidFromDrawer']).toBeTrue();
    expect(typeof body['clientRequestId']).toBe('string');
    expect(component.payTarget()).toBeNull();
    expect(component.successMessage()).toContain('230.000');
  });

  it('خصم أكتر من السلف المتبقية ما بيتبعت', async () => {
    component.openPayForm(KHALED, 'Salary');
    component.payDeduct = 80;

    await component.submitPayment();

    expect(api.post).not.toHaveBeenCalled();
    expect(component.errorMessage()).toContain('أكتر من السلف');
  });

  it('السلفة بلا شهر وبلا خصم', async () => {
    component.openPayForm(KHALED, 'Advance');
    component.payAmount = 25;
    component.payFromDrawer = false;

    await component.submitPayment();

    const body = api.post.calls.mostRecent().args[2] as Record<string, unknown>;
    expect(body['type']).toBe('Advance');
    expect(body['periodYear']).toBeNull();
    expect(body['advanceDeducted']).toBe(0);
    expect(body['paidFromDrawer']).toBeFalse();
    expect(component.successMessage()).toContain('برّا الصندوق');
  });

  it('راتب تاني لنفس الشهر: بينصرف بس بينبّه', async () => {
    api.post.and.returnValue(of({ netPaid: 10, outstandingAdvance: 0, paidBeforeForPeriod: 300 }));
    component.openPayForm({ ...KHALED, outstandingAdvance: 0 }, 'Salary');
    component.payAmount = 10;

    await component.submitPayment();

    expect(component.successMessage()).toContain('كان منصرف 300.000');
  });

  it('خطأ الباك إند بيبين والنموذج بيضل مفتوح', async () => {
    api.post.and.returnValue(throwError(() => ({ error: { detail: 'الموظف موقوف' } })));
    component.openPayForm(KHALED, 'Advance');
    component.payAmount = 5;

    await component.submitPayment();

    expect(component.errorMessage()).toBe('الموظف موقوف');
    expect(component.payTarget()).not.toBeNull();
  });

  it('موظف جديد بيتبعت مع الفرع، والاسم الفاضي مرفوض', async () => {
    component.openEmployeeForm();
    await component.saveEmployee();
    expect(api.post).not.toHaveBeenCalled();

    component.formName = ' ليلى ';
    component.formSalary = 280;
    await component.saveEmployee();

    const body = api.post.calls.mostRecent().args[2] as Record<string, unknown>;
    expect(body['fullName']).toBe('ليلى');
    expect(body['branchId']).toBe('b1');
    expect(body['monthlySalary']).toBe(280);
  });

  it('السجل لموظف معيّن بيبعت employeeId', async () => {
    await component.showHistory(KHALED);

    const call = api.get.calls.all().filter(c => c.args[1] === EmployeesOperation.Payments).pop()!;
    expect((call.args[3] as Record<string, unknown>)['employeeId']).toBe('e1');
  });
});
