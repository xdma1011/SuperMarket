import '../models/cart_item.dart';
import '../models/order.dart';
import 'api_client.dart';

class OrdersPage {
  final List<OrderListItem> items;
  final int totalCount;
  final int totalPages;

  OrdersPage({required this.items, required this.totalCount, required this.totalPages});

  factory OrdersPage.fromJson(Map<String, dynamic> json) {
    return OrdersPage(
      items: (json['items'] as List<dynamic>).map((e) => OrderListItem.fromJson(e as Map<String, dynamic>)).toList(),
      totalCount: json['totalCount'] as int,
      totalPages: json['totalPages'] as int,
    );
  }
}

/// يطابق OrderingEndpoints.cs بالباك إند - نفس بنية PlaceOrderCommand بالضبط.
class OrderService {
  static final OrderService instance = OrderService._internal();
  OrderService._internal();

  final _api = ApiClient.instance;

  Future<String> placeOrder({
    required String customerPhone,
    String? customerName,
    required String branchId,
    String? deliveryNote,
    double? deliveryLatitude,
    double? deliveryLongitude,
    required List<CartItem> items,
  }) async {
    final result = await _api.post('/orders', body: {
      'customerPhone': customerPhone,
      'customerName': customerName,
      'branchId': branchId,
      'deliveryNote': deliveryNote,
      'deliveryLatitude': deliveryLatitude,
      'deliveryLongitude': deliveryLongitude,
      'items': items
          .map((c) => {
                'productId': c.product.id,
                'productUnitId': c.product.baseUnitId,
                'quantity': c.quantity,
              })
          .toList(),
    });
    return result['orderId'] as String;
  }

  Future<OrdersPage> getCustomerOrders(String customerId, {int pageNumber = 1, int pageSize = 20}) async {
    final result = await _api.get('/orders/customers/$customerId', query: {
      'pageNumber': pageNumber,
      'pageSize': pageSize,
    });
    return OrdersPage.fromJson(result as Map<String, dynamic>);
  }

  Future<OrderDetail> getOrderDetail(String orderId) async {
    final result = await _api.get('/orders/$orderId/customer-view');
    return OrderDetail.fromJson(result as Map<String, dynamic>);
  }

  Future<void> rateOrder({required String orderId, required int rating, String? comment}) async {
    await _api.post('/orders/$orderId/rate', body: {'rating': rating, 'comment': comment});
  }
}
