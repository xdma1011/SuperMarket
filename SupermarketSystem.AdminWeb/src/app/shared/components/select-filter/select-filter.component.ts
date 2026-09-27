import { Component, ElementRef, HostListener, OnDestroy, OnInit, ViewChild, computed, inject, signal } from '@angular/core';
import { DOCUMENT } from '@angular/common';

interface FilterOption {
  index: number;
  label: string;
  normalized: string;
  disabled: boolean;
}

/** توحيد النص العربي للبحث: بلا تشكيل، الألف/الهاء/الياء بصيغة وحدة. */
export function normalizeArabic(text: string): string {
  return text
    .toLowerCase()
    .replace(/[ً-ْـ]/g, '')
    .replace(/[أإآ]/g, 'ا')
    .replace(/ة/g, 'ه')
    .replace(/ى/g, 'ي')
    .trim();
}

/**
 * بحث (فلتر) لكل قائمة منسدلة بالنظام، بلا تعديل أي صفحة: بيلتقط فتح أي
 * <select> عادي بالماوس أو الكيبورد (Enter/Space/Alt+↓/F4) ويعرض بدل
 * القائمة الأصلية لوحة فيها خانة بحث + الخيارات. الـ<select> الأصلي بيضل
 * مصدر القيمة الوحيد: الاختيار بيغيّر selectedIndex وبيطلق حدث change،
 * فـngModel و(change) واختبارات الصفحات كلها شغّالة زي ما هي.
 *
 * مستثنى: multiple، size>1، معطّل، أو عليه data-no-filter. اللمس (موبايل)
 * بيضل بالقائمة الأصلية للجهاز - منع قائمة الجهاز الأصلية على اللمس مش
 * موثوق بكل المتصفحات.
 */
@Component({
  selector: 'app-select-filter',
  standalone: true,
  templateUrl: './select-filter.component.html',
  styleUrl: './select-filter.component.css'
})
export class SelectFilterComponent implements OnInit, OnDestroy {
  private readonly document = inject(DOCUMENT);

  @ViewChild('panel') panelRef?: ElementRef<HTMLElement>;
  @ViewChild('searchInput') searchInputRef?: ElementRef<HTMLInputElement>;
  @ViewChild('list') listRef?: ElementRef<HTMLElement>;

  readonly open = signal(false);
  readonly query = signal('');
  readonly options = signal<FilterOption[]>([]);
  readonly selectedIndex = signal(-1);
  readonly activeIndex = signal(-1);
  readonly position = signal({ top: 0, left: 0, width: 0, maxHeight: 320 });

  readonly filtered = computed(() => {
    const q = normalizeArabic(this.query());
    const all = this.options();
    return q ? all.filter(o => o.normalized.includes(q)) : all;
  });

  private target: HTMLSelectElement | null = null;

  private readonly onPointerDown = (event: PointerEvent) => {
    const select = this.eligibleSelect(event.target);
    if (select && event.pointerType !== 'touch' && event.button === 0) {
      event.preventDefault();
      select.focus();
      if (this.open() && this.target === select) {
        this.close(true);
      } else {
        this.openFor(select);
      }
      return;
    }

    if (this.open() && !this.panelRef?.nativeElement.contains(event.target as Node)) {
      this.close(false);
    }
  };

  // بعض المتصفحات بتفتح القائمة الأصلية على mousedown حتى لو pointerdown انمنع.
  private readonly onMouseDown = (event: MouseEvent) => {
    if (this.eligibleSelect(event.target) && !this.isTouchMouse) {
      event.preventDefault();
    }
  };

  private isTouchMouse = false;
  private readonly onTouchStart = () => { this.isTouchMouse = true; };
  private readonly onTouchEnd = () => { setTimeout(() => (this.isTouchMouse = false), 800); };

  private readonly onKeyDown = (event: KeyboardEvent) => {
    const select = this.eligibleSelect(event.target);
    if (!select || this.open()) {
      return;
    }
    const opensList =
      event.key === 'Enter' || event.key === ' ' || event.key === 'F4' ||
      (event.altKey && (event.key === 'ArrowDown' || event.key === 'ArrowUp'));
    if (opensList) {
      event.preventDefault();
      this.openFor(select);
    }
  };

  private readonly onScroll = (event: Event) => {
    if (this.open() && !this.panelRef?.nativeElement.contains(event.target as Node)) {
      this.close(false);
    }
  };

  ngOnInit(): void {
    this.document.addEventListener('pointerdown', this.onPointerDown, true);
    this.document.addEventListener('mousedown', this.onMouseDown, true);
    this.document.addEventListener('touchstart', this.onTouchStart, { capture: true, passive: true });
    this.document.addEventListener('touchend', this.onTouchEnd, { capture: true, passive: true });
    this.document.addEventListener('keydown', this.onKeyDown, true);
    this.document.addEventListener('scroll', this.onScroll, true);
  }

  ngOnDestroy(): void {
    this.document.removeEventListener('pointerdown', this.onPointerDown, true);
    this.document.removeEventListener('mousedown', this.onMouseDown, true);
    this.document.removeEventListener('touchstart', this.onTouchStart, true);
    this.document.removeEventListener('touchend', this.onTouchEnd, true);
    this.document.removeEventListener('keydown', this.onKeyDown, true);
    this.document.removeEventListener('scroll', this.onScroll, true);
  }

  @HostListener('window:resize')
  onResize(): void {
    if (this.open()) {
      this.close(false);
    }
  }

  openFor(select: HTMLSelectElement): void {
    this.target = select;
    this.options.set(
      Array.from(select.options).map(o => ({
        index: o.index,
        label: o.text,
        normalized: normalizeArabic(o.text),
        disabled: o.disabled
      }))
    );
    this.selectedIndex.set(select.selectedIndex);
    this.query.set('');
    this.activeIndex.set(Math.max(0, this.filtered().findIndex(o => o.index === select.selectedIndex)));

    const rect = select.getBoundingClientRect();
    const viewportHeight = this.document.defaultView?.innerHeight ?? 800;
    const viewportWidth = this.document.defaultView?.innerWidth ?? 1200;
    const width = Math.min(Math.max(rect.width, 220), viewportWidth - 16);
    const spaceBelow = viewportHeight - rect.bottom - 8;
    const spaceAbove = rect.top - 8;
    const openUp = spaceBelow < 220 && spaceAbove > spaceBelow;
    const maxHeight = Math.min(360, Math.max(160, openUp ? spaceAbove : spaceBelow));
    // RTL: محاذاة الحافة اليمين للوحة مع يمين القائمة.
    const left = Math.min(Math.max(8, rect.right - width), viewportWidth - width - 8);
    const top = openUp ? rect.top - maxHeight - 4 : rect.bottom + 4;

    this.position.set({ top, left, width, maxHeight });
    this.open.set(true);

    setTimeout(() => {
      this.searchInputRef?.nativeElement.focus();
      this.scrollActiveIntoView();
    });
  }

  close(returnFocus: boolean): void {
    this.open.set(false);
    if (returnFocus) {
      this.target?.focus();
    }
    this.target = null;
  }

  onQueryInput(value: string): void {
    this.query.set(value);
    const firstEnabled = this.filtered().findIndex(o => !o.disabled);
    this.activeIndex.set(firstEnabled);
  }

  onSearchKeyDown(event: KeyboardEvent): void {
    const list = this.filtered();
    switch (event.key) {
      case 'ArrowDown':
        event.preventDefault();
        this.moveActive(1);
        break;
      case 'ArrowUp':
        event.preventDefault();
        this.moveActive(-1);
        break;
      case 'Enter': {
        event.preventDefault();
        const option = list[this.activeIndex()];
        if (option) {
          this.choose(option);
        }
        break;
      }
      case 'Escape':
        event.preventDefault();
        event.stopPropagation();
        this.close(true);
        break;
      case 'Tab':
        this.close(true);
        break;
    }
  }

  choose(option: FilterOption): void {
    if (option.disabled || !this.target) {
      return;
    }
    const select = this.target;
    if (select.selectedIndex !== option.index) {
      select.selectedIndex = option.index;
      select.dispatchEvent(new Event('input', { bubbles: true }));
      select.dispatchEvent(new Event('change', { bubbles: true }));
    }
    this.close(true);
  }

  private moveActive(step: number): void {
    const list = this.filtered();
    if (list.length === 0) {
      return;
    }
    let next = this.activeIndex();
    for (let i = 0; i < list.length; i++) {
      next = (next + step + list.length) % list.length;
      if (!list[next].disabled) {
        break;
      }
    }
    this.activeIndex.set(next);
    this.scrollActiveIntoView();
  }

  private scrollActiveIntoView(): void {
    setTimeout(() => {
      const active = this.listRef?.nativeElement.querySelector('.option.active');
      active?.scrollIntoView({ block: 'nearest' });
    });
  }

  private eligibleSelect(target: EventTarget | null): HTMLSelectElement | null {
    if (!(target instanceof HTMLSelectElement)) {
      return null;
    }
    if (target.multiple || target.size > 1 || target.disabled || target.hasAttribute('data-no-filter')) {
      return null;
    }
    return target;
  }
}
