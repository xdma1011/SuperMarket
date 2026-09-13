# SupermarketSystem.CustomerApp (Flutter)

تطبيق طلبات الزبائن - يتكلم مباشرة مع نفس باك إند SupermarketSystem.API.

## ✅ حالة الكود (تحدَّث 13/9/2026)

الكود **بُني فعليًا وتحقّقنا منه** بعد ما صار متاح Flutter SDK (3.47.4
stable). تم:
- `flutter create --platforms=web,linux .` لتوليد مجلدات المنصّات
  (`web/`, `linux/`) - بلا لمس `pubspec.yaml` أو `lib/` الموجودين، ومُلتزَمة
  الآن بالمستودع.
- `flutter analyze` → **صفر أخطاء وتحذيرات**.
- `flutter build web --release` → **نجح فعليًا** (أول تجميع (compile) حقيقي
  لهالكود من الأساس - كل الـscreens/providers/services تترجم بلا أي خطأ).
- `flutter build linux --release` تعذّر فقط لأن حاوية الاختبار ما فيها
  `libgtk-3-dev` مُثبَّتة (مش مشكلة كود) - مش هدف أساسي لهالتطبيق أصلًا
  (تطبيق زبائن جوّال، لا سطح مكتب).
- `android/` و`ios/` ما تولّدوا بعد (يحتاجوا Android SDK/Xcode غير
  متوفرين بهالبيئة) - `flutter create --platforms=android,ios .` كافي
  لتوليدهم لاحقًا بلا لمس أي كود موجود.

**لسه بلا Android SDK حقيقي ولا جهاز/محاكي فعلي** - يعني ما تم تشغيله
فعليًا (`flutter run`) على أي جهاز أو محاكي، بس التجميع الحقيقي (build)
يثبت صحة الكود نحويًا ونوعيًا (type-safe) بشكل شبه كامل.

## خطوات الإعداد عندك (لتشغيل فعلي على جهاز/محاكي)

```bash
cd SupermarketSystem.CustomerApp

# لو بدك تبني لأندرويد/آيفون فعليًا - يولّد android/ و ios/ فقط، بلا تكرار web/linux/ الموجودين
flutter create --platforms=android,ios .

flutter pub get
flutter analyze   # تحقق أولي من الأخطاء قبل أي تشغيل
```

## قبل التشغيل

1. عدّل `lib/config/api_config.dart` وحط عنوان الباك إند الصحيح (محلي أو سيرفر فعلي).
2. تأكد الباك إند شغّال ويقبل اتصالات من جهاز/محاكي التطوير (CORS، الشبكة).
3. `flutter run`.

## ما هو مبني فعليًا (بالكود، بانتظار تجربة حقيقية)

- تسجيل دخول برقم الهاتف + كود تحقق عبر تلغرام (Deep Link لفتح البوت لو الرقم غير مربوط)
- اختيار فرع، تصفّح كتالوج (بحث + تصنيفات + pagination)
- سلة تسوّق محلية، خريطة مجانية (OpenStreetMap) لاختيار موقع التسليم
- تقديم طلب، متابعة حالته، تقييمه بعد التسليم
- عرض QR ثابت لهوية الزبون (يُمسح بالكاشير)
- رصيد نقاط الولاء، تقديم شكوى

## ما هو ناقص عمدًا (خارج نطاق هذه الدفعة)

- **تسجيل توكن FCM فعلي للإشعارات**: `CustomerService.registerDeviceToken()`
  موجود وجاهز بالكود، بس ما تم ربطه بـ`firebase_messaging` package فعليًا
  (يحتاج ملفات إعداد Firebase حقيقية - `google-services.json`/`GoogleService-Info.plist`
  - من Firebase Console، ما بقدر أولّدها بدون مشروع Firebase حقيقي).
- أيقونة التطبيق، splash screen مخصَّصة، توطين (localization) رسمي - حاليًا نص عربي مباشر بالكود بلا `intl` ARB files.
