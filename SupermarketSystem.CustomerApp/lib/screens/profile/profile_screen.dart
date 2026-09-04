import 'package:flutter/material.dart';
import 'package:provider/provider.dart';
import 'package:qr_flutter/qr_flutter.dart';
import '../../providers/auth_provider.dart';
import '../../services/customer_service.dart';
import '../auth/phone_entry_screen.dart';
import 'complaint_screen.dart';

class ProfileScreen extends StatefulWidget {
  const ProfileScreen({super.key});

  @override
  State<ProfileScreen> createState() => _ProfileScreenState();
}

class _ProfileScreenState extends State<ProfileScreen> {
  String? _qrToken;
  int? _loyaltyBalance;
  bool _loading = true;

  @override
  void initState() {
    super.initState();
    _load();
  }

  Future<void> _load() async {
    final customerId = context.read<AuthProvider>().customerId;
    if (customerId == null) return;

    try {
      final results = await Future.wait([
        CustomerService.instance.getQrToken(customerId),
        CustomerService.instance.getLoyaltyBalance(customerId),
      ]);
      if (!mounted) return;
      setState(() {
        _qrToken = results[0] as String;
        _loyaltyBalance = results[1] as int;
        _loading = false;
      });
    } catch (_) {
      if (!mounted) return;
      setState(() => _loading = false);
    }
  }

  Future<void> _logout() async {
    await context.read<AuthProvider>().logout();
    if (!mounted) return;
    Navigator.of(context).pushAndRemoveUntil(MaterialPageRoute(builder: (_) => const PhoneEntryScreen()), (route) => false);
  }

  @override
  Widget build(BuildContext context) {
    final phone = context.watch<AuthProvider>().phone;

    return Scaffold(
      appBar: AppBar(title: const Text('حسابي')),
      body: _loading
          ? const Center(child: CircularProgressIndicator())
          : ListView(
              padding: const EdgeInsets.all(16),
              children: [
                Text('رقم الهاتف: ${phone ?? '-'}', style: const TextStyle(fontSize: 16)),
                const SizedBox(height: 24),
                if (_qrToken != null) ...[
                  const Text('اعرض هذا الرمز للكاشير لتأكيد هويتك', style: TextStyle(fontWeight: FontWeight.bold)),
                  const SizedBox(height: 12),
                  Center(child: QrImageView(data: _qrToken!, size: 220)),
                  const SizedBox(height: 24),
                ],
                if (_loyaltyBalance != null) ...[
                  Card(
                    child: ListTile(
                      leading: const Icon(Icons.star, color: Colors.amber),
                      title: const Text('رصيد نقاط الولاء'),
                      trailing: Text('$_loyaltyBalance نقطة', style: const TextStyle(fontWeight: FontWeight.bold, fontSize: 16)),
                    ),
                  ),
                  const SizedBox(height: 12),
                ],
                ListTile(
                  leading: const Icon(Icons.report_problem_outlined),
                  title: const Text('تقديم شكوى'),
                  trailing: const Icon(Icons.arrow_back_ios, size: 16),
                  onTap: () => Navigator.of(context).push(MaterialPageRoute(builder: (_) => const ComplaintScreen())),
                ),
                const Divider(),
                ListTile(
                  leading: const Icon(Icons.logout, color: Colors.red),
                  title: const Text('تسجيل الخروج', style: TextStyle(color: Colors.red)),
                  onTap: _logout,
                ),
              ],
            ),
    );
  }
}
