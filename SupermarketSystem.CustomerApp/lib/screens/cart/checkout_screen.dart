import 'package:flutter/material.dart';
import 'package:latlong2/latlong.dart';
import 'package:provider/provider.dart';
import '../../providers/auth_provider.dart';
import '../../providers/branch_provider.dart';
import '../../providers/cart_provider.dart';
import '../../models/coupon.dart';
import '../../services/api_client.dart';
import '../../services/coupon_service.dart';
import '../../services/order_service.dart';
import 'location_picker_screen.dart';

class CheckoutScreen extends StatefulWidget {
  const CheckoutScreen({super.key});

  @override
  State<CheckoutScreen> createState() => _CheckoutScreenState();
}

class _CheckoutScreenState extends State<CheckoutScreen> {
  final _noteController = TextEditingController();
  final _couponController = TextEditingController();
  LatLng? _deliveryLocation;
  bool _placing = false;
  String? _error;

  // كوبون الخصم (29/9/2026): فحص قبل الطلب بس - الحجز الفعلي مع الطلب، والخصم النهائي على أسعار لحظة التسليم.
  CouponPreview? _couponPreview;
  String? _couponError;
  bool _checkingCoupon = false;

  @override
  void dispose() {
    _noteController.dispose();
    _couponController.dispose();
    super.dispose();
  }

  Future<void> _applyCoupon() async {
    final code = _couponController.text.trim();
    final customerId = context.read<AuthProvider>().customerId;
    final total = context.read<CartProvider>().estimatedTotal;
    if (code.isEmpty || customerId == null) return;

    setState(() {
      _checkingCoupon = true;
      _couponError = null;
    });
    try {
      final preview = await CouponService.instance.preview(customerId: customerId, code: code, estimatedTotal: total);
      if (!mounted) return;
      setState(() {
        _couponPreview = preview;
        _checkingCoupon = false;
      });
    } on ApiException catch (e) {
      if (!mounted) return;
      setState(() {
        _couponPreview = null;
        _couponError = e.message;
        _checkingCoupon = false;
      });
    } catch (_) {
      if (!mounted) return;
      setState(() {
        _couponPreview = null;
        _couponError = 'تعذّر فحص الكود.';
        _checkingCoupon = false;
      });
    }
  }

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
        couponCode: _couponController.text.trim().isEmpty ? null : _couponController.text.trim(),
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
            Text('الإجمالي التقديري: ${cart.estimatedTotal.toStringAsFixed(3)} د.أ'),
            if (_couponPreview != null) ...[
              Text('خصم ${_couponPreview!.title}: −${_couponPreview!.estimatedDiscount.toStringAsFixed(3)} د.أ',
                  style: const TextStyle(color: Colors.green)),
              Text('بعد الخصم: ${_couponPreview!.estimatedTotalAfterDiscount.toStringAsFixed(3)} د.أ',
                  style: const TextStyle(fontWeight: FontWeight.bold)),
            ],
            const SizedBox(height: 16),
            Row(
              children: [
                Expanded(
                  child: TextField(
                    controller: _couponController,
                    textDirection: TextDirection.ltr,
                    textCapitalization: TextCapitalization.characters,
                    decoration: const InputDecoration(border: OutlineInputBorder(), labelText: 'كود خصم (اختياري)'),
                    onChanged: (_) {
                      if (_couponPreview != null || _couponError != null) {
                        setState(() {
                          _couponPreview = null;
                          _couponError = null;
                        });
                      }
                    },
                  ),
                ),
                const SizedBox(width: 8),
                OutlinedButton(
                  onPressed: _checkingCoupon ? null : _applyCoupon,
                  child: _checkingCoupon
                      ? const SizedBox(width: 18, height: 18, child: CircularProgressIndicator(strokeWidth: 2))
                      : const Text('تطبيق'),
                ),
              ],
            ),
            if (_couponError != null) Text(_couponError!, style: const TextStyle(color: Colors.red)),
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
