import 'package:flutter/material.dart';
import 'package:intl/intl.dart';
import 'package:provider/provider.dart';
import '../../models/order.dart';
import '../../providers/auth_provider.dart';
import '../../services/order_service.dart';
import 'order_detail_screen.dart';

class OrdersScreen extends StatefulWidget {
  const OrdersScreen({super.key});

  @override
  State<OrdersScreen> createState() => _OrdersScreenState();
}

class _OrdersScreenState extends State<OrdersScreen> {
  late Future<OrdersPage> _ordersFuture;

  @override
  void initState() {
    super.initState();
    _load();
  }

  void _load() {
    final customerId = context.read<AuthProvider>().customerId;
    _ordersFuture = customerId == null
        ? Future.value(OrdersPage(items: [], totalCount: 0, totalPages: 0))
        : OrderService.instance.getCustomerOrders(customerId);
  }

  Color _statusColor(OrderStatus status) {
    switch (status) {
      case OrderStatus.pending:
        return Colors.orange;
      case OrderStatus.accepted:
        return Colors.blue;
      case OrderStatus.completed:
        return Colors.green;
      case OrderStatus.rejected:
        return Colors.red;
    }
  }

  @override
  Widget build(BuildContext context) {
    return Scaffold(
      appBar: AppBar(title: const Text('طلباتي')),
      body: RefreshIndicator(
        onRefresh: () async {
          setState(_load);
          await _ordersFuture;
        },
        child: FutureBuilder<OrdersPage>(
          future: _ordersFuture,
          builder: (context, snapshot) {
            if (snapshot.connectionState != ConnectionState.done) {
              return const Center(child: CircularProgressIndicator());
            }
            if (snapshot.hasError) {
              return Center(child: Text('تعذّر تحميل الطلبات: ${snapshot.error}'));
            }
            final orders = snapshot.data?.items ?? [];
            if (orders.isEmpty) {
              return ListView(children: const [SizedBox(height: 200), Center(child: Text('لا يوجد طلبات سابقة.'))]);
            }
            return ListView.separated(
              padding: const EdgeInsets.all(12),
              itemCount: orders.length,
              separatorBuilder: (_, __) => const SizedBox(height: 8),
              itemBuilder: (context, index) {
                final order = orders[index];
                return Card(
                  child: ListTile(
                    title: Text('${order.itemsCount} صنف - ${order.estimatedTotal.toStringAsFixed(2)} د.أ'),
                    subtitle: Text(DateFormat('yyyy-MM-dd HH:mm').format(order.createdAtUtc.toLocal())),
                    trailing: Chip(
                      label: Text(orderStatusLabel(order.status), style: const TextStyle(color: Colors.white, fontSize: 12)),
                      backgroundColor: _statusColor(order.status),
                    ),
                    onTap: () => Navigator.of(context).push(MaterialPageRoute(builder: (_) => OrderDetailScreen(orderId: order.id))),
                  ),
                );
              },
            );
          },
        ),
      ),
    );
  }
}
