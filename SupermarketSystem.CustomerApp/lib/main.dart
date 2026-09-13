import 'package:flutter/material.dart';
import 'package:provider/provider.dart';
import 'providers/auth_provider.dart';
import 'providers/branch_provider.dart';
import 'providers/cart_provider.dart';
import 'screens/splash_screen.dart';

void main() {
  runApp(const SupermarketCustomerApp());
}

class SupermarketCustomerApp extends StatelessWidget {
  const SupermarketCustomerApp({super.key});

  @override
  Widget build(BuildContext context) {
    return MultiProvider(
      providers: [
        ChangeNotifierProvider(create: (_) => AuthProvider()),
        ChangeNotifierProvider(create: (_) => BranchProvider()),
        ChangeNotifierProvider(create: (_) => CartProvider()),
      ],
      child: MaterialApp(
        title: 'تطبيق الطلبات',
        debugShowCheckedModeBanner: false,
        locale: const Locale('ar'),
        theme: _buildTheme(),
        builder: (context, child) => Directionality(textDirection: TextDirection.rtl, child: child!),
        home: const SplashScreen(),
      ),
    );
  }

  ThemeData _buildTheme() {
    final base = ThemeData(colorSchemeSeed: Colors.green, useMaterial3: true);
    return base.copyWith(
      textTheme: base.textTheme.apply(fontFamilyFallback: const ['NotoSansArabic']),
    );
  }
}
