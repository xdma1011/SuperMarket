import '../models/category.dart';
import '../models/product.dart';
import 'api_client.dart';

class CatalogPage {
  final List<Product> items;
  final int totalCount;
  final int pageNumber;
  final int totalPages;

  CatalogPage({required this.items, required this.totalCount, required this.pageNumber, required this.totalPages});

  factory CatalogPage.fromJson(Map<String, dynamic> json) {
    return CatalogPage(
      items: (json['items'] as List<dynamic>).map((e) => Product.fromJson(e as Map<String, dynamic>)).toList(),
      totalCount: json['totalCount'] as int,
      pageNumber: json['pageNumber'] as int,
      totalPages: json['totalPages'] as int,
    );
  }
}

/// يطابق PublicCatalogEndpoints.cs بالباك إند - تصفّح بلا حاجة تسجيل دخول.
class CatalogService {
  static final CatalogService instance = CatalogService._internal();
  CatalogService._internal();

  final _api = ApiClient.instance;

  Future<CatalogPage> getProducts({
    required String branchId,
    String? categoryId,
    String? search,
    int pageNumber = 1,
    int pageSize = 20,
  }) async {
    final result = await _api.get('/catalog/products', query: {
      'branchId': branchId,
      'categoryId': categoryId,
      'search': search,
      'pageNumber': pageNumber,
      'pageSize': pageSize,
    });
    return CatalogPage.fromJson(result as Map<String, dynamic>);
  }

  Future<List<ProductCategory>> getCategories(String branchId) async {
    final result = await _api.get('/catalog/categories', query: {'branchId': branchId});
    return (result as List<dynamic>).map((e) => ProductCategory.fromJson(e as Map<String, dynamic>)).toList();
  }
}
