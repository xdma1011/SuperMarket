import 'api_client.dart';

/// يطابق CustomerEndpoints.cs بالباك إند (رصيد الولاء، QR الهوية، توكن
/// الجهاز للإشعارات، الشكاوى).
class CustomerService {
  static final CustomerService instance = CustomerService._internal();
  CustomerService._internal();

  final _api = ApiClient.instance;

  Future<String> getQrToken(String customerId) async {
    final result = await _api.get('/customers/$customerId/qr-token');
    return result['qrToken'] as String;
  }

  Future<int> getLoyaltyBalance(String customerId) async {
    final result = await _api.get('/customers/$customerId/loyalty-balance');
    return result['balance'] as int;
  }

  /// platform: 'Android' أو 'Ios' - يطابق DevicePlatform enum بالباك إند حرفيًا.
  Future<void> registerDeviceToken({required String customerId, required String token, required String platform}) async {
    await _api.post('/customers/$customerId/device-tokens', body: {'token': token, 'platform': platform});
  }

  Future<void> fileComplaint({required String customerId, String? orderId, required String text}) async {
    await _api.post('/complaints', body: {'customerId': customerId, 'orderId': orderId, 'text': text});
  }
}
