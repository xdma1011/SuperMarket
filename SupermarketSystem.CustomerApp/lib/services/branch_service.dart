import 'api_client.dart';

class Branch {
  final String id;
  final String name;
  Branch({required this.id, required this.name});

  factory Branch.fromJson(Map<String, dynamic> json) {
    return Branch(id: json['id'] as String, name: json['name'] as String);
  }
}

/// يستخدم /api/v1/auth/branches الموجود أصلًا (GetPublicBranchesHandler) -
/// نفس القائمة اللي تعبّي صفحة اختيار الفرع بتسجيل دخول الموظفين.
class BranchService {
  static final BranchService instance = BranchService._internal();
  BranchService._internal();

  Future<List<Branch>> getBranches() async {
    final result = await ApiClient.instance.get('/auth/branches');
    return (result as List<dynamic>).map((e) => Branch.fromJson(e as Map<String, dynamic>)).toList();
  }
}
