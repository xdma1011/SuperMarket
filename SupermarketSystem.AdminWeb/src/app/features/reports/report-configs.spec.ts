import { REPORT_CONFIGS } from './report-configs';

describe('REPORT_CONFIGS', () => {
  it('يحتوي على 12 تقريرًا عاديًا (بلا الثلاثة الخاصة: ملخّص المبيعات/رأس المال/ديون الموردين)', () => {
    expect(REPORT_CONFIGS.length).toBe(12);
  });

  it('كل معرّف تقرير فريد (بلا تكرار)', () => {
    const ids = REPORT_CONFIGS.map(r => r.id);
    expect(new Set(ids).size).toBe(ids.length);
  });

  it('كل تقرير عنده عمود واحد على الأقل', () => {
    for (const report of REPORT_CONFIGS) {
      expect(report.columns.length).toBeGreaterThan(0);
    }
  });

  it('كل عمود من نوع enum يحمل enumMap فعليًا', () => {
    for (const report of REPORT_CONFIGS) {
      for (const column of report.columns) {
        if (column.type === 'enum') {
          expect(column.enumMap).withContext(`${report.id}.${column.key}`).toBeDefined();
        }
      }
    }
  });

  it('كل تقرير عنده title وoperation غير فاضيين', () => {
    for (const report of REPORT_CONFIGS) {
      expect(report.title.length).toBeGreaterThan(0);
      expect(String(report.operation).length).toBeGreaterThan(0);
    }
  });
});
