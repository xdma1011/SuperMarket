import 'package:flutter/material.dart';
import 'package:provider/provider.dart';
import '../models/product.dart';
import '../providers/cart_provider.dart';

class ProductCard extends StatelessWidget {
  final Product product;
  const ProductCard({super.key, required this.product});

  @override
  Widget build(BuildContext context) {
    return Card(
      clipBehavior: Clip.antiAlias,
      child: Column(
        crossAxisAlignment: CrossAxisAlignment.stretch,
        children: [
          AspectRatio(
            aspectRatio: 1,
            child: product.primaryImageUrl != null
                ? Image.network(product.primaryImageUrl!, fit: BoxFit.cover, errorBuilder: (_, __, ___) => const Icon(Icons.image_not_supported, size: 48))
                : Container(color: Colors.grey.shade200, child: const Icon(Icons.shopping_bag_outlined, size: 48)),
          ),
          Padding(
            padding: const EdgeInsets.all(8),
            child: Column(
              crossAxisAlignment: CrossAxisAlignment.start,
              children: [
                Text(product.name, maxLines: 2, overflow: TextOverflow.ellipsis, style: const TextStyle(fontWeight: FontWeight.bold)),
                const SizedBox(height: 4),
                Text('${product.price.toStringAsFixed(2)} د.أ / ${product.baseUnitName}', style: const TextStyle(color: Colors.green)),
                const SizedBox(height: 4),
                SizedBox(
                  width: double.infinity,
                  child: ElevatedButton(
                    onPressed: () {
                      context.read<CartProvider>().add(product);
                      ScaffoldMessenger.of(context).showSnackBar(
                        SnackBar(content: Text('أُضيف "${product.name}" للسلة'), duration: const Duration(seconds: 1)),
                      );
                    },
                    child: const Text('إضافة للسلة'),
                  ),
                ),
              ],
            ),
          ),
        ],
      ),
    );
  }
}
