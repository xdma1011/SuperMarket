import '../models/coupon.dart';
import 'api_client.dart';

/// يطابق CouponEndpoints.cs بالباك إند (جهة الزبون).
class CouponService {
  static final CouponService instance = CouponService._internal();
  CouponService._internal();

  final _api = ApiClient.instance;

  Future<List<CustomerCoupon>> getMyCoupons(String customerId) async {
    final result = await _api.get('/customers/$customerId/coupons');
    return (result as List<dynamic>).map((e) => CustomerCoupon.fromJson(e as Map<String, dynamic>)).toList();
  }

  /// فحص الكود على مجموع السلة التقديري - ApiException برسالة عربية لو الكود مش صالح.
  Future<CouponPreview> preview({required String customerId, required String code, required double estimatedTotal}) async {
    final result = await _api.post('/customers/$customerId/coupons/preview', body: {
      'code': code,
      'estimatedTotal': estimatedTotal,
    });
    return CouponPreview.fromJson(result as Map<String, dynamic>);
  }
}
