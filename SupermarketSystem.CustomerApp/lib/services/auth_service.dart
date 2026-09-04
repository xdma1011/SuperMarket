import 'api_client.dart';
import 'secure_storage_service.dart';

class OtpRequestResult {
  final bool telegramLinked;
  final String? telegramDeepLink;

  OtpRequestResult({required this.telegramLinked, this.telegramDeepLink});
}

/// تسجيل دخول الزبون عبر رقم الهاتف + تلغرام - يطابق تدفق
/// CustomerAuthEndpoints.cs بالباك إند بالضبط: طلب كود → تحقق → توكن.
class AuthService {
  static final AuthService instance = AuthService._internal();
  AuthService._internal();

  final _api = ApiClient.instance;

  Future<OtpRequestResult> requestOtp(String phone) async {
    final result = await _api.post('/customer-auth/request-otp', body: {'phone': phone});
    return OtpRequestResult(
      telegramLinked: result['telegramLinked'] as bool,
      telegramDeepLink: result['telegramDeepLink'] as String?,
    );
  }

  Future<void> verifyOtp({required String phone, required String code}) async {
    final result = await _api.post('/customer-auth/verify-otp', body: {'phone': phone, 'code': code});
    await SecureStorageService.instance.saveSession(
      accessToken: result['accessToken'] as String,
      customerId: result['customerId'] as String,
      phone: phone,
    );
  }

  Future<bool> isLoggedIn() async {
    final token = await SecureStorageService.instance.readAccessToken();
    return token != null;
  }

  Future<void> logout() => SecureStorageService.instance.clearSession();
}
