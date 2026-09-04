import 'package:flutter/material.dart';
import 'package:provider/provider.dart';
import '../providers/auth_provider.dart';
import '../providers/branch_provider.dart';
import 'auth/branch_select_screen.dart';
import 'auth/phone_entry_screen.dart';
import 'catalog/catalog_screen.dart';

/// يقرر شاشة البداية: تسجيل دخول → اختيار فرع → كتالوج، بالترتيب - كل
/// خطوة تُقرأ من التخزين الآمن المحلي (SecureStorageService)، بلا نداء
/// سيرفر إضافي لمجرد معرفة "وين نروّح المستخدم".
class SplashScreen extends StatefulWidget {
  const SplashScreen({super.key});

  @override
  State<SplashScreen> createState() => _SplashScreenState();
}

class _SplashScreenState extends State<SplashScreen> {
  @override
  void initState() {
    super.initState();
    _decideNextScreen();
  }

  Future<void> _decideNextScreen() async {
    final auth = context.read<AuthProvider>();
    final branch = context.read<BranchProvider>();

    await auth.loadSession();
    await branch.loadSaved();

    if (!mounted) return;

    Widget next;
    if (!auth.isLoggedIn) {
      next = const PhoneEntryScreen();
    } else if (!branch.hasBranch) {
      next = const BranchSelectScreen();
    } else {
      next = const CatalogScreen();
    }

    Navigator.of(context).pushReplacement(MaterialPageRoute(builder: (_) => next));
  }

  @override
  Widget build(BuildContext context) {
    return const Scaffold(body: Center(child: CircularProgressIndicator()));
  }
}
