import 'package:flutter_test/flutter_test.dart';
import 'package:supermarket_customer_app/models/product.dart';
import 'package:supermarket_customer_app/providers/cart_provider.dart';

Product _product(String id, double price) => Product(
      id: id,
      name: 'منتج $id',
      categoryName: 'تصنيف',
      price: price,
      baseUnitId: 'u-$id',
      baseUnitName: 'حبة',
    );

void main() {
  group('CartProvider', () {
    test('إضافة نفس الصنف بتزيد الكمية بدل سطر جديد', () {
      final cart = CartProvider();
      cart.add(_product('a', 1.25));
      cart.add(_product('a', 1.25), quantity: 2);
      expect(cart.itemCount, 1);
      expect(cart.items.single.quantity, 3);
      expect(cart.estimatedTotal, closeTo(3.75, 0.0001));
    });

    test('المجموع بالفلس (3 خانات) بدون تقريب لخانتين', () {
      final cart = CartProvider();
      cart.add(_product('a', 0.125), quantity: 3);
      cart.add(_product('b', 0.350));
      expect(cart.estimatedTotal, closeTo(0.725, 0.0001));
    });

    test('تعديل الكمية لصفر أو أقل بيشيل الصنف', () {
      final cart = CartProvider();
      cart.add(_product('a', 1));
      cart.updateQuantity('a', 0);
      expect(cart.isEmpty, isTrue);
    });

    test('مسح السلة', () {
      final cart = CartProvider();
      cart.add(_product('a', 1));
      cart.add(_product('b', 2));
      cart.clear();
      expect(cart.itemCount, 0);
      expect(cart.estimatedTotal, 0);
    });
  });
}
