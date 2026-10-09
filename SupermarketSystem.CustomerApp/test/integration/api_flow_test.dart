// تكامل حقيقي: خدمات التطبيق نفسها (ApiClient/BranchService/CatalogService/AuthService/OrderService/...) ضد API شغّال
// على قاعدة اختبار. ما بتشتغل إلا لما ينبعت --dart-define=RUN_API_TESTS=true (و API_BASE_URL للعنوان)، مثلًا:
//   flutter test test/integration --dart-define=RUN_API_TESTS=true --dart-define=API_BASE_URL=http://localhost:5000/api/v1
// التخزين الآمن (Keychain/Keystore) ما إله plugin بالاختبار، فبنستعمل الـmock الرسمي تبع flutter_secure_storage.
import 'dart:math';

import 'package:flutter_secure_storage/flutter_secure_storage.dart';
import 'package:flutter_test/flutter_test.dart';
import 'package:supermarket_customer_app/models/cart_item.dart';
import 'package:supermarket_customer_app/services/api_client.dart';
import 'package:supermarket_customer_app/services/auth_service.dart';
import 'package:supermarket_customer_app/services/branch_service.dart';
import 'package:supermarket_customer_app/services/catalog_service.dart';
import 'package:supermarket_customer_app/services/coupon_service.dart';
import 'package:supermarket_customer_app/services/order_service.dart';

const runApiTests = bool.fromEnvironment('RUN_API_TESTS');

void main() {
  // بلا TestWidgetsFlutterBinding: بيحوّل كل طلب HTTP لـ400 وهمي.
  setUp(() => FlutterSecureStorage.setMockInitialValues({}));

  final phone = '0796${Random().nextInt(899999) + 100000}';

  test('دورة الزبون: فروع ← تصنيفات ← منتجات ← طلب كود ← طلب توصيل ← تفاصيله وقائمته', () async {
    final branches = await BranchService.instance.getBranches();
    expect(branches, isNotEmpty, reason: 'ما في فروع');
    final branch = branches.first;

    final categories = await CatalogService.instance.getCategories(branch.id);
    expect(categories, isNotEmpty);

    final page = await CatalogService.instance.getProducts(branchId: branch.id, pageSize: 50);
    expect(page.items, isNotEmpty, reason: 'الكتالوج فاضي للفرع (§1.8؟)');
    for (final p in page.items) {
      expect(p.baseUnitId, isNotEmpty, reason: 'منتج "${p.name}" بلا baseUnitId - الطلب رح ينرفض');
      expect(p.price, greaterThan(0), reason: 'منتج "${p.name}" سعره صفر');
    }

    final otp = await AuthService.instance.requestOtp(phone);
    expect(otp.telegramLinked, isFalse);
    // telegramDeepLink = null لما بوت تلغرام مش مضبوط بإعدادات القاعدة (شاشة الهاتف صارت تعرض رسالة لهالحالة).

    final picked = page.items.take(2).toList();
    final cart = [CartItem(product: picked[0], quantity: 2), if (picked.length > 1) CartItem(product: picked[1], quantity: 1)];
    final expectedTotal = cart.fold<double>(0, (s, c) => s + c.estimatedLineTotal);

    final orderId = await OrderService.instance.placeOrder(
      customerPhone: phone, customerName: 'زبون فلاتر', branchId: branch.id, deliveryNote: 'اختبار تكامل', items: cart);
    expect(orderId, isNotEmpty);

    final detail = await OrderService.instance.getOrderDetail(orderId);
    expect(detail.items.length, cart.length);
    final detailTotal = detail.items.fold<double>(0, (s, i) => s + i.quantity * i.estimatedUnitPrice);
    expect(detailTotal, closeTo(expectedTotal, 0.0005), reason: 'سعر الطلب ≠ سعر الكتالوج اللي شافه الزبون');

    final raw = await ApiClient.instance.get('/orders/$orderId/customer-view');
    final customerId = raw['customerId'] as String;
    final orders = await OrderService.instance.getCustomerOrders(customerId);
    final mine = orders.items.where((o) => o.id == orderId).toList();
    expect(mine, hasLength(1));
    expect(mine.single.estimatedTotal, closeTo(expectedTotal, 0.0005));
    expect(mine.single.itemsCount, cart.length);

    // كوبون غلط: رسالة عربية واضحة من السيرفر، مش استثناء خام.
    try {
      await CouponService.instance.preview(customerId: customerId, code: 'NO-SUCH-${Random().nextInt(9999)}', estimatedTotal: expectedTotal);
      fail('كوبون مش موجود انقبل');
    } on ApiException catch (e) {
      expect(e.statusCode, inInclusiveRange(400, 499));
      expect(RegExp(r'[؀-ۿ]').hasMatch(e.message), isTrue, reason: 'الرسالة مش عربية: ${e.message}');
    }
  }, skip: runApiTests ? false : 'بدّه API شغّال: --dart-define=RUN_API_TESTS=true');
}
