import { code39Modules, code39Svg } from './code39';

describe('code39', () => {
  it('كل رمز فيه 3 عناصر عريضة بالضبط (قاعدة Code 39) - فراغ عريض واحد وخطين عريضين', () => {
    const modules = code39Modules('0123456789');
    // 12 رمز (مع * بالطرفين) × 9 عناصر + 11 فراغ فاصل
    expect(modules.length).toBe(12 * 9 + 11);
    for (let s = 0; s < 12; s++) {
      const symbol = modules.slice(s * 10, s * 10 + 9);
      expect(symbol.filter(m => m.width === 3).length).toBe(3);
      expect(symbol.filter(m => m.width === 3 && !m.bar).length).toBe(1);
      expect(symbol[0].bar).toBeTrue();
    }
  });

  it('الأرقام بتتبع ترميز 2 من 5 (أوزان 1، 2، 4، 7، 0) - تحقق مستقل من جدول الأنماط', () => {
    const weights = [1, 2, 4, 7, 0];
    const modules = code39Modules('0123456789');
    for (let d = 0; d <= 9; d++) {
      const symbol = modules.slice((d + 1) * 10, (d + 1) * 10 + 9);
      const bars = symbol.filter(m => m.bar);
      const sum = bars.reduce((acc, m, i) => acc + (m.width === 3 ? weights[i] : 0), 0);
      expect(sum === 11 ? 0 : sum).withContext(`الرقم ${d}`).toBe(d);
    }
  });

  it('بيرفض غير الأرقام، والـSVG فيه خلفية بيضا وهامش', () => {
    expect(() => code39Modules('12A')).toThrow();
    const svg = code39Svg('12345', 2, 60);
    expect(svg.startsWith('<svg')).toBeTrue();
    expect(svg).toContain('fill="#fff"');
    expect(svg).toContain('height="60"');
  });
});
