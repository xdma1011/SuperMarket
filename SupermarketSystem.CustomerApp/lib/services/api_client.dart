import 'dart:convert';
import 'package:http/http.dart' as http;
import '../config/api_config.dart';
import 'secure_storage_service.dart';

/// استثناء موحَّد لأي فشل HTTP - يحمل رسالة عربية جاهزة للعرض مباشرة
/// (الباك إند يرجّع {detail} أو {message} عربي جاهز بأغلب الأخطاء).
class ApiException implements Exception {
  final int statusCode;
  final String message;
  ApiException(this.statusCode, this.message);

  @override
  String toString() => message;
}

/// غلاف HTTP موحَّد - كل نداء API بالتطبيق لازم يمر من هون، نفس مبدأ
/// ApiClient بلوحة الإدارة (Angular). يرفق توكن هوية الزبون تلقائيًا لو
/// موجود (⚠️ الباك إند لسا ما يتحقق منه فعليًا على نقاط الطلبات - راجع
/// تحذير OrderingEndpoints.cs، بس إرفاقه هون يخليه جاهز فور ما يُفعَّل).
class ApiClient {
  static final ApiClient instance = ApiClient._internal();
  ApiClient._internal();

  Future<Map<String, String>> _headers({bool withAuth = true}) async {
    final headers = {'Content-Type': 'application/json'};
    if (withAuth) {
      final token = await SecureStorageService.instance.readAccessToken();
      if (token != null) {
        headers['Authorization'] = 'Bearer $token';
      }
    }
    return headers;
  }

  Uri _uri(String path, [Map<String, dynamic>? query]) {
    final cleanQuery = <String, String>{};
    query?.forEach((key, value) {
      if (value != null) {
        cleanQuery[key] = value.toString();
      }
    });
    return Uri.parse('${ApiConfig.baseUrl}$path').replace(queryParameters: cleanQuery.isEmpty ? null : cleanQuery);
  }

  Future<dynamic> get(String path, {Map<String, dynamic>? query}) async {
    final response = await http.get(_uri(path, query), headers: await _headers());
    return _handle(response);
  }

  Future<dynamic> post(String path, {Object? body}) async {
    final response = await http.post(_uri(path), headers: await _headers(), body: body == null ? null : jsonEncode(body));
    return _handle(response);
  }

  Future<dynamic> put(String path, {Object? body}) async {
    final response = await http.put(_uri(path), headers: await _headers(), body: body == null ? null : jsonEncode(body));
    return _handle(response);
  }

  dynamic _handle(http.Response response) {
    if (response.statusCode >= 200 && response.statusCode < 300) {
      if (response.body.isEmpty) {
        return null;
      }
      return jsonDecode(utf8.decode(response.bodyBytes));
    }

    String message = 'حصل خطأ غير متوقَّع (${response.statusCode}).';
    try {
      final decoded = jsonDecode(utf8.decode(response.bodyBytes));
      if (decoded is Map<String, dynamic>) {
        message = (decoded['detail'] ?? decoded['title'] ?? decoded['message'] ?? message).toString();
      }
    } catch (_) {
      // جسم الرد مش JSON صالح - نكتفي بالرسالة الافتراضية أعلاه.
    }

    throw ApiException(response.statusCode, message);
  }
}
