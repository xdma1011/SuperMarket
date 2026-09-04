import 'package:flutter/material.dart';
import 'package:latlong2/latlong.dart';
import 'package:provider/provider.dart';
import '../../providers/auth_provider.dart';
import '../../providers/branch_provider.dart';
import '../../providers/cart_provider.dart';
import '../../services/api_client.dart';
import '../../services/order_service.dart';
import 'location_picker_screen.dart';

class CheckoutScreen extends StatefulWidget {
  const CheckoutScreen({super.key});

  @override
  State<CheckoutScreen> createState() => _CheckoutScreenState();
}

class _CheckoutScreenState extends State<CheckoutScreen> {
  final _noteController = TextEditingController();
  LatLng? _deliveryLocation;
  bool _placing = false;
  String? _error;

  Future<void> _pickLocation() async {
    final result = await Navigator.of(context).push<LatLng>(MaterialPageRoute(builder: (_) => const LocationPickerScreen()));
    if (result != null) {
      setState(() => _deliveryLocation = result);
    }
  }

  Future<void> _placeOrder() async {
    final auth = context.read<AuthProvider>();
    final branch = context.read<BranchProvider>();
    final cart = context.read<CartProvider>();

    if (auth.phone == null || branch.branchId == null) {
      setState(() => _error = 'تعذّر تحديد الزبون أو الفرع.');
      return;
    }

    setState(() {
      _placing = true;
      _error = null;
    });

    try {
      final orderId = await OrderService.instance.placeOrder(
        customerPhone: auth.phone!,
        branchId: branch.branchId!,
        deliveryNote: _noteController.text.trim().isEmpty ? null : _noteController.text.trim(),
        deliveryLatitude: _deliveryLocation?.latitude,
        deliveryLongitude: _deliveryLocation?.longitude,
        items: cart.items,
      );

      if (!mounted) return;
      cart.clear();

      showDialog(
        context: context,
        barrierDismissible: false,
        builder: (_) => AlertDialog(
          title: const Text('تم إرسال طلبك ✅'),
          content: Text('رقم الطلب: $orderId\nبانتظار قبول الفرع.'),
          actions: [
            TextButton(
              onPressed: () => Navigator.of(context)
                ..pop()
                ..popUntil((route) => route.isFirst),
              child: const Text('حسنًا'),
            ),
          ],
        ),
      );
    } on ApiException catch (e) {
      setState(() {
        _error = e.message;
        _placing = false;
      });
    } catch (_) {
      setState(() {
        _error = 'تعذّر إرسال الطلب، حاول مجددًا.';
        _placing = false;
      });
    }
  }

  @override
  Widget build(BuildContext context) {
    final cart = context.watch<CartProvider>();

    return Scaffold(
      appBar: AppBar(title: const Text('تأكيد الطلب')),
      body: Padding(
        padding: const EdgeInsets.all(16),
        child: Column(
          crossAxisAlignment: CrossAxisAlignment.stretch,
          children: [
            Text('عدد الأصناف: ${cart.itemCount}'),
            Text('الإجمالي التقديري: ${cart.estimatedTotal.toStringAsFixed(2)} د.أ'),
            const SizedBox(height: 16),
            TextField(
              controller: _noteController,
              maxLines: 2,
              decoration: const InputDecoration(border: OutlineInputBorder(), labelText: 'ملاحظة للتوصيل (اختياري)'),
            ),
            const SizedBox(height: 16),
            OutlinedButton.icon(
              icon: const Icon(Icons.location_on_outlined),
              label: Text(_deliveryLocation == null ? 'اختر موقع التسليم على الخريطة' : 'تم تحديد الموقع ✓'),
              onPressed: _pickLocation,
            ),
            const SizedBox(height: 16),
            if (_error != null) Text(_error!, style: const TextStyle(color: Colors.red)),
            const SizedBox(height: 16),
            ElevatedButton(
              onPressed: _placing ? null : _placeOrder,
              child: _placing ? const CircularProgressIndicator() : const Text('إرسال الطلب'),
            ),
          ],
        ),
      ),
    );
  }
}
