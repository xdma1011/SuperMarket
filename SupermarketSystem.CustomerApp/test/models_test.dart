import 'package:flutter_test/flutter_test.dart';
import 'package:supermarket_customer_app/models/order.dart';
import 'package:supermarket_customer_app/models/product.dart';

void main() {
  test('Product.fromJson بشكل رد /catalog/products', () {
    final p = Product.fromJson({
      'productId': '11111111-1111-1111-1111-111111111111',
      'name': 'حليب',
      'description': null,
      'categoryName': 'ألبان',
      'price': 1.25,
      'primaryImageUrl': null,
      'baseUnitId': '22222222-2222-2222-2222-222222222222',
      'baseUnitName': 'حبة',
    });
    expect(p.price, 1.25);
    expect(p.baseUnitId, isNotEmpty);
  });

  test('OrderListItem.fromJson بحالة رقمية (نمط Code+Title بالباك إند)', () {
    final o = OrderListItem.fromJson({
      'id': 'x',
      'status': 2,
      'deliveryNote': null,
      'estimatedTotal': 3.5,
      'itemCount': 2,
      'createdAtUtc': '2026-10-09T10:00:00Z',
    });
    expect(o.status, OrderStatus.accepted);
    expect(o.itemsCount, 2);
  });
}
