import { DestroyRef, inject } from '@angular/core';
import { EmptyError, Observable, Subscription } from 'rxjs';

/** الطلب انلغى لأنه إجا طلب أحدث مكانه (أو المكوّن انهدم) - مش خطأ، ما بينعرض للمستخدم. */
export class RequestCancelledError extends Error {
  constructor() {
    super('الطلب انلغى لأنه إجا طلب أحدث.');
    this.name = 'RequestCancelledError';
  }
}

export function isRequestCancelled(err: unknown): boolean {
  return err instanceof RequestCancelledError;
}

interface PendingEntry {
  subscription?: Subscription;
  reject: (reason: unknown) => void;
}

/**
 * "آخر طلب بس" (1/10/2026، صاحب المشروع: "next next next ورى بعض بتتنفذ الثلاث requests... افرض الأول تأخر،
 * بتظهر بيانات الأولى وأنا بالصفحة 3 - ليش ما تكنسل الطلب اللي قبل؟").
 *
 * بديل firstValueFrom لأي تحميل بيتكرر من الشاشة (صفحة، بحث، فلتر، فرع، تبويب، شهر):
 * - كل run جديد **بيقطع اللي قبله فعليًا** (unsubscribe = المتصفح بيلغي الطلب، بيبين "canceled" بأدوات المطوّر)،
 *   فمستحيل رد قديم يكتب فوق الصفحة الحالية مهما تأخر.
 * - الـPromise تبع الملغى بيترفض بـRequestCancelledError **بعد ما الأحدث يخلص** (مش فورًا): هيك الـfinally تبعه
 *   (loading=false) ما بيطفّي التحميل والطلب الجديد لسه شغّال، وأي كود عامل `await this.load()` (بعد حفظ مثلًا)
 *   بيكمل عادي بعد ما البيانات الجديدة تنزل - ما في إشي بيعلق.
 * - لازم الـcatch يتجاهله: `catch (err) { if (isRequestCancelled(err)) return; ... }`.
 *
 * الاستعمال: حقل بالمكوّن لكل نوع تحميل مستقل (القائمة لحال، الملخّص لحال):
 *   private readonly listRequest = latestRequest();
 *   const result = await this.listRequest.run(this.apiClient.get<...>(...));
 *
 * **للقراءة بس** - الحفظ (post/put) ما بينلغى بهالطريقة: الطلب ممكن يكون وصل للسيرفر وانحفظ.
 */
export class LatestRequest {
  private current: PendingEntry | null = null;
  private superseded: Array<(reason: unknown) => void> = [];

  run<T>(source: Observable<T>): Promise<T> {
    this.supersedeCurrent();

    return new Promise<T>((resolve, reject) => {
      const entry: PendingEntry = { reject };
      let settled = false;
      const settle = (finish: () => void) => {
        if (settled) return;
        settled = true;
        const wasLatest = this.current === entry;
        if (wasLatest) this.current = null;
        finish();
        // الأحدث خلص: هلق بس الملغيين بيكملوا (بعد ما كود الأحدث ياخد دوره)
        if (wasLatest) this.flushSuperseded();
      };

      this.current = entry;
      entry.subscription = source.subscribe({
        next: value => settle(() => resolve(value)),
        error: err => settle(() => reject(err)),
        // نفس سلوك firstValueFrom: اكتمل بلا قيمة = خطأ
        complete: () => settle(() => reject(new EmptyError()))
      });
      if (settled) {
        entry.subscription.unsubscribe();
      }
    });
  }

  /** بيقطع الطلب المعلّق (لو في) وبيكمّل كل الملغيين فورًا - لما المكوّن ينهدم. */
  cancel(): void {
    this.supersedeCurrent();
    this.flushSuperseded();
  }

  private supersedeCurrent(): void {
    if (!this.current) return;
    this.current.subscription?.unsubscribe();
    this.superseded.push(this.current.reject);
    this.current = null;
  }

  private flushSuperseded(): void {
    const pending = this.superseded;
    this.superseded = [];
    for (const reject of pending) {
      reject(new RequestCancelledError());
    }
  }
}

/** LatestRequest بيقطع طلبه المعلّق لحاله لما المكوّن ينهدم - لازم ينادى بسياق حقن (تعريف حقل أو constructor). */
export function latestRequest(): LatestRequest {
  const request = new LatestRequest();
  inject(DestroyRef).onDestroy(() => request.cancel());
  return request;
}
