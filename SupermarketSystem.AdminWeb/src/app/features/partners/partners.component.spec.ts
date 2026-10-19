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

  describe('إعادة استثمار الربح برأس المال', () => {
    const PREVIEW = {
      partnerId: 'p1', partnerName: 'أحمد', amount: 0.5, balanceBefore: 0.6, balanceAfter: 0.1, capitalBefore: 3000, capitalAfter: 3000.5,
      sharePercentBefore: 75, sharePercentAfter: 75.01, canReinvest: true, blockedReason: null
    };

    function mockPreview(preview: unknown = PREVIEW): void {
      api.get.and.callFake(((controller: string, operation: string) => {
        if (operation === '{id}/reinvest-preview') return of(preview);
        if (controller === 'partners' && operation === '') return of([AHMAD]);
        return of([]);
      }) as unknown as typeof api.get);
    }

    it('الزر بس للشريك رأس مال فعّال برصيد موجب', () => {
      expect(component.canReinvest(AHMAD)).toBeTrue();
      expect(component.canReinvest({ ...AHMAD, typeCode: 2 })).toBeFalse();
      expect(component.canReinvest({ ...AHMAD, isActive: false })).toBeFalse();
      expect(component.canReinvest({ ...AHMAD, currentBalance: 0 })).toBeFalse();
      expect(component.canReinvest({ ...AHMAD, currentBalance: -3 })).toBeFalse();
    });

    it('التنفيذ مقفول لحد المعاينة + اسم الشريك بالضبط + كلمة السر', async () => {
      mockPreview();
      component.openReinvest(AHMAD);
      expect(component.reinvestReady()).toBeFalse();

      component.reinvestAmount = 0.5;
      await component.loadReinvestPreview();
      expect(component.reinvestPreview()?.canReinvest).toBeTrue();
      expect(component.reinvestReady()).toBeFalse(); // لا اسم ولا كلمة سر

      component.reinvestConfirmName = 'أحمد ';
      expect(component.reinvestReady()).toBeFalse(); // لا كلمة سر
      component.reinvestPassword = 'x';
      expect(component.reinvestReady()).toBeTrue();

      component.reinvestConfirmName = 'احمد'; // اسم غلط (همزة)
      expect(component.reinvestReady()).toBeFalse();
    });

    it('تغيير المبلغ بعد المعاينة بيقفل التنفيذ لحد معاينة جديدة', async () => {
      mockPreview();
      component.openReinvest(AHMAD);
      component.reinvestAmount = 0.5;
      await component.loadReinvestPreview();
      component.reinvestConfirmName = 'أحمد';
      component.reinvestPassword = 'x';
      expect(component.reinvestReady()).toBeTrue();
      component.reinvestAmount = 0.6;
      expect(component.reinvestReady()).toBeFalse();
    });

    it('معاينة مرفوضة (سبب) = التنفيذ مقفول حتى لو الاسم وكلمة السر مكتوبين', async () => {
      mockPreview({ ...PREVIEW, canReinvest: false, blockedReason: 'المبلغ أكبر من رصيد الشريك' });
      component.openReinvest(AHMAD);
      component.reinvestAmount = 5;
      await component.loadReinvestPreview();
      component.reinvestConfirmName = 'أحمد';
      component.reinvestPassword = 'x';
      expect(component.reinvestReady()).toBeFalse();
    });

    it('"كل الرصيد" بيقرّب لتحت ما بيتجاوز الرصيد', async () => {
      mockPreview();
      component.openReinvest({ ...AHMAD, currentBalance: 2.2509 });
      await component.reinvestAll();
      expect(component.reinvestAmount).toBe(2.25);
    });

    it('التنفيذ بيبعت المبلغ وكلمة السر ومفتاح عدم التكرار، وبيسكّر النافذة وبيمسح كلمة السر', async () => {
      mockPreview();
      api.post.and.returnValue(of({}));
      component.openReinvest(AHMAD);
      component.reinvestAmount = 0.5;
      component.reinvestNotes = ' توسعة ';
      await component.loadReinvestPreview();
      component.reinvestConfirmName = 'أحمد';
      component.reinvestPassword = 'secret';
      await component.confirmReinvest();

      const [controller, operation, body, route] = api.post.calls.mostRecent().args as unknown as [string, string, Record<string, unknown>, Record<string, string>];
      expect(controller).toBe('partners');
      expect(operation).toBe('{id}/reinvest');
      expect(route).toEqual({ id: 'p1' });
      expect(body['amount']).toBe(0.5);
      expect(body['password']).toBe('secret');
      expect(body['notes']).toBe('توسعة');
      expect(typeof body['clientRequestId']).toBe('string');
      expect(component.reinvestTarget()).toBeNull();
      expect(component.reinvestPassword).toBe('');
      expect(component.successMessage()).toContain('0.500');
    });

    it('كلمة سر غلط: بتبين الرسالة، بتنمسح كلمة السر، والنافذة بتضل والمفتاح نفسه', async () => {
      mockPreview();
      api.post.and.returnValue(throwError(() => ({ error: { detail: 'كلمة السر غير صحيحة' } })));
      component.openReinvest(AHMAD);
      component.reinvestAmount = 0.5;
      await component.loadReinvestPreview();
      component.reinvestConfirmName = 'أحمد';
      component.reinvestPassword = 'غلط';
      await component.confirmReinvest();
      const firstId = (api.post.calls.mostRecent().args[2] as Record<string, unknown>)['clientRequestId'];

      expect(component.reinvestError()).toBe('كلمة السر غير صحيحة');
      expect(component.reinvestPassword).toBe('');
      expect(component.reinvestTarget()).not.toBeNull();
      expect(component.reinvestReady()).toBeFalse(); // لازم يكتب كلمة السر من جديد

      component.reinvestPassword = 'تاني';
      await component.confirmReinvest();
      expect((api.post.calls.mostRecent().args[2] as Record<string, unknown>)['clientRequestId']).toBe(firstId);
    });
  });
});
