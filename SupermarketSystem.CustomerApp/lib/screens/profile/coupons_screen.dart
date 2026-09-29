import 'package:flutter/material.dart';
import 'package:flutter/services.dart';
import 'package:provider/provider.dart';
import '../../models/coupon.dart';
import '../../providers/auth_provider.dart';
import '../../services/coupon_service.dart';

/// كوبوناتي (29/9/2026): الكوبونات الشغّالة للزبون (المخصصة إله + العامة). الكود بيتنسخ بكبسة، وبينكتب وقت الطلب.
class CouponsScreen extends StatefulWidget {
  const CouponsScreen({super.key});

  @override
  State<CouponsScreen> createState() => _CouponsScreenState();
}

class _CouponsScreenState extends State<CouponsScreen> {
  List<CustomerCoupon>? _coupons;
  String? _error;

  @override
  void initState() {
    super.initState();
    _load();
  }

  Future<void> _load() async {
    final customerId = context.read<AuthProvider>().customerId;
    if (customerId == null) {
      setState(() => _error = 'سجّل دخول أول.');
      return;
    }

    try {
      final coupons = await CouponService.instance.getMyCoupons(customerId);
      if (!mounted) return;
      setState(() => _coupons = coupons);
    } catch (_) {
      if (!mounted) return;
      setState(() => _error = 'تعذّر تحميل الكوبونات.');
    }
  }

  @override
  Widget build(BuildContext context) {
    return Scaffold(
      appBar: AppBar(title: const Text('كوبوناتي')),
      body: _error != null
          ? Center(child: Text(_error!))
          : _coupons == null
              ? const Center(child: CircularProgressIndicator())
              : _coupons!.isEmpty
                  ? const Center(child: Text('ما في كوبونات حاليًا.'))
                  : ListView.separated(
                      padding: const EdgeInsets.all(16),
                      itemCount: _coupons!.length,
                      separatorBuilder: (_, __) => const SizedBox(height: 10),
                      itemBuilder: (_, i) => _CouponCard(coupon: _coupons![i]),
                    ),
    );
  }
}

class _CouponCard extends StatelessWidget {
  final CustomerCoupon coupon;

  const _CouponCard({required this.coupon});

  @override
  Widget build(BuildContext context) {
    final end = coupon.endAtUtc.toLocal();
    return Card(
      child: ListTile(
        leading: Icon(coupon.isPersonal ? Icons.card_giftcard : Icons.local_offer_outlined, color: Colors.green),
        title: Text(coupon.title, style: const TextStyle(fontWeight: FontWeight.bold)),
        subtitle: Text(
          '${coupon.discountDescription}'
          '${coupon.minOrderAmount > 0 ? ' على طلب ${coupon.minOrderAmount.toStringAsFixed(3)} د.أ وأكتر' : ''}\n'
          'لحد ${end.day}/${end.month}/${end.year}',
        ),
        isThreeLine: true,
        trailing: TextButton(
          onPressed: () {
            Clipboard.setData(ClipboardData(text: coupon.code));
            ScaffoldMessenger.of(context).showSnackBar(SnackBar(content: Text('انسخ الكود ${coupon.code}')));
          },
          child: Text(coupon.code, textDirection: TextDirection.ltr),
        ),
      ),
    );
  }
}
