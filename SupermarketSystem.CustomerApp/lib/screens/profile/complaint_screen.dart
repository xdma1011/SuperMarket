import 'package:flutter/material.dart';
import 'package:provider/provider.dart';
import '../../providers/auth_provider.dart';
import '../../services/customer_service.dart';

class ComplaintScreen extends StatefulWidget {
  const ComplaintScreen({super.key});

  @override
  State<ComplaintScreen> createState() => _ComplaintScreenState();
}

class _ComplaintScreenState extends State<ComplaintScreen> {
  final _textController = TextEditingController();
  bool _sending = false;
  String? _error;

  Future<void> _submit() async {
    if (_textController.text.trim().isEmpty) {
      setState(() => _error = 'اكتب نص الشكوى.');
      return;
    }

    final customerId = context.read<AuthProvider>().customerId;
    if (customerId == null) return;

    setState(() {
      _sending = true;
      _error = null;
    });

    try {
      await CustomerService.instance.fileComplaint(customerId: customerId, text: _textController.text.trim());
      if (!mounted) return;
      Navigator.of(context).pop();
      ScaffoldMessenger.of(context).showSnackBar(const SnackBar(content: Text('تم إرسال شكواك، سنتواصل معك.')));
    } catch (_) {
      setState(() {
        _error = 'تعذّر إرسال الشكوى.';
        _sending = false;
      });
    }
  }

  @override
  Widget build(BuildContext context) {
    return Scaffold(
      appBar: AppBar(title: const Text('تقديم شكوى')),
      body: Padding(
        padding: const EdgeInsets.all(16),
        child: Column(
          crossAxisAlignment: CrossAxisAlignment.stretch,
          children: [
            TextField(
              controller: _textController,
              maxLines: 6,
              decoration: const InputDecoration(border: OutlineInputBorder(), labelText: 'اكتب شكواك هنا'),
            ),
            const SizedBox(height: 16),
            if (_error != null) Text(_error!, style: const TextStyle(color: Colors.red)),
            const SizedBox(height: 16),
            ElevatedButton(
              onPressed: _sending ? null : _submit,
              child: _sending ? const CircularProgressIndicator() : const Text('إرسال'),
            ),
          ],
        ),
      ),
    );
  }
}
