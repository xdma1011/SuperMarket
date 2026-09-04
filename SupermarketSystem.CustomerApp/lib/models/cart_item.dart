import 'product.dart';

/// عربة التسوّق محليًا فقط بالتطبيق - السعر النهائي دائمًا من السيرفر
/// لحظة تقديم الطلب (نفس مبدأ الكاشير، راجع CLAUDE.md §3.6). السعر هون
/// تقديري بس لعرضه للزبون قبل الإرسال.
class CartItem {
  final Product product;
  double quantity;

  CartItem({required this.product, this.quantity = 1});

  double get estimatedLineTotal => product.price * quantity;
}
