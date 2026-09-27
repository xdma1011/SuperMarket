import { ComponentFixture, TestBed } from '@angular/core/testing';
import { SelectFilterComponent, normalizeArabic } from './select-filter.component';

describe('SelectFilterComponent', () => {
  let fixture: ComponentFixture<SelectFilterComponent>;
  let component: SelectFilterComponent;
  let select: HTMLSelectElement;

  beforeEach(async () => {
    await TestBed.configureTestingModule({ imports: [SelectFilterComponent] }).compileComponents();
    fixture = TestBed.createComponent(SelectFilterComponent);
    component = fixture.componentInstance;
    fixture.detectChanges();

    select = document.createElement('select');
    for (const label of ['كل الفروع', 'فرع الزرقاء', 'فرع إربد', 'فرع عمّان']) {
      const option = document.createElement('option');
      option.text = label;
      option.value = label;
      select.appendChild(option);
    }
    document.body.appendChild(select);
  });

  afterEach(() => {
    component.close(false);
    select.remove();
    fixture.destroy();
  });

  it('normalizeArabic بيوحّد الهمزات والتاء المربوطة والتشكيل', () => {
    expect(normalizeArabic('إربد')).toBe('اربد');
    expect(normalizeArabic('عمّان')).toBe('عمان');
    expect(normalizeArabic('علبة')).toBe('علبه');
  });

  it('بيفتح على الضغط بالماوس ويعرض كل الخيارات', () => {
    select.dispatchEvent(new PointerEvent('pointerdown', { bubbles: true, cancelable: true, pointerType: 'mouse', button: 0 }));
    fixture.detectChanges();

    expect(component.open()).toBeTrue();
    expect(component.filtered().length).toBe(4);
  });

  it('البحث بيفلتر الخيارات بلا حساسية للهمزة', () => {
    component.openFor(select);
    component.onQueryInput('اربد');

    expect(component.filtered().map(o => o.label)).toEqual(['فرع إربد']);
  });

  it('الاختيار بيغيّر قيمة القائمة الأصلية وبيطلق change ويسكّر', () => {
    const changeSpy = jasmine.createSpy('change');
    select.addEventListener('change', changeSpy);

    component.openFor(select);
    component.onQueryInput('الزرقاء');
    component.onSearchKeyDown(new KeyboardEvent('keydown', { key: 'Enter' }));

    expect(select.value).toBe('فرع الزرقاء');
    expect(changeSpy).toHaveBeenCalledTimes(1);
    expect(component.open()).toBeFalse();
  });

  it('ما بيفتح على قائمة معطّلة أو عليها data-no-filter', () => {
    select.disabled = true;
    select.dispatchEvent(new PointerEvent('pointerdown', { bubbles: true, cancelable: true, pointerType: 'mouse', button: 0 }));
    expect(component.open()).toBeFalse();

    select.disabled = false;
    select.setAttribute('data-no-filter', '');
    select.dispatchEvent(new PointerEvent('pointerdown', { bubbles: true, cancelable: true, pointerType: 'mouse', button: 0 }));
    expect(component.open()).toBeFalse();
  });

  it('Escape بيسكّر بلا تغيير القيمة', () => {
    component.openFor(select);
    component.onSearchKeyDown(new KeyboardEvent('keydown', { key: 'ArrowDown' }));
    component.onSearchKeyDown(new KeyboardEvent('keydown', { key: 'Escape' }));

    expect(component.open()).toBeFalse();
    expect(select.selectedIndex).toBe(0);
  });
});
