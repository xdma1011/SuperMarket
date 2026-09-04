import 'package:flutter/material.dart';
import 'package:url_launcher/url_launcher.dart';
import '../../services/api_client.dart';
import '../../services/auth_service.dart';
import 'otp_verify_screen.dart';

/// خطوة 1 من تسجيل الدخول - رقم الهاتف. يطابق تدفق
/// CustomerAuthEndpoints.cs بالضبط: لو الرقم مش مربوط بتلغرام بعد، يطلع
/// رابط فتح البوت (deep link) بدل ما يرسل كود مباشرة.
class PhoneEntryScreen extends StatefulWidget {
  const PhoneEntryScreen({super.key});

  @override
  State<PhoneEntryScreen> createState() => _PhoneEntryScreenState();
}

class _PhoneEntryScreenState extends State<PhoneEntryScreen> {
  final _phoneController = TextEditingController();
  bool _loading = false;
  String? _error;
  String? _telegramDeepLink;

  Future<void> _requestOtp() async {
    final phone = _phoneController.text.trim();
    if (phone.isEmpty) {
      setState(() => _error = 'أدخل رقم هاتفك.');
      return;
    }

    setState(() {
      _loading = true;
      _error = null;
      _telegramDeepLink = null;
    });

    try {
      final result = await AuthService.instance.requestOtp(phone);
      if (!mounted) return;

      if (!result.telegramLinked) {
        setState(() {
          _telegramDeepLink = result.telegramDeepLink;
          _loading = false;
        });
        return;
      }

      Navigator.of(context).push(MaterialPageRoute(builder: (_) => OtpVerifyScreen(phone: phone)));
      setState(() => _loading = false);
    } on ApiException catch (e) {
      setState(() {
        _error = e.message;
        _loading = false;
      });
    } catch (_) {
      setState(() {
        _error = 'تعذّر الاتصال بالسيرفر، تأكد من اتصالك بالإنترنت.';
        _loading = false;
      });
    }
  }

  @override
  Widget build(BuildContext context) {
    return Scaffold(
      appBar: AppBar(title: const Text('تسجيل الدخول')),
      body: Padding(
        padding: const EdgeInsets.all(24),
        child: Column(
          crossAxisAlignment: CrossAxisAlignment.stretch,
          children: [
            const Text('أدخل رقم هاتفك وسنرسل لك كود تحقق عبر تلغرام.', style: TextStyle(fontSize: 16)),
            const SizedBox(height: 16),
            TextField(
              controller: _phoneController,
              keyboardType: TextInputType.phone,
              decoration: const InputDecoration(border: OutlineInputBorder(), labelText: 'رقم الهاتف', hintText: '9627xxxxxxxx'),
            ),
            const SizedBox(height: 16),
            if (_error != null) Text(_error!, style: const TextStyle(color: Colors.red)),
            if (_telegramDeepLink != null) ...[
              const SizedBox(height: 8),
              const Text('رقمك غير مربوط بعد بتلغرام. افتح البوت وشارك رقم هاتفك، ثم ارجع وحاول مجددًا.'),
              const SizedBox(height: 8),
              ElevatedButton.icon(
                icon: const Icon(Icons.telegram),
                label: const Text('فتح بوت تلغرام'),
                onPressed: () => launchUrl(Uri.parse(_telegramDeepLink!), mode: LaunchMode.externalApplication),
              ),
            ],
            const SizedBox(height: 24),
            ElevatedButton(
              onPressed: _loading ? null : _requestOtp,
              child: _loading ? const CircularProgressIndicator() : const Text('إرسال كود التحقق'),
            ),
          ],
        ),
      ),
    );
  }
}
