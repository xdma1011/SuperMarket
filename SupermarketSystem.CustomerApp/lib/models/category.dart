/// يطابق PublicCategoryDto (GetPublicCatalogCategoriesQuery.cs).
class ProductCategory {
  final String id;
  final String name;

  ProductCategory({required this.id, required this.name});

  factory ProductCategory.fromJson(Map<String, dynamic> json) {
    return ProductCategory(id: json['id'] as String, name: json['name'] as String);
  }
}
