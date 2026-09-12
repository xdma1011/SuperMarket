import { ComponentFixture, TestBed } from '@angular/core/testing';
import { of, throwError } from 'rxjs';
import { UsersComponent } from './users.component';
import { ApiClient } from '../../core/api/api-client.service';

describe('UsersComponent', () => {
  let fixture: ComponentFixture<UsersComponent>;
  let component: UsersComponent;
  let apiClientSpy: jasmine.SpyObj<ApiClient>;

  const sampleUser = {
    userId: 'u1',
    fullName: 'أحمد',
    username: 'ahmad',
    email: 'a@x.com',
    isActive: true,
    roleNames: ['كاشير'],
    roleId: 'r1',
    defaultBranchName: 'الرئيسي',
    defaultBranchId: 'b1'
  };

  function mockLookupsSuccess() {
    apiClientSpy.get.and.callFake(((controller: string, operation: string) => {
      if (controller === 'users' && operation === 'roles') return of([{ id: 'r1', name: 'كاشير', description: null }]);
      if (controller === 'branches') return of({ items: [{ id: 'b1', name: 'الرئيسي', code: 'MAIN', isActive: true }] });
      return of({ items: [], totalCount: 0 });
    }) as unknown as typeof apiClientSpy.get);
  }

  beforeEach(async () => {
    apiClientSpy = jasmine.createSpyObj('ApiClient', ['get', 'post', 'put']);
    apiClientSpy.get.and.returnValue(of({ items: [], totalCount: 0 }));

    await TestBed.configureTestingModule({
      imports: [UsersComponent],
      providers: [{ provide: ApiClient, useValue: apiClientSpy }]
    }).compileComponents();

    fixture = TestBed.createComponent(UsersComponent);
    component = fixture.componentInstance;
  });

  it('يُنشأ المكوّن بنجاح', () => {
    expect(component).toBeTruthy();
  });

  it('يحمّل المستخدمين والأدوار والفروع، ويختار أول دور وفرع نشط', async () => {
    mockLookupsSuccess();

    fixture.detectChanges();
    await fixture.whenStable();

    expect(component.selectedRoleId).toBe('r1');
    expect(component.selectedBranchId).toBe('b1');
  });

  it('يفلتر الفروع غير النشطة من قائمة الاختيار', async () => {
    apiClientSpy.get.and.callFake(((controller: string, operation: string) => {
      if (controller === 'branches') {
        return of({
          items: [
            { id: 'b1', name: 'نشط', code: 'A', isActive: true },
            { id: 'b2', name: 'موقوف', code: 'B', isActive: false }
          ]
        });
      }
      if (operation === 'roles') return of([]);
      return of({ items: [], totalCount: 0 });
    }) as unknown as typeof apiClientSpy.get);

    fixture.detectChanges();
    await fixture.whenStable();

    expect(component.branches().length).toBe(1);
    expect(component.branches()[0].id).toBe('b1');
  });

  it('يعرض رسالة خطأ عربية واضحة عند فشل تحميل المستخدمين', async () => {
    apiClientSpy.get.and.returnValue(throwError(() => new Error('network')));

    fixture.detectChanges();
    await fixture.whenStable();

    expect(component.errorMessage()).toBe('تعذّر تحميل قائمة المستخدمين.');
  });

  describe('onPageChanged', () => {
    it('يحدّث الصفحة ويعيد التحميل', () => {
      apiClientSpy.get.calls.reset();
      component.onPageChanged({ pageNumber: 2, pageSize: 10 });
      expect(component.pageNumber()).toBe(2);
    });
  });

  describe('openCreateForm / openEditForm / closeForm', () => {
    it('openCreateForm يفتح بوضع إنشاء', () => {
      component.openCreateForm();
      expect(component.isEditMode()).toBeFalse();
      expect(component.formOpen()).toBeTrue();
    });

    it('openEditForm يعبّي الحقول من المستخدم ويفعّل وضع التعديل', () => {
      component.openEditForm(sampleUser);

      expect(component.isEditMode()).toBeTrue();
      expect(component.editingUserId).toBe('u1');
      expect(component.fullName).toBe('أحمد');
      expect(component.password).toBe('');
      expect(component.selectedRoleId).toBe('r1');
    });

    it('closeForm يصفّر الحقول', () => {
      component.openEditForm(sampleUser);
      component.closeForm();

      expect(component.formOpen()).toBeFalse();
      expect(component.fullName).toBe('');
      expect(component.isActive).toBeTrue();
    });
  });

  describe('submit', () => {
    it('يرفض الإرسال بلا اسم كامل أو دور أو فرع', async () => {
      component.fullName = '';

      await component.submit();

      expect(component.formError()).toBe('عبّي كل الحقول المطلوبة.');
      expect(apiClientSpy.post).not.toHaveBeenCalled();
    });

    it('يرفض الإنشاء بلا اسم مستخدم أو كلمة سر', async () => {
      component.fullName = 'أحمد';
      component.selectedRoleId = 'r1';
      component.selectedBranchId = 'b1';
      component.username = '';
      component.password = '';

      await component.submit();

      expect(component.formError()).toBe('اسم المستخدم وكلمة السر مطلوبان عند الإنشاء.');
    });

    it('لا يتطلب اسم مستخدم أو كلمة سر عند التعديل', async () => {
      component.openEditForm(sampleUser);
      apiClientSpy.put.and.returnValue(of({}));

      await component.submit();

      expect(component.formError()).toBeNull();
      expect(apiClientSpy.put).toHaveBeenCalled();
    });

    it('ينشئ مستخدمًا جديدًا بإيميل تلقائي لو ما أدخل إيميل', async () => {
      component.fullName = 'أحمد';
      component.selectedRoleId = 'r1';
      component.selectedBranchId = 'b1';
      component.username = 'ahmad';
      component.password = '123456';
      component.email = '';
      apiClientSpy.post.and.returnValue(of({ userId: 'u1', username: 'ahmad' }));

      await component.submit();

      const [, , body] = apiClientSpy.post.calls.mostRecent().args;
      expect((body as Record<string, unknown>)['email']).toBe('ahmad@local.test');
    });

    it('يعرض رسالة الخطأ التفصيلية من الباك إند عند فشل الإنشاء', async () => {
      component.fullName = 'أحمد';
      component.selectedRoleId = 'r1';
      component.selectedBranchId = 'b1';
      component.username = 'ahmad';
      component.password = '123456';
      apiClientSpy.post.and.returnValue(throwError(() => ({ error: { detail: 'اسم المستخدم مستخدم مسبقًا.' } })));

      await component.submit();

      expect(component.formError()).toBe('اسم المستخدم مستخدم مسبقًا.');
    });

    it('يعرض رسالة عربية عامة مختلفة للتعديل عند الفشل بلا تفاصيل', async () => {
      component.openEditForm(sampleUser);
      apiClientSpy.put.and.returnValue(throwError(() => new Error('network')));

      await component.submit();

      expect(component.formError()).toBe('تعذّر تعديل المستخدم.');
    });
  });
});
