import { ComponentFixture, TestBed } from '@angular/core/testing';
import { of, throwError } from 'rxjs';
import { ReviewsComponent } from './reviews.component';
import { ApiClient } from '../../core/api/api-client.service';

describe('ReviewsComponent', () => {
  let fixture: ComponentFixture<ReviewsComponent>;
  let component: ReviewsComponent;
  let apiClientSpy: jasmine.SpyObj<ApiClient>;

  function item(type: 1 | 2 | 3 | 4, referenceId = 'ref1') {
    return {
      type,
      typeTitle: 'مراجعة',
      referenceId,
      title: 'عنصر بانتظار مراجعة',
      detail: '',
      amount: 10,
      branchId: 'b1',
      occurredAtUtc: ''
    };
  }

  beforeEach(async () => {
    apiClientSpy = jasmine.createSpyObj('ApiClient', ['get', 'post']);
    apiClientSpy.get.and.returnValue(of({ items: [], totalCount: 0 }));

    await TestBed.configureTestingModule({
      imports: [ReviewsComponent],
      providers: [{ provide: ApiClient, useValue: apiClientSpy }]
    }).compileComponents();

    fixture = TestBed.createComponent(ReviewsComponent);
    component = fixture.componentInstance;
  });

  it('يُنشأ المكوّن بنجاح', () => {
    expect(component).toBeTruthy();
  });

  it('يحمّل عناصر المراجعة عند ngOnInit', async () => {
    apiClientSpy.get.and.returnValue(of({ items: [item(1)], totalCount: 1 }));

    fixture.detectChanges();
    await fixture.whenStable();

    expect(component.items().length).toBe(1);
  });

  it('يعرض رسالة خطأ عربية واضحة عند فشل التحميل', async () => {
    apiClientSpy.get.and.returnValue(throwError(() => new Error('network')));

    await component.load();

    expect(component.errorMessage()).toBe('تعذّر تحميل قائمة المراجعات.');
  });

  describe('isReturn', () => {
    it('يرجّع true لنوع إرجاع (type=1)', () => {
      expect(component.isReturn(item(1))).toBeTrue();
    });

    it('يرجّع false لأي نوع آخر', () => {
      expect(component.isReturn(item(2))).toBeFalse();
      expect(component.isReturn(item(3))).toBeFalse();
      expect(component.isReturn(item(4))).toBeFalse();
    });
  });

  describe('markReviewed', () => {
    it('يستدعي endpoint الإرجاع الصحيح لنوع 1 ويزيل العنصر من القائمة', async () => {
      apiClientSpy.post.and.returnValue(of({}));
      component.items.set([item(1, 'ref1')]);

      await component.markReviewed(item(1, 'ref1'));

      expect(apiClientSpy.post).toHaveBeenCalledWith(jasmine.anything(), jasmine.anything(), {}, { id: 'ref1' });
      expect(component.items().length).toBe(0);
    });

    it('يستدعي endpoint حركة المخزون الصحيح لنوع 2', async () => {
      apiClientSpy.post.and.returnValue(of({}));

      await component.markReviewed(item(2, 'sm1'));

      expect(apiClientSpy.post).toHaveBeenCalledWith(
        jasmine.anything(),
        jasmine.anything(),
        {},
        { stockMovementId: 'sm1' }
      );
    });

    it('يستدعي endpoint بند فاتورة الشراء الصحيح لنوع 3', async () => {
      apiClientSpy.post.and.returnValue(of({}));

      await component.markReviewed(item(3, 'pii1'));

      expect(apiClientSpy.post).toHaveBeenCalledWith(
        jasmine.anything(),
        jasmine.anything(),
        {},
        { purchaseInvoiceItemId: 'pii1' }
      );
    });

    it('يستدعي endpoint الشكوى الصحيح لنوع 4', async () => {
      apiClientSpy.post.and.returnValue(of({}));

      await component.markReviewed(item(4, 'c1'));

      expect(apiClientSpy.post).toHaveBeenCalledWith(
        jasmine.anything(),
        jasmine.anything(),
        {},
        { complaintId: 'c1' }
      );
    });

    it('يعرض رسالة خطأ عربية تحمل عنوان العنصر عند الفشل، بلا حذفه من القائمة', async () => {
      apiClientSpy.post.and.returnValue(throwError(() => new Error('network')));
      const theItem = item(1, 'ref1');
      component.items.set([theItem]);

      await component.markReviewed(theItem);

      expect(component.errorMessage()).toBe('تعذّر تعليم "عنصر بانتظار مراجعة" كمُراجَعة.');
      expect(component.items().length).toBe(1);
    });
  });
});
