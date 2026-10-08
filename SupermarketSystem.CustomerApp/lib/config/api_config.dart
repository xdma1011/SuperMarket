/// عنوان الباك إند - عدّله حسب بيئتك (محلي/سيرفر فعلي) قبل البناء
/// النهائي. أثناء التطوير على محاكي Android، 10.0.2.2 يشير لـlocalhost
/// الجهاز المضيف؛ على iOS Simulator استخدم localhost مباشرة؛ على جهاز
/// حقيقي لازم IP فعلي بنفس الشبكة أو دومين حقيقي.
///
/// قابل للتغيير وقت التشغيل بلا تعديل الكود، مثلًا على Chrome:
///   flutter run -d chrome --web-port 5300 --dart-define=API_BASE_URL=http://localhost:5000/api/v1
/// (انضاف 25/9 بـ77880eb وانشال بالغلط برفع 202c006 - رجع 9/10/2026.)
class ApiConfig {
  static const String baseUrl = String.fromEnvironment(
    'API_BASE_URL',
    defaultValue: 'http://10.0.2.2:5000/api/v1',
  );
}
