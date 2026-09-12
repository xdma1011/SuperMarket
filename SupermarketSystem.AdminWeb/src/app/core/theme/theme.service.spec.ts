import { TestBed } from '@angular/core/testing';
import { ThemeService } from './theme.service';

describe('ThemeService', () => {
  beforeEach(() => {
    localStorage.clear();
    document.documentElement.removeAttribute('data-theme');
  });

  afterEach(() => {
    localStorage.clear();
    document.documentElement.removeAttribute('data-theme');
  });

  function createService(): ThemeService {
    TestBed.configureTestingModule({ providers: [ThemeService] });
    const service = TestBed.inject(ThemeService);
    TestBed.flushEffects();
    return service;
  }

  it('يُنشأ بنجاح', () => {
    const service = createService();
    expect(service).toBeTruthy();
  });

  it('يقرأ الوضع المحفوظ بـlocalStorage لو موجود', () => {
    localStorage.setItem('theme_mode', 'dark');

    const service = createService();

    expect(service.mode()).toBe('dark');
  });

  it('يضبط data-theme على <html> فورًا عند الإنشاء', () => {
    localStorage.setItem('theme_mode', 'light');

    createService();

    expect(document.documentElement.getAttribute('data-theme')).toBe('light');
  });

  describe('toggle', () => {
    it('يبدّل من light إلى dark', () => {
      const service = createService();
      service.set('light');
      TestBed.flushEffects();

      service.toggle();
      TestBed.flushEffects();

      expect(service.mode()).toBe('dark');
      expect(localStorage.getItem('theme_mode')).toBe('dark');
    });

    it('يبدّل من dark إلى light', () => {
      const service = createService();
      service.set('dark');
      TestBed.flushEffects();

      service.toggle();
      TestBed.flushEffects();

      expect(service.mode()).toBe('light');
      expect(localStorage.getItem('theme_mode')).toBe('light');
    });
  });

  describe('set', () => {
    it('يضبط الوضع المطلوب مباشرة ويحفظه بـlocalStorage', () => {
      const service = createService();

      service.set('dark');
      TestBed.flushEffects();

      expect(service.mode()).toBe('dark');
      expect(localStorage.getItem('theme_mode')).toBe('dark');
      expect(document.documentElement.getAttribute('data-theme')).toBe('dark');
    });
  });
});
