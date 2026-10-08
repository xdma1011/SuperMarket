# لوحة الإدارة (Angular) — سياق الشغل بهالمجلد

> بيتحمّل تلقائيًا لما الشغل بهالمجلد. **القواعد الإلزامية بالـ`CLAUDE.md` الرئيسي (القسم 1)** — الأهم: §1.7 كل نص ظاهر **عربي** بلا استثناء، §1.2 لا تحديث حزم، §1.8 منتج جديد = ربط فرع+سعر يدويًا.
> السجل التاريخي بـ`docs/HISTORY.md` (ابحث بالـ`grep`).

## نمط ميزة جديدة (§3.8)
`core/api/operations/<x>.operations.ts` (enum بمسارات الـAPI) ← مكوّن `standalone` بـ`features/<name>/` (ts + html + css + spec) ← مسار بـ`app.routes.ts`
(`canActivate: [requirePermissionGuard('X.Y')]`) ← رابط بـ`shared/models/nav-item.ts` (أيقونة من `NAV_ICONS` بس) ← Signals + `firstValueFrom` + رسائل خطأ عربية واضحة.

## قواعد
- **أي تحميل بيتكرر من الشاشة (صفحة/بحث/فلتر/تبويب/فتح تفاصيل) = `latestRequest()`** (`core/api/latest-request.ts`) مع `isRequestCancelled(err)`؛ **مش** للحفظ (post/put) ولا لأفعال لها أثر.
- الـenums بالردود **أسماء نصية** ("InProgress") مش أرقام — قارن بالأسماء. الأعمدة بنمط `StatusCode`+`StatusTitle` جاهزة بالعربي.
- المبالغ 3 خانات (`number: '1.3-3'`) + "د.أ". التواريخ بتوقيت المحل (`BusinessTimeService`) مش توقيت الجهاز. الصلاحيات بالواجهة fail-open لحد ما تتحمّل (`PermissionsService.loaded()`) — الحماية الحقيقية بالباك إند.
- `@else if (x; as y)` ما بتنفع بقوالب Angular — استعمل `@if (x; as y)` منفصلة. الأيقونات للقائمة من `NAV_ICONS` فقط.
- الجداول على التلفون (≤560px): تنسيق عام بـ`styles.css`؛ تحقق بعرض 390px بلا انزياح.

## أوامر
- بناء: `npx ng build` (لازم نظيف). اختبارات: `CHROME_BIN=<chromium> npx ng test --watch=false --browsers=ChromeHeadlessCI` (الكل لازم ينجح).
- بعد أي ميزة جديدة: spec للمكوّن + تحقق إن الاختبارات الموجودة ما انكسرت.
