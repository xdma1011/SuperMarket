import { TestBed } from '@angular/core/testing';
import { of, throwError } from 'rxjs';
import { ApiClient } from '../api/api-client.service';
import { BusinessTimeService } from './business-time.service';

describe('BusinessTimeService', () => {
  let service: BusinessTimeService;
  let api: jasmine.SpyObj<ApiClient>;

  beforeEach(() => {
    api = jasmine.createSpyObj('ApiClient', ['get']);
    TestBed.configureTestingModule({ providers: [{ provide: ApiClient, useValue: api }] });
    service = TestBed.inject(BusinessTimeService);
  });

  it('الافتراضي +3 لو السيرفر مش متاح', async () => {
    api.get.and.returnValue(throwError(() => new Error('offline')));
    await service.load();
    expect(service.offsetMinutes()).toBe(180);
    expect(service.timezone()).toBe('+0300');
  });

  it('بياخد الفرق من السيرفر (مثلًا فرق يدوي +2)', async () => {
    api.get.and.returnValue(of({ timeZoneId: 'Asia/Amman', fixedUtcOffsetMinutes: 120, currentOffsetMinutes: 120, isTimeZoneKnown: true, serverUtcNow: '', localNow: '' }));
    await service.load();
    expect(service.timezone()).toBe('+0200');
  });

  it('حدود اليوم المحلي بـUTC بتوقيت المحل مش الجهاز', () => {
    service.offsetMinutes.set(180);
    expect(service.dayStartUtc('2026-09-29')).toBe('2026-09-28T21:00:00.000Z');
    expect(service.dayEndUtc('2026-09-29')).toBe('2026-09-29T20:59:59.999Z');
  });

  it('التاريخ والشهر المحليين: الساعة 22:30 UTC آخر الشهر = أول الشهر الجاي بالأردن', () => {
    service.offsetMinutes.set(180);
    const at = new Date('2026-09-30T22:30:00Z');
    expect(service.localDate(at)).toBe('2026-10-01');
    expect(service.localYearMonth(at)).toEqual({ year: 2026, month: 10 });
    expect(service.formatDateTime(at)).toContain('01:30');
  });

  it('صيغ الفرق للعرض', () => {
    expect(BusinessTimeService.formatOffset(-150)).toBe('-0230');
    expect(BusinessTimeService.offsetLabel(180)).toBe('UTC+3');
    expect(BusinessTimeService.offsetLabel(330)).toBe('UTC+5:30');
  });
});
