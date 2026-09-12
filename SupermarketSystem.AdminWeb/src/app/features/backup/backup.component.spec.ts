import { ComponentFixture, TestBed } from '@angular/core/testing';
import { of, throwError } from 'rxjs';
import { BackupComponent } from './backup.component';
import { ApiClient } from '../../core/api/api-client.service';
import { AuthService } from '../../core/services/auth.service';

describe('BackupComponent', () => {
  let fixture: ComponentFixture<BackupComponent>;
  let component: BackupComponent;
  let apiClientSpy: jasmine.SpyObj<ApiClient>;
  let authServiceSpy: jasmine.SpyObj<AuthService>;

  const emptyBackups = { items: { items: [], totalCount: 0 }, stats: { totalCount: 0, totalSizeBytes: 0 } };

  beforeEach(async () => {
    apiClientSpy = jasmine.createSpyObj('ApiClient', ['get', 'post', 'delete']);
    apiClientSpy.get.and.returnValue(of(emptyBackups));
    authServiceSpy = jasmine.createSpyObj('AuthService', [], { accessToken: () => 'tok' });

    await TestBed.configureTestingModule({
      imports: [BackupComponent],
      providers: [
        { provide: ApiClient, useValue: apiClientSpy },
        { provide: AuthService, useValue: authServiceSpy }
      ]
    }).compileComponents();

    fixture = TestBed.createComponent(BackupComponent);
    component = fixture.componentInstance;
  });

  it('يُنشأ المكوّن بنجاح', () => {
    expect(component).toBeTruthy();
  });

  it('يحمّل النسخ الاحتياطية والإحصائيات عند ngOnInit', async () => {
    apiClientSpy.get.and.returnValue(
      of({ items: { items: [{ id: 'bk1', fileName: 'a.bak', fileSizeBytes: 100, statusCode: 1, statusTitle: 'ناجحة', errorMessage: null, createdAtUtc: new Date().toISOString() }], totalCount: 1 }, stats: { totalCount: 1, totalSizeBytes: 100 } })
    );

    fixture.detectChanges();
    await fixture.whenStable();

    expect(component.backups().length).toBe(1);
    expect(component.stats().totalCount).toBe(1);
  });

  it('يعرض رسالة خطأ عربية واضحة عند فشل التحميل', async () => {
    apiClientSpy.get.and.returnValue(throwError(() => new Error('network')));

    fixture.detectChanges();
    await fixture.whenStable();

    expect(component.errorMessage()).toBe('تعذّر تحميل قائمة النسخ الاحتياطية.');
  });

  describe('hasFreshBackupToday', () => {
    it('يرجّع true لو في نسخة ناجحة بتاريخ اليوم', () => {
      component.backups.set([
        { id: '1', fileName: 'a', fileSizeBytes: 1, statusCode: 1, statusTitle: 'ناجحة', errorMessage: null, createdAtUtc: new Date().toISOString() }
      ]);
      expect(component.hasFreshBackupToday).toBeTrue();
    });

    it('يرجّع false لو النسخ كلها فاشلة أو بتاريخ سابق', () => {
      component.backups.set([
        { id: '1', fileName: 'a', fileSizeBytes: 1, statusCode: 2, statusTitle: 'فشلت', errorMessage: 'خطأ', createdAtUtc: new Date().toISOString() }
      ]);
      expect(component.hasFreshBackupToday).toBeFalse();
    });

    it('يرجّع false لو ما في أي نسخ إطلاقًا', () => {
      component.backups.set([]);
      expect(component.hasFreshBackupToday).toBeFalse();
    });
  });

  describe('triggerBackup', () => {
    it('ينشئ نسخة جديدة ويعيد التحميل عند النجاح', async () => {
      apiClientSpy.post.and.returnValue(of({}));

      await component.triggerBackup();

      expect(component.actionMessage()).toBe('تم إنشاء نسخة احتياطية جديدة بنجاح.');
      expect(component.triggering()).toBeFalse();
    });

    it('يعرض رسالة خطأ عربية واضحة عند الفشل', async () => {
      apiClientSpy.post.and.returnValue(throwError(() => new Error('network')));

      await component.triggerBackup();

      expect(component.errorMessage()).toBe('فشل إنشاء النسخة الاحتياطية — تأكد إعدادات السيرفر (BACKUP DATABASE).');
    });
  });

  describe('deleteBackup', () => {
    const backup = { id: 'bk1', fileName: 'a.bak', fileSizeBytes: 1, statusCode: 1, statusTitle: 'ناجحة', errorMessage: null, createdAtUtc: '' };

    it('يحذف النسخة ويعيد التحميل عند النجاح', async () => {
      apiClientSpy.delete.and.returnValue(of({}));

      await component.deleteBackup(backup);

      expect(component.actionMessage()).toBe('تم حذف النسخة الاحتياطية.');
    });

    it('يعرض رسالة الخطأ التفصيلية من الباك إند لو موجودة', async () => {
      apiClientSpy.delete.and.returnValue(throwError(() => ({ error: { detail: 'النسخة قيد الاستخدام.' } })));

      await component.deleteBackup(backup);

      expect(component.errorMessage()).toBe('النسخة قيد الاستخدام.');
    });
  });

  describe('formatSize', () => {
    it('يعرض بايتات لحجم أقل من 1024', () => {
      expect(component.formatSize(500)).toBe('500 B');
    });

    it('يعرض كيلوبايت لحجم بين 1024 و1024*1024', () => {
      expect(component.formatSize(2048)).toBe('2.0 KB');
    });

    it('يعرض ميغابايت لحجم أكبر من 1024*1024', () => {
      expect(component.formatSize(5 * 1024 * 1024)).toBe('5.0 MB');
    });
  });

  describe('createAndDownload', () => {
    it('ينشئ رسالة نجاح ويعيد التحميل عند نجاح الطلب المباشر', async () => {
      const fakeBlob = new Blob(['x']);
      spyOn(window, 'fetch').and.resolveTo(
        new Response(fakeBlob, { status: 200, headers: { 'Content-Disposition': 'attachment; filename="backup_1.bak"' } })
      );
      spyOn(window.URL, 'createObjectURL').and.returnValue('blob:fake');
      spyOn(window.URL, 'revokeObjectURL');

      await component.createAndDownload();

      expect(component.actionMessage()).toBe('تم إنشاء نسخة احتياطية وتنزيلها مباشرة.');
      expect(component.downloadingDirectly()).toBeFalse();
    });

    it('يعرض رسالة خطأ عربية واضحة لو فشل الطلب (response.ok=false)', async () => {
      spyOn(window, 'fetch').and.resolveTo(new Response(null, { status: 500 }));

      await component.createAndDownload();

      expect(component.errorMessage()).toBe('فشل الإنشاء والتنزيل المباشر — لقاعدة بيانات كبيرة، العملية قد تأخذ وقتًا أطول.');
    });
  });

  describe('download', () => {
    it('يعرض رسالة خطأ عربية واضحة لو فشل التنزيل', async () => {
      spyOn(window, 'fetch').and.resolveTo(new Response(null, { status: 404 }));

      await component.download({ id: 'bk1', fileName: 'a.bak', fileSizeBytes: 1, statusCode: 1, statusTitle: 'ناجحة', errorMessage: null, createdAtUtc: '' });

      expect(component.errorMessage()).toBe('تعذّر تنزيل النسخة الاحتياطية.');
    });
  });
});
