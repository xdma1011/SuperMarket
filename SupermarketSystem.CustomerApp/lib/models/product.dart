/// يطابق PublicCatalogItemDto بالباك إند (GetPublicCatalogQuery.cs) بالضبط.
class Product {
  final String id;
  final String name;
  final String? description;
  final String categoryName;
  final double price;
  final String? primaryImageUrl;
  final String baseUnitId;
  final String baseUnitName;

  Product({
    required this.id,
    required this.name,
    required this.categoryName,
    required this.price,
    required this.baseUnitId,
    required this.baseUnitName,
    this.description,
    this.primaryImageUrl,
  });

  factory Product.fromJson(Map<String, dynamic> json) {
    return Product(
      id: json['productId'] as String,
      name: json['name'] as String,
      description: json['description'] as String?,
      categoryName: json['categoryName'] as String,
      price: (json['price'] as num).toDouble(),
      primaryImageUrl: json['primaryImageUrl'] as String?,
      baseUnitId: json['baseUnitId'] as String? ?? '',
      baseUnitName: json['baseUnitName'] as String? ?? '',
    );
  }
}
