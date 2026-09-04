import 'package:flutter/material.dart';
import '../../models/order.dart';
import '../../services/order_service.dart';

class OrderDetailScreen extends StatefulWidget {
  final String orderId;
  const OrderDetailScreen({super.key, required this.orderId});

  @override
  State<OrderDetailScreen> createState() => _OrderDetailScreenState();
}

class _OrderDetailScreenState extends State<OrderDetailScreen> {
  late Future<OrderDetail> _orderFuture;
  int _selectedRating = 0;
  final _ratingCommentController = TextEditingController();
  bool _submittingRating = false;

  @override
  void initState() {
    super.initState();
    _orderFuture = OrderService.instance.getOrderDetail(widget.orderId);
  }

  Future<void> _submitRating() async {
    if (_selectedRating == 0) return;
    setState(() => _submittingRating = true);
    try {
      await OrderService.instance.rateOrder(
        orderId: widget.orderId,
        rating: _selectedRating,
        comment: _ratingCommentController.text.trim().isEmpty ? null : _ratingCommentController.text.trim(),
      );
      if (!mounted) return;
      setState(() {
        _submittingRating = false;
        _orderFuture = OrderService.instance.getOrderDetail(widget.orderId);
      });
    } catch (_) {
      if (!mounted) return;
      setState(() => _submittingRating = false);
      ScaffoldMessenger.of(context).showSnackBar(const SnackBar(content: Text('تعذّر إرسال التقييم.')));
    }
  }

  @override
  Widget build(BuildContext context) {
    return Scaffold(
      appBar: AppBar(title: const Text('تفاصيل الطلب')),
      body: FutureBuilder<OrderDetail>(
        future: _orderFuture,
        builder: (context, snapshot) {
          if (snapshot.connectionState != ConnectionState.done) {
            return const Center(child: CircularProgressIndicator());
          }
          if (snapshot.hasError) {
            return Center(child: Text('تعذّر تحميل الطلب: ${snapshot.error}'));
          }
          final order = snapshot.data!;
          return ListView(
            padding: const EdgeInsets.all(16),
            children: [
              Text(orderStatusLabel(order.status), style: const TextStyle(fontSize: 18, fontWeight: FontWeight.bold)),
              if (order.status == OrderStatus.rejected && order.rejectionReason != null) ...[
                const SizedBox(height: 8),
                Text('سبب الرفض: ${order.rejectionReason}', style: const TextStyle(color: Colors.red)),
              ],
              if (order.deliveryNote != null) ...[
                const SizedBox(height: 8),
                Text('ملاحظة: ${order.deliveryNote}'),
              ],
              const Divider(height: 32),
              const Text('الأصناف', style: TextStyle(fontWeight: FontWeight.bold)),
              const SizedBox(height: 8),
              ...order.items.map(
                (item) => ListTile(
                  contentPadding: EdgeInsets.zero,
                  title: Text(item.productName),
                  subtitle: Text('${item.quantity.toStringAsFixed(0)} ${item.unitName}'),
                  trailing: Text('${(item.quantity * item.estimatedUnitPrice).toStringAsFixed(2)} د.أ'),
                ),
              ),
              if (order.status == OrderStatus.completed && order.rating == null) ...[
                const Divider(height: 32),
                const Text('قيّم طلبك', style: TextStyle(fontWeight: FontWeight.bold)),
                const SizedBox(height: 8),
                Row(
                  children: List.generate(5, (i) {
                    final starValue = i + 1;
                    return IconButton(
                      icon: Icon(starValue <= _selectedRating ? Icons.star : Icons.star_border, color: Colors.amber),
                      onPressed: () => setState(() => _selectedRating = starValue),
                    );
                  }),
                ),
                TextField(
                  controller: _ratingCommentController,
                  decoration: const InputDecoration(border: OutlineInputBorder(), labelText: 'تعليق (اختياري)'),
                  maxLines: 2,
                ),
                const SizedBox(height: 12),
                ElevatedButton(
                  onPressed: _submittingRating || _selectedRating == 0 ? null : _submitRating,
                  child: _submittingRating ? const CircularProgressIndicator() : const Text('إرسال التقييم'),
                ),
              ] else if (order.rating != null) ...[
                const Divider(height: 32),
                Text('تقييمك: ${'⭐' * order.rating!}'),
                if (order.ratingComment != null) Text(order.ratingComment!),
              ],
            ],
          );
        },
      ),
    );
  }
}
