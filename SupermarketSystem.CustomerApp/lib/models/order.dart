/// حالات الطلب - تطابق OrderStatus بالباك إند (Domain/Ordering/Order.cs) بالضبط.
enum OrderStatus { pending, accepted, completed, rejected }

OrderStatus orderStatusFromInt(int value) {
  switch (value) {
    case 1:
      return OrderStatus.pending;
    case 2:
      return OrderStatus.accepted;
    case 3:
      return OrderStatus.completed;
    case 4:
      return OrderStatus.rejected;
    default:
      return OrderStatus.pending;
  }
}

String orderStatusLabel(OrderStatus status) {
  switch (status) {
    case OrderStatus.pending:
      return 'قيد الانتظار';
    case OrderStatus.accepted:
      return 'تم القبول - جاري التجهيز';
    case OrderStatus.completed:
      return 'تم التسليم';
    case OrderStatus.rejected:
      return 'مرفوض';
  }
}

/// يطابق OrderListItemDto (GetPendingOrders.cs المشترك مع GetCustomerOrders).
class OrderListItem {
  final String id;
  final OrderStatus status;
  final String? deliveryNote;
  final double estimatedTotal;
  final int itemsCount;
  final DateTime createdAtUtc;

  OrderListItem({
    required this.id,
    required this.status,
    required this.estimatedTotal,
    required this.itemsCount,
    required this.createdAtUtc,
    this.deliveryNote,
  });

  factory OrderListItem.fromJson(Map<String, dynamic> json) {
    return OrderListItem(
      id: json['id'] as String,
      status: orderStatusFromInt(json['status'] as int),
      deliveryNote: json['deliveryNote'] as String?,
      estimatedTotal: (json['estimatedTotal'] as num).toDouble(),
      itemsCount: json['itemCount'] as int,
      createdAtUtc: DateTime.parse(json['createdAtUtc'] as String),
    );
  }
}

class OrderItemDetail {
  final String productId;
  final String productName;
  final String unitName;
  final double quantity;
  final double estimatedUnitPrice;

  OrderItemDetail({
    required this.productId,
    required this.productName,
    required this.unitName,
    required this.quantity,
    required this.estimatedUnitPrice,
  });

  factory OrderItemDetail.fromJson(Map<String, dynamic> json) {
    return OrderItemDetail(
      productId: json['productId'] as String,
      productName: json['productName'] as String,
      unitName: json['unitName'] as String,
      quantity: (json['quantity'] as num).toDouble(),
      estimatedUnitPrice: (json['estimatedUnitPrice'] as num).toDouble(),
    );
  }
}

/// يطابق OrderDetailDto (GetOrderById/GetOrderByIdQuery.cs).
class OrderDetail {
  final String id;
  final OrderStatus status;
  final String? deliveryNote;
  final String? rejectionReason;
  final List<OrderItemDetail> items;
  final DateTime createdAtUtc;
  final int? rating;
  final String? ratingComment;

  OrderDetail({
    required this.id,
    required this.status,
    required this.items,
    required this.createdAtUtc,
    this.deliveryNote,
    this.rejectionReason,
    this.rating,
    this.ratingComment,
  });

  factory OrderDetail.fromJson(Map<String, dynamic> json) {
    return OrderDetail(
      id: json['id'] as String,
      status: orderStatusFromInt(json['status'] as int),
      deliveryNote: json['deliveryNote'] as String?,
      rejectionReason: json['rejectionReason'] as String?,
      items: (json['items'] as List<dynamic>)
          .map((e) => OrderItemDetail.fromJson(e as Map<String, dynamic>))
          .toList(),
      createdAtUtc: DateTime.parse(json['createdAtUtc'] as String),
      rating: json['rating'] as int?,
      ratingComment: json['ratingComment'] as String?,
    );
  }
}
