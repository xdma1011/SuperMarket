import { Subject, of, throwError } from 'rxjs';
import { LatestRequest, isRequestCancelled } from './latest-request';

describe('LatestRequest (آخر طلب بس)', () => {
  const flush = () => new Promise<void>(resolve => setTimeout(resolve));

  it('صفحة 1 ثم 2 ثم 3 بسرعة: الأول والتاني بينقطعوا، والرد المتأخر تبعهم ما بيوصل أبدًا', async () => {
    const request = new LatestRequest();
    const page1 = new Subject<string>();
    const page2 = new Subject<string>();
    const page3 = new Subject<string>();
    const shown: string[] = [];
    const cancelled: number[] = [];

    const load = (n: number, source: Subject<string>) =>
      request.run(source).then(v => shown.push(v), err => { if (isRequestCancelled(err)) cancelled.push(n); });

    const p1 = load(1, page1);
    const p2 = load(2, page2);
    const p3 = load(3, page3);

    // الطلبات القديمة انقطعت فعليًا (ما حدا مشترك فيها)
    expect(page1.observed).toBeFalse();
    expect(page2.observed).toBeFalse();
    expect(page3.observed).toBeTrue();

    page1.next('بيانات صفحة 1 (متأخرة)');
    page3.next('بيانات صفحة 3');
    await Promise.all([p1, p2, p3]);

    expect(shown).toEqual(['بيانات صفحة 3']);
    expect(cancelled).toEqual([1, 2]);
  });

  it('الملغى ما بيكمل قبل ما الأحدث يخلص (loading ما بينطفى بالنص)', async () => {
    const request = new LatestRequest();
    const older = new Subject<number>();
    const newer = new Subject<number>();
    const events: string[] = [];

    const first = request.run(older).catch(() => events.push('القديم كمّل'));
    const second = request.run(newer).then(() => events.push('الجديد كمّل'));

    await flush();
    expect(events).toEqual([]);

    newer.next(1);
    await Promise.all([first, second]);
    expect(events).toEqual(['الجديد كمّل', 'القديم كمّل']);
  });

  it('مصدر متزامن، وخطأ الطلب نفسه بيوصل زي ما هو', async () => {
    const request = new LatestRequest();
    expect(await request.run(of(5))).toBe(5);

    const failure = new Error('500');
    await expectAsync(request.run(throwError(() => failure))).toBeRejectedWith(failure);
  });

  it('cancel (المكوّن انهدم): بيقطع وبيكمّل المعلّق بخطأ إلغاء', async () => {
    const request = new LatestRequest();
    const source = new Subject<number>();
    const pending = request.run(source);

    request.cancel();

    expect(source.observed).toBeFalse();
    await expectAsync(pending).toBeRejected();
    expect(await pending.catch(isRequestCancelled)).toBeTrue();
  });
});
