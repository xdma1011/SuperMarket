/**
 * باركود Code 39 كـSVG (29/9/2026) - لكرت تحقق الشريك بالكاشير. أرقام بس (0-9): كل ماسح باركود بيقرأ Code 39
 * افتراضيًا، والأرقام ما بتتخربط لو لوحة مفاتيح الكاشير عربي (الحروف كانت رح تطلع عربي من الماسح).
 *
 * كل رمز 9 عناصر (5 خطوط و4 فراغات بالتناوب، يبلّش بخط)، 3 منها عريضة: n = ضيق، w = عريض. البداية والنهاية "*".
 */
const PATTERNS: Record<string, string> = {
  '0': 'nnnwwnwnn',
  '1': 'wnnwnnnnw',
  '2': 'nnwwnnnnw',
  '3': 'wnwwnnnnn',
  '4': 'nnnwwnnnw',
  '5': 'wnnwwnnnn',
  '6': 'nnwwwnnnn',
  '7': 'nnnwnnwnw',
  '8': 'wnnwnnwnn',
  '9': 'nnwwnnwnn',
  '*': 'nwnnwnwnn'
};

const NARROW = 1;
const WIDE = 3;

/** عناصر الباركود (عرض كل خط/فراغ بوحدات الضيّق) - مفصولة عن الرسم عشان تنفحص بالاختبار. */
export function code39Modules(digits: string): { bar: boolean; width: number }[] {
  if (!/^[0-9]+$/.test(digits)) {
    throw new Error('Code 39 هون للأرقام بس.');
  }

  const modules: { bar: boolean; width: number }[] = [];
  const symbols = `*${digits}*`;
  for (let s = 0; s < symbols.length; s++) {
    const pattern = PATTERNS[symbols[s]];
    for (let i = 0; i < pattern.length; i++) {
      modules.push({ bar: i % 2 === 0, width: pattern[i] === 'w' ? WIDE : NARROW });
    }
    if (s < symbols.length - 1) {
      modules.push({ bar: false, width: NARROW }); // فراغ بين الرموز
    }
  }
  return modules;
}

/** SVG كامل (خلفية بيضا + هامش هادئ 10 وحدات بكل جهة) - "unit" = عرض الخط الضيّق بالبكسل. */
export function code39Svg(digits: string, unit = 2, height = 80): string {
  const modules = code39Modules(digits);
  const quiet = 10 * unit;
  let x = quiet;
  const rects: string[] = [];
  for (const m of modules) {
    const w = m.width * unit;
    if (m.bar) {
      rects.push(`<rect x="${x}" y="0" width="${w}" height="${height}"/>`);
    }
    x += w;
  }
  const total = x + quiet;
  return `<svg xmlns="http://www.w3.org/2000/svg" width="${total}" height="${height}" viewBox="0 0 ${total} ${height}">` +
    `<rect width="${total}" height="${height}" fill="#fff"/><g fill="#000">${rects.join('')}</g></svg>`;
}
