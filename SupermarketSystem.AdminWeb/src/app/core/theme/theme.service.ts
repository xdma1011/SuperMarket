import { Injectable, signal, effect } from '@angular/core';

export type ThemeMode = 'light' | 'dark';

const STORAGE_KEY = 'theme_mode';

/**
 * يبدّل data-theme على <html> — كل قيم الألوان الفعلية تعيش بـstyles.css
 * (متغيّرات لكل وضع)، هذا الصنف بس يقرر أي وضع فعّال حاليًا ويحفظ
 * التفضيل. القيم الجمالية نفسها لسه غير محسومة (بانتظار تصميم Claude
 * Design)، بس آلية التبديل جاهزة تستقبلها بلا أي تعديل هون لاحقًا.
 */
@Injectable({ providedIn: 'root' })
export class ThemeService {
  readonly mode = signal<ThemeMode>(this.readInitialMode());

  constructor() {
    effect(() => {
      const mode = this.mode();
      document.documentElement.setAttribute('data-theme', mode);
      localStorage.setItem(STORAGE_KEY, mode);
    });
  }

  toggle(): void {
    this.mode.set(this.mode() === 'light' ? 'dark' : 'light');
  }

  set(mode: ThemeMode): void {
    this.mode.set(mode);
  }

  private readInitialMode(): ThemeMode {
    const stored = localStorage.getItem(STORAGE_KEY);
    if (stored === 'light' || stored === 'dark') {
      return stored;
    }

    return window.matchMedia('(prefers-color-scheme: dark)').matches ? 'dark' : 'light';
  }
}
