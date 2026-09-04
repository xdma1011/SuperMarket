# SupermarketSystem.CustomerApp (Flutter)

تطبيق طلبات الزبائن - يتكلم مباشرة مع نفس باك إند SupermarketSystem.API.

## ⚠️ حالة الكود

هذا الكود **لم يُبنَ ولم يُختبَر فعليًا** - كُتب بدون وصول لبيئة فيها Flutter
SDK. لازم تعمل الخطوات التالية عندك قبل أي تشغيل فعلي:

## خطوات الإعداد الأولى (مرة واحدة)

هذا المجلد يحتوي `pubspec.yaml` و`lib/` فقط - بلا مجلدات المنصّات
(`android/`, `ios/`) لأن `flutter create` لم يُشغَّل هون. الخطوات:

```bash
cd SupermarketSystem.CustomerApp

# يولّد android/ و ios/ وبقية ملفات المنصّات، بلا لمس pubspec.yaml أو lib/ الموجودين
flutter create --org com.supermarket --project-name supermarket_customer_app .

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
