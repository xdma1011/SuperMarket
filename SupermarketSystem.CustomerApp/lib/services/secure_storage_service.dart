import 'package:flutter_secure_storage/flutter_secure_storage.dart';

/// تخزين آمن (Keychain/Keystore) لتوكن هوية الزبون ومعرّفه - لا
/// SharedPreferences نص عادي، نفس مبدأ عدم تخزين أسرار بنص صريح المتبع
/// بكل مكان بالمشروع (راجع صفحة المفاتيح المقنَّعة بلوحة الإدارة).
class SecureStorageService {
  static final SecureStorageService instance = SecureStorageService._internal();
  SecureStorageService._internal();

  final _storage = const FlutterSecureStorage();

  static const _accessTokenKey = 'customer_access_token';
  static const _customerIdKey = 'customer_id';
  static const _phoneKey = 'customer_phone';
  static const _branchIdKey = 'selected_branch_id';

  Future<void> saveSession({required String accessToken, required String customerId, required String phone}) async {
    await _storage.write(key: _accessTokenKey, value: accessToken);
    await _storage.write(key: _customerIdKey, value: customerId);
    await _storage.write(key: _phoneKey, value: phone);
  }

  Future<String?> readAccessToken() => _storage.read(key: _accessTokenKey);
  Future<String?> readCustomerId() => _storage.read(key: _customerIdKey);
  Future<String?> readPhone() => _storage.read(key: _phoneKey);

  Future<void> clearSession() async {
    await _storage.delete(key: _accessTokenKey);
    await _storage.delete(key: _customerIdKey);
    await _storage.delete(key: _phoneKey);
  }

  Future<void> saveBranchId(String branchId) => _storage.write(key: _branchIdKey, value: branchId);
  Future<String?> readBranchId() => _storage.read(key: _branchIdKey);
}
