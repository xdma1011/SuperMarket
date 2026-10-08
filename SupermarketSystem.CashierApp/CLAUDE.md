# تطبيق الكاشير (WPF) — سياق الشغل بهالمجلد

> بيتحمّل تلقائيًا لما الشغل بهالمجلد. **القواعد الإلزامية بالـ`CLAUDE.md` الرئيسي (القسم 1)** — الأهم هون: §1.2 لا تحديث حزم، §1.3 فحص شامل (أحداث XAML ↔ الميثودات، منشئات النوافذ)،
> §1.4 Migration جديدة دايمًا. السجل التاريخي بـ`docs/HISTORY.md` (ابحث بالـ`grep`، لا تحمّله كامل).

## التصميم (Offline-first)
- SQLite محلية `%LocalAppData%\SupermarketSystem.CashierApp\local.db` + ملفات JSON جنبها (`held-carts.json` الفواتير المعلّقة، طابور فتح الصندوق، كاش اسم المحل/الدفع).
- البيع بيتحفظ محليًا (`PendingSale`) **قبل** أي إرسال، إرسال فوري، فشل → يضل بالطابور وبتبعته `BackgroundSyncService` (كل `SyncIntervalSeconds`).
- البحث محلي بالكامل. الكتالوج بيتزامن بالنسخة (`Catalog.Version`) وبذرّية (كل الصفحات بالذاكرة ثم معاملة SQLite وحدة). الأسعار/العروض محلية تقديرية؛ **السعر النهائي من السيرفر** (كل سطر بيحمل `catalogVersion`).
- **الساعة الموثوقة (`Services/TrustedClock`):** كل ختم وقت (بيعة `occurredAtUtc`، العروض، فتح الصندوق، تاريخ التقفيل) من `TrustedClock.Instance.Now()` مش `DateTime.Now/UtcNow`.
  بتتزامن مع السيرفر كل دورة مزامنة (`GET /system/time-settings` → `serverUtcNow`) وبتتحفظ بجدول `TrustedTimeAnchors`. الحارس `HighWaterUtc` بيتحدّث بنفس معاملة حفظ البيعة (`Stamp(db)`).
- `ApiClient` بيجدّد التوكن لحاله (401 → تجديد مرة وإعادة الطلب)، رفض التجديد = `SessionEnded` → شاشة الدخول. بيبعت `X-Client-App`/`X-Client-Version` (من `<Version>` بالـcsproj)؛ رد 426 = `UpdateRequired` ("حدّث البرنامج").
- تقفيل الصندوق: `CashClosingWindow` بيبعت الطابور أول (حد 20ث) وبيعدّ المعلّق (`PendingQueueSummary`) ويبعته مع التقفيل (نسمح وننبّه، مش نمنع).

## فخاخ مؤكَّدة
- عنصر بصف Grid ارتفاعه أقل من ~24px بينقص/ما بيبين (انصاب الكاش المعدود بالتقفيل وسطر التلميح). فحص آلي لكل XAML بعد أي تعديل تخطيط.
- **Migrations المحلية** (`Local/Migrations`): بعضها مكتوب يدويًا بلا Designer + لازم تحديث `LocalDbContextModelSnapshot` بالمطابقة. EF 10 بيرمي pending-model-changes بـ`Database.Migrate()` لو ما طابق.
  تحقق: مشروع console مؤقت بيربط `Local/**/*.cs` (Sqlite + Design 10.0.0) وبينادي `Migrate()` على ملف جديد. ما تعدّل Migration موجودة — أضف جديدة. `-1` بنسخة الكتالوج بتجبر سحب كامل.
- بناء من لينكس: `dotnet build -p:EnableWindowsTargeting=true` (يبني بس، ما بيشغّل شاشات).
- أي `using` ناقص (`Microsoft.EntityFrameworkCore`, `System.Windows.Controls`) كسر البناء قبل — الكاشير لازم يبني دايمًا قبل التسليم.
- كل سجل صندوق/مخزون بالسيرفر بوقت **الوصول**؛ الكاشير يبعت وقت البيع بس بحقل `occurredAtUtc`.
- الطباعة (ESC/POS) والعربي ما انجرّبوا على طابعة حقيقية؛ شاشات الكاشير الجديدة ما انجرّبت على ويندوز.

## فحص قبل التسليم
1. `dotnet build -p:EnableWindowsTargeting=true` نظيف. 2. كل حدث بـXAML له ميثود بالـcode-behind. 3. كل `new XxxWindow(...)` يطابق منشئ النافذة بالضبط. 4. سيناريو بكود الكاشير الفعلي على API شغّال إذا لمست `ApiClient`/المزامنة.
