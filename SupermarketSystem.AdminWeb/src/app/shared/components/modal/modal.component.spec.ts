import { ComponentFixture, TestBed } from '@angular/core/testing';
import { ModalComponent } from './modal.component';

describe('ModalComponent', () => {
  let fixture: ComponentFixture<ModalComponent>;
  let component: ModalComponent;

  beforeEach(async () => {
    await TestBed.configureTestingModule({
      imports: [ModalComponent]
    }).compileComponents();

    fixture = TestBed.createComponent(ModalComponent);
    component = fixture.componentInstance;
  });

  it('يُنشأ المكوّن بنجاح وبقيم افتراضية معقولة', () => {
    expect(component).toBeTruthy();
    expect(component.open).toBeFalse();
    expect(component.fields).toEqual([]);
    expect(component.saveLabel).toBe('حفظ');
  });

  describe('close', () => {
    it('يبعث حدث closed', () => {
      let emitted = false;
      component.closed.subscribe(() => (emitted = true));

      component.close();

      expect(emitted).toBeTrue();
    });
  });

  describe('save', () => {
    it('يبعث حدث saved', () => {
      let emitted = false;
      component.saved.subscribe(() => (emitted = true));

      component.save();

      expect(emitted).toBeTrue();
    });
  });

  describe('stopPropagation', () => {
    it('يمنع انتشار حدث النقرة (لا يغلق الـmodal عند نقرة داخلية)', () => {
      const event = jasmine.createSpyObj<MouseEvent>('MouseEvent', ['stopPropagation']);

      component.stopPropagation(event);

      expect(event.stopPropagation).toHaveBeenCalled();
    });
  });
});
