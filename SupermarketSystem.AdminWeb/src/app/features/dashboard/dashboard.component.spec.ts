import { ComponentFixture, TestBed } from '@angular/core/testing';
import { provideRouter } from '@angular/router';
import { of, throwError } from 'rxjs';
import { DashboardComponent } from './dashboard.component';
import { ApiClient } from '../../core/api/api-client.service';

describe('DashboardComponent', () => {
  let fixture: ComponentFixture<DashboardComponent>;
  let component: DashboardComponent;
  let apiClientSpy: jasmine.SpyObj<ApiClient>;

  function mockAllSucceed() {
    apiClientSpy.get.and.callFake(((controller: string) => {
      switch (controller) {
        case 'reports':
          return of({ period: { invoiceCount: 10, totalSales: 1000, netRevenue: 900 } });
        case 'sales':
          return of({ items: [{ id: 's1', invoiceNumber: 'INV-1', statusTitle: 'مكتملة', totalAmount: 100, createdAtUtc: '' }], totalCount: 1 });
        case 'reviews':
          return of({ totalCount: 2 });
        case 'backups':
          return of({ items: { items: [{ createdAtUtc: new Date().toISOString(), statusCode: 1 }], totalCount: 1 } });
        default:
          return of({ items: [], totalCount: 0 });
      }
    }) as unknown as typeof apiClientSpy.get);
  }

  beforeEach(async () => {
    apiClientSpy = jasmine.createSpyObj('ApiClient', ['get']);
    apiClientSpy.get.and.returnValue(of({ items: [], totalCount: 0 }));

    await TestBed.configureTestingModule({
      imports: [DashboardComponent],
      providers: [{ provide: ApiClient, useValue: apiClientSpy }, provideRouter([])]
    }).compileComponents();

    fixture = TestBed.createComponent(DashboardComponent);
    component = fixture.componentInstance;
  });

  it('يُنشأ المكوّن بنجاح', () => {
    expect(component).toBeTruthy();
  });

  it('يحمّل كل بيانات اللوحة بنجاح عبر Promise.allSettled', async () => {
    mockAllSucceed();

    fixture.detectChanges();
    await fixture.whenStable();

    expect(component.summary()?.totalSales).toBe(1000);
    expect(component.recentInvoices().length).toBe(1);
    expect(component.pendingReviewsCount()).toBe(2);
    expect(component.lastBackupAtUtc()).toBeTruthy();
    expect(component.loading()).toBeFalse();
  });

  it('فشل استدعاء واحد لا يوقف باقي البيانات (allSettled)', async () => {
    apiClientSpy.get.and.callFake(((controller: string) => {
      if (controller === 'reports') return throwError(() => new Error('down'));
      if (controller === 'reviews') return of({ totalCount: 5 });
      return of({ items: [], totalCount: 0 });
    }) as unknown as typeof apiClientSpy.get);

    fixture.detectChanges();
    await fixture.whenStable();

    expect(component.summary()).toBeNull();
    expect(component.pendingReviewsCount()).toBe(5);
  });

  it('لا يعلّم آخر نسخة احتياطية لو كل المحاولات الأخيرة فشلت (statusCode != 1)', async () => {
    apiClientSpy.get.and.callFake(((controller: string) => {
      if (controller === 'backups') return of({ items: { items: [{ createdAtUtc: '', statusCode: 2 }], totalCount: 1 } });
      return of({ items: [], totalCount: 0 });
    }) as unknown as typeof apiClientSpy.get);

    fixture.detectChanges();
    await fixture.whenStable();

    expect(component.lastBackupAtUtc()).toBeNull();
  });

  describe('averageInvoice', () => {
    it('يحسب متوسط الفاتورة صحيحًا', () => {
      component.summary.set({ invoiceCount: 4, totalSales: 400, netRevenue: 380 });
      expect(component.averageInvoice).toBe(100);
    });

    it('يرجّع صفر لو ما في فواتير (تفادي القسمة على صفر)', () => {
      component.summary.set({ invoiceCount: 0, totalSales: 0, netRevenue: 0 });
      expect(component.averageInvoice).toBe(0);
    });

    it('يرجّع صفر لو الملخّص لسه null', () => {
      expect(component.averageInvoice).toBe(0);
    });
  });

  describe('totalAlerts', () => {
    it('يجمع كل التنبيهات الثلاثة', () => {
      component.negativeStockCount.set(2);
      component.reorderNeededCount.set(3);
      component.pendingReviewsCount.set(1);
      expect(component.totalAlerts).toBe(6);
    });
  });

  describe('isBackupStale', () => {
    it('يرجّع false لو ما في تاريخ نسخة معروف (تفادي تنبيه كاذب)', () => {
      component.lastBackupAtUtc.set(null);
      expect(component.isBackupStale).toBeFalse();
    });

    it('يرجّع false لو النسخة حديثة (أقل من 24 ساعة)', () => {
      component.lastBackupAtUtc.set(new Date().toISOString());
      expect(component.isBackupStale).toBeFalse();
    });

    it('يرجّع true لو النسخة أقدم من 24 ساعة', () => {
      const old = new Date(Date.now() - 25 * 60 * 60 * 1000).toISOString();
      component.lastBackupAtUtc.set(old);
      expect(component.isBackupStale).toBeTrue();
    });
  });
});
