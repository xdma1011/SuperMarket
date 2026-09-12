import { ComponentFixture, TestBed } from '@angular/core/testing';
import { PaginationComponent } from './pagination.component';

describe('PaginationComponent', () => {
  let fixture: ComponentFixture<PaginationComponent>;
  let component: PaginationComponent;

  beforeEach(async () => {
    await TestBed.configureTestingModule({
      imports: [PaginationComponent],
    }).compileComponents();

    fixture = TestBed.createComponent(PaginationComponent);
    component = fixture.componentInstance;
  });

  it('يُنشأ المكوّن بنجاح', () => {
    expect(component).toBeTruthy();
  });

  describe('totalPages', () => {
    it('يحسب عدد الصفحات صحيحًا (تقريب لأعلى)', () => {
      component.totalCount = 45;
      component.pageSize = 20;
      expect(component.totalPages).toBe(3);
    });

    it('يرجع صفحة واحدة على الأقل حتى لو totalCount صفر', () => {
      component.totalCount = 0;
      component.pageSize = 20;
      expect(component.totalPages).toBe(1);
    });
  });

  describe('fromRecord / toRecord', () => {
    it('يحسب مدى السجلات المعروضة بالصفحة الأولى', () => {
      component.pageNumber = 1;
      component.pageSize = 20;
      component.totalCount = 45;
      expect(component.fromRecord).toBe(1);
      expect(component.toRecord).toBe(20);
    });

    it('يحسب مدى السجلات بالصفحة الأخيرة (أقل من حجم الصفحة الكامل)', () => {
      component.pageNumber = 3;
      component.pageSize = 20;
      component.totalCount = 45;
      expect(component.fromRecord).toBe(41);
      expect(component.toRecord).toBe(45);
    });

    it('يرجع صفر لما ما في أي سجلات', () => {
      component.pageNumber = 1;
      component.totalCount = 0;
      expect(component.fromRecord).toBe(0);
    });
  });

  describe('canGoPrev / canGoNext', () => {
    it('ما يقدر يرجع للخلف من الصفحة الأولى', () => {
      component.pageNumber = 1;
      component.totalCount = 45;
      component.pageSize = 20;
      expect(component.canGoPrev).toBeFalse();
      expect(component.canGoNext).toBeTrue();
    });

    it('ما يقدر يتقدم للأمام من آخر صفحة', () => {
      component.pageNumber = 3;
      component.totalCount = 45;
      component.pageSize = 20;
      expect(component.canGoNext).toBeFalse();
      expect(component.canGoPrev).toBeTrue();
    });
  });

  describe('goTo', () => {
    it('يبعث changed بالصفحة الجديدة لما تكون صالحة', () => {
      component.pageNumber = 1;
      component.pageSize = 20;
      component.totalCount = 45;

      const emitted: { pageNumber: number; pageSize: number }[] = [];
      component.changed.subscribe((e) => emitted.push(e));

      component.goTo(2);

      expect(emitted).toEqual([{ pageNumber: 2, pageSize: 20 }]);
    });

    it('ما يبعث أي شي لصفحة أقل من 1', () => {
      component.pageNumber = 1;
      component.totalCount = 45;
      component.pageSize = 20;

      let emitted = false;
      component.changed.subscribe(() => (emitted = true));

      component.goTo(0);

      expect(emitted).toBeFalse();
    });

    it('ما يبعث أي شي لصفحة أكبر من العدد الكلي', () => {
      component.pageNumber = 1;
      component.totalCount = 45;
      component.pageSize = 20;

      let emitted = false;
      component.changed.subscribe(() => (emitted = true));

      component.goTo(99);

      expect(emitted).toBeFalse();
    });

    it('ما يبعث أي شي لنفس الصفحة الحالية', () => {
      component.pageNumber = 2;
      component.totalCount = 45;
      component.pageSize = 20;

      let emitted = false;
      component.changed.subscribe(() => (emitted = true));

      component.goTo(2);

      expect(emitted).toBeFalse();
    });
  });

  describe('onPageSizeChange', () => {
    it('يرجع لصفحة 1 دائمًا عند تغيير حجم الصفحة', () => {
      component.pageNumber = 3;

      const emitted: { pageNumber: number; pageSize: number }[] = [];
      component.changed.subscribe((e) => emitted.push(e));

      component.onPageSizeChange(50);

      expect(emitted).toEqual([{ pageNumber: 1, pageSize: 50 }]);
    });
  });
});
