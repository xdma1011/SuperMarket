import { ComponentFixture, TestBed } from '@angular/core/testing';
import { of, throwError } from 'rxjs';
import { PartnersComponent, PartnerDto } from './partners.component';
import { ApiClient } from '../../core/api/api-client.service';
import { AuthService } from '../../core/services/auth.service';

const AHMAD: PartnerDto = {
  id: 'p1', branchId: 'b1', fullName: 'أحمد', typeCode: 1, typeTitle: 'شريك رأس مال', userId: null, username: null,
  speculativeProfitPercent: null, isActive: true, notes: null, capitalBalance: 3000, sharesTotal: 2.4,
  withdrawalsTotal: 1, atCostDeductedTotal: 0.8, currentBalance: 0.6
};

describe('PartnersComponent', () => {
  let fixture: ComponentFixture<PartnersComponent>;
  let component: PartnersComponent;
  let api: jasmine.SpyObj<ApiClient>;

  beforeEach(async () => {
    api = jasmine.createSpyObj('ApiClient', ['get', 'post', 'put']);
    api.get.and.callFake(((controller: string, operation: string) => {
      if (controller === 'partners' && operation === '') return of([AHMAD]);
      return of([]);
    }) as unknown as typeof api.get);

    const authStub = {
      getPublicBranches: () => Promise.resolve([{ id: 'b1', name: 'الرئيسي' }]),
      defaultBranchId: (b: { id: string }[]) => b[0].id,
      currentBranchId: () => 'b1'
    };

    await TestBed.configureTestingModule({
      imports: [PartnersComponent],
      providers: [{ provide: ApiClient, useValue: api }, { provide: AuthService, useValue: authStub }]
    }).compileComponents();

    fixture = TestBed.createComponent(PartnersComponent);
    component = fixture.componentInstance;
    fixture.detectChanges();
    await fixture.whenStable();
  });

  it('بيحمّل الشركاء والكشوف والسحوبات والمستحق للفرع', () => {
    expect(component.branchId).toBe('b1');
    expect(component.partners().length).toBe(1);
    expect(component.withdrawalPartnerId).toBe('p1');
    const operations = api.get.calls.allArgs().filter(a => a[0] === ('partners' as never)).map(a => a[1]);
    expect(operations).toEqual(jasmine.arrayContaining(['', 'statements', 'withdrawals', 'owner-receivables']));
  });

  it('الشهر الافتراضي للكشف = الشهر الماضي', () => {
    const now = new Date();
    const prev = new Date(now.getFullYear(), now.getMonth() - 1, 1);
    expect(component.statementMonth).toBe(`${prev.getFullYear()}-${String(prev.getMonth() + 1).padStart(2, '0')}`);
  });

  it('شريك مضارب بلا نسبة ما بينحفظ', async () => {
    await component.openPartnerForm();
    component.formName = 'خالد';
    component.formType = 'Speculative';
    component.formPercent = null;
    await component.savePartner();
    expect(api.post).not.toHaveBeenCalled();
    expect(component.errorMessage()).toContain('نسبة');
  });

  it('شريك جديد بينبعت بالفرع والنوع', async () => {
    api.post.and.returnValue(of({ partnerId: 'new' }));
    await component.openPartnerForm();
    component.formName = 'سامي';
    component.formType = 'Capital';
    await component.savePartner();
    expect(api.post).toHaveBeenCalledWith('partners' as never, '', {
      fullName: 'سامي', userId: null, speculativeProfitPercent: null, notes: null, branchId: 'b1', type: 'Capital'
    });
    expect(component.partnerFormOpen()).toBeFalse();
  });

  it('سحب من الجيب بيبعت المصدر ومفتاح عدم التكرار', async () => {
    spyOn(window, 'confirm').and.returnValue(true);
    api.post.and.returnValue(of({}));
    component.withdrawalAmount = 2;
    component.withdrawalSource = 'OwnerPocket';
    await component.recordWithdrawal();
    const body = api.post.calls.mostRecent().args[2] as Record<string, unknown>;
    expect(body['partnerId']).toBe('p1');
    expect(body['source']).toBe('OwnerPocket');
    expect(typeof body['clientRequestId']).toBe('string');
  });

  it('خطأ إصدار الكشف بيبين رسالة السيرفر', async () => {
    api.post.and.returnValue(throwError(() => ({ error: { detail: 'ما في رأس مال مسجَّل' } })));
    await component.generateStatement();
    expect(component.errorMessage()).toBe('ما في رأس مال مسجَّل');
  });

  it('رقم تلغرام بينحفظ لحاله بعد إنشاء الشريك (بالـid الجديد)، وبلا رقم ما في طلب زيادة', async () => {
    api.post.and.returnValue(of({ partnerId: 'new' }));
    api.put.and.returnValue(of({}));
    await component.openPartnerForm();
    component.formName = 'محمد';
    component.formTelegramPhone = ' 0791112222 ';
    await component.savePartner();
    expect(api.put).toHaveBeenCalledWith('partners' as never, '{id}/telegram-phone', { telegramPhone: '0791112222' }, { id: 'new' });

    api.put.calls.reset();
    await component.openPartnerForm(AHMAD);
    await component.savePartner();
    expect(api.put.calls.allArgs().some(a => a[1] === '{id}/telegram-phone')).toBeFalse();
  });

  it('إصدار باركود شخصي بيعرض الكرت (SVG + الأرقام) مرة وحدة', async () => {
    spyOn(window, 'confirm').and.returnValue(true);
    api.post.and.returnValue(of({ barcode: '12345678901234567890', issuedAtUtc: '2026-09-29T10:00:00Z' }));
    await component.issueBarcode(AHMAD);
    expect(api.post).toHaveBeenCalledWith('partners' as never, '{id}/cashier-barcode', {}, { id: 'p1' });
    const card = component.issuedCard();
    expect(card?.barcode).toBe('12345678901234567890');
    expect(card?.rawSvg).toContain('<svg');
    component.closeIssuedCard();
    expect(component.issuedCard()).toBeNull();
  });

  it('الاسترجاع ما بيتجاوز المستحق', async () => {
    const receivable = { branchId: 'b1', branchName: '', ownerUserId: 'u1', ownerName: 'صاحب المحل', balance: 2, entries: [] };
    component.repayAmount['u1'] = 5;
    await component.repay(receivable);
    expect(api.post).not.toHaveBeenCalled();
    expect(component.errorMessage()).toContain('2.000');
  });
});
