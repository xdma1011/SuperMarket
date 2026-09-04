import 'package:flutter/foundation.dart';
import '../services/auth_service.dart';
import '../services/secure_storage_service.dart';

class AuthProvider extends ChangeNotifier {
  bool _isLoggedIn = false;
  String? _customerId;
  String? _phone;

  bool get isLoggedIn => _isLoggedIn;
  String? get customerId => _customerId;
  String? get phone => _phone;

  Future<void> loadSession() async {
    _customerId = await SecureStorageService.instance.readCustomerId();
    _phone = await SecureStorageService.instance.readPhone();
    _isLoggedIn = await AuthService.instance.isLoggedIn();
    notifyListeners();
  }

  Future<void> onLoggedIn() async {
    await loadSession();
  }

  Future<void> logout() async {
    await AuthService.instance.logout();
    _isLoggedIn = false;
    _customerId = null;
    _phone = null;
    notifyListeners();
  }
}
