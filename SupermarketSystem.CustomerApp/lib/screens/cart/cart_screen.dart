import 'package:flutter/material.dart';
import 'package:provider/provider.dart';
import '../../providers/cart_provider.dart';
import 'checkout_screen.dart';

class CartScreen extends StatelessWidget {
  const CartScreen({super.key});

  @override
  Widget build(BuildContext context) {
    final cart = context.watch<CartProvider>();

    return Scaffold(
      appBar: AppBar(title: const Text('سلة التسوّق')),
      body: cart.isEmpty
          ? const Center(child: Text('السلة فارغة.'))
          : ListView.separated(
              padding: const EdgeInsets.all(12),
              itemCount: cart.items.length,
              separatorBuilder: (_, __) => const Divider(),
              itemBuilder: (context, index) {
                final item = cart.items[index];
                return ListTile(
                  title: Text(item.product.name),
                  subtitle: Text('${item.product.price.toStringAsFixed(2)} د.أ / ${item.product.baseUnitName}'),
                  trailing: Row(
                    mainAxisSize: MainAxisSize.min,
                    children: [
                      IconButton(icon: const Icon(Icons.remove_circle_outline), onPressed: () => context.read<CartProvider>().updateQuantity(item.product.id, item.quantity - 1)),
                      Text(item.quantity.toStringAsFixed(0)),
                      IconButton(icon: const Icon(Icons.add_circle_outline), onPressed: () => context.read<CartProvider>().updateQuantity(item.product.id, item.quantity + 1)),
                      IconButton(icon: const Icon(Icons.delete_outline, color: Colors.red), onPressed: () => context.read<CartProvider>().remove(item.product.id)),
                    ],
                  ),
                );
              },
            ),
      bottomNavigationBar: cart.isEmpty
          ? null
          : Padding(
              padding: const EdgeInsets.all(16),
              child: Column(
                mainAxisSize: MainAxisSize.min,
                children: [
                  Row(
                    mainAxisAlignment: MainAxisAlignment.spaceBetween,
                    children: [
                      const Text('الإجمالي التقديري', style: TextStyle(fontWeight: FontWeight.bold)),
                      Text('${cart.estimatedTotal.toStringAsFixed(2)} د.أ', style: const TextStyle(fontWeight: FontWeight.bold, fontSize: 18)),
                    ],
                  ),
                  const SizedBox(height: 4),
                  const Text('السعر النهائي يُحسَب عند تأكيد الفرع للطلب.', style: TextStyle(fontSize: 12, color: Colors.grey)),
                  const SizedBox(height: 12),
                  SizedBox(
                    width: double.infinity,
                    child: ElevatedButton(
                      onPressed: () => Navigator.of(context).push(MaterialPageRoute(builder: (_) => const CheckoutScreen())),
                      child: const Text('متابعة الطلب'),
                    ),
                  ),
                ],
              ),
            ),
    );
  }
}
