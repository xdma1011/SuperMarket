import 'package:flutter/foundation.dart';
import '../services/secure_storage_service.dart';

class BranchProvider extends ChangeNotifier {
  String? _branchId;
  String? _branchName;

  String? get branchId => _branchId;
  String? get branchName => _branchName;
  bool get hasBranch => _branchId != null;

  Future<void> loadSaved() async {
    _branchId = await SecureStorageService.instance.readBranchId();
    notifyListeners();
  }

  Future<void> select(String branchId, String branchName) async {
    _branchId = branchId;
    _branchName = branchName;
    await SecureStorageService.instance.saveBranchId(branchId);
    notifyListeners();
  }
}
