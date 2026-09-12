import { ComponentFixture, TestBed } from '@angular/core/testing';
import { PlaceholderComponent } from './placeholder.component';

describe('PlaceholderComponent', () => {
  let fixture: ComponentFixture<PlaceholderComponent>;
  let component: PlaceholderComponent;

  beforeEach(async () => {
    await TestBed.configureTestingModule({
      imports: [PlaceholderComponent]
    }).compileComponents();

    fixture = TestBed.createComponent(PlaceholderComponent);
    component = fixture.componentInstance;
  });

  it('يُنشأ المكوّن بنجاح', () => {
    expect(component).toBeTruthy();
  });

  it('العنوان الافتراضي فاضي قبل أي إدخال', () => {
    expect(component.title).toBe('');
  });

  it('يعرض العنوان المُمرَّر عبر Input فعليًا بالـDOM', () => {
    component.title = 'الفروع';
    fixture.detectChanges();

    const compiled = fixture.nativeElement as HTMLElement;
    expect(compiled.querySelector('h1')?.textContent).toContain('الفروع');
  });
});
