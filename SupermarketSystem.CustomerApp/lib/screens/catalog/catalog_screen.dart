import 'package:flutter/material.dart';
import 'package:provider/provider.dart';
import '../../models/category.dart';
import '../../models/product.dart';
import '../../providers/branch_provider.dart';
import '../../providers/cart_provider.dart';
import '../../services/catalog_service.dart';
import '../../widgets/product_card.dart';
import '../cart/cart_screen.dart';
import '../orders/orders_screen.dart';
import '../profile/profile_screen.dart';

class CatalogScreen extends StatefulWidget {
  const CatalogScreen({super.key});

  @override
  State<CatalogScreen> createState() => _CatalogScreenState();
}

class _CatalogScreenState extends State<CatalogScreen> {
  final _searchController = TextEditingController();
  final _scrollController = ScrollController();

  List<ProductCategory> _categories = [];
  String? _selectedCategoryId;

  final List<Product> _products = [];
  int _pageNumber = 1;
  int _totalPages = 1;
  bool _loading = false;
  String? _error;

  @override
  void initState() {
    super.initState();
    _scrollController.addListener(_onScroll);
    _loadCategories();
    _loadPage(reset: true);
  }

  void _onScroll() {
    if (_scrollController.position.pixels >= _scrollController.position.maxScrollExtent - 200) {
      if (!_loading && _pageNumber < _totalPages) {
        _loadPage();
      }
    }
  }

  Future<void> _loadCategories() async {
    final branchId = context.read<BranchProvider>().branchId;
    if (branchId == null) return;
    try {
      final categories = await CatalogService.instance.getCategories(branchId);
      if (mounted) setState(() => _categories = categories);
    } catch (_) {
      // فشل تحميل التصنيفات ما يوقف تصفّح المنتجات - يبقى الفلتر مخفي بس.
    }
  }

  Future<void> _loadPage({bool reset = false}) async {
    final branchId = context.read<BranchProvider>().branchId;
    if (branchId == null) return;

    setState(() {
      _loading = true;
      _error = null;
      if (reset) {
        _products.clear();
        _pageNumber = 1;
        _totalPages = 1;
      }
    });

    try {
      final page = await CatalogService.instance.getProducts(
        branchId: branchId,
        categoryId: _selectedCategoryId,
        search: _searchController.text.trim().isEmpty ? null : _searchController.text.trim(),
        pageNumber: reset ? 1 : _pageNumber,
      );

      if (!mounted) return;
      setState(() {
        _products.addAll(page.items);
        _totalPages = page.totalPages;
        _pageNumber = (reset ? 1 : _pageNumber) + 1;
        _loading = false;
      });
    } catch (e) {
      if (!mounted) return;
      setState(() {
        _error = 'تعذّر تحميل المنتجات.';
        _loading = false;
      });
    }
  }

  void _onSearchSubmitted(String _) => _loadPage(reset: true);

  void _onCategorySelected(String? categoryId) {
    setState(() => _selectedCategoryId = categoryId);
    _loadPage(reset: true);
  }

  @override
  Widget build(BuildContext context) {
    final branchName = context.watch<BranchProvider>().branchName;
    final cartCount = context.watch<CartProvider>().itemCount;

    return Scaffold(
      appBar: AppBar(
        title: Text(branchName ?? 'المنتجات'),
        actions: [
          IconButton(icon: const Icon(Icons.receipt_long), onPressed: () => Navigator.of(context).push(MaterialPageRoute(builder: (_) => const OrdersScreen()))),
          IconButton(icon: const Icon(Icons.person), onPressed: () => Navigator.of(context).push(MaterialPageRoute(builder: (_) => const ProfileScreen()))),
          Stack(
            alignment: Alignment.center,
            children: [
              IconButton(icon: const Icon(Icons.shopping_cart), onPressed: () => Navigator.of(context).push(MaterialPageRoute(builder: (_) => const CartScreen()))),
              if (cartCount > 0)
                Positioned(
                  top: 8,
                  right: 8,
                  child: CircleAvatar(radius: 8, backgroundColor: Colors.red, child: Text('$cartCount', style: const TextStyle(color: Colors.white, fontSize: 10))),
                ),
            ],
          ),
        ],
      ),
      body: Column(
        children: [
          Padding(
            padding: const EdgeInsets.all(12),
            child: TextField(
              controller: _searchController,
              onSubmitted: _onSearchSubmitted,
              decoration: InputDecoration(
                border: const OutlineInputBorder(),
                hintText: 'بحث عن منتج...',
                prefixIcon: const Icon(Icons.search),
                suffixIcon: IconButton(icon: const Icon(Icons.send), onPressed: () => _onSearchSubmitted(_searchController.text)),
              ),
            ),
          ),
          if (_categories.isNotEmpty)
            SizedBox(
              height: 44,
              child: ListView(
                scrollDirection: Axis.horizontal,
                padding: const EdgeInsets.symmetric(horizontal: 12),
                children: [
                  Padding(
                    padding: const EdgeInsets.symmetric(horizontal: 4),
                    child: ChoiceChip(label: const Text('الكل'), selected: _selectedCategoryId == null, onSelected: (_) => _onCategorySelected(null)),
                  ),
                  ..._categories.map(
                    (c) => Padding(
                      padding: const EdgeInsets.symmetric(horizontal: 4),
                      child: ChoiceChip(label: Text(c.name), selected: _selectedCategoryId == c.id, onSelected: (_) => _onCategorySelected(c.id)),
                    ),
                  ),
                ],
              ),
            ),
          const SizedBox(height: 8),
          if (_error != null) Padding(padding: const EdgeInsets.all(8), child: Text(_error!, style: const TextStyle(color: Colors.red))),
          Expanded(
            child: _products.isEmpty && _loading
                ? const Center(child: CircularProgressIndicator())
                : _products.isEmpty
                    ? const Center(child: Text('لا توجد منتجات مطابقة.'))
                    : GridView.builder(
                        controller: _scrollController,
                        padding: const EdgeInsets.all(12),
                        gridDelegate: const SliverGridDelegateWithFixedCrossAxisCount(crossAxisCount: 2, mainAxisSpacing: 12, crossAxisSpacing: 12, childAspectRatio: 0.68),
                        itemCount: _products.length,
                        itemBuilder: (context, index) => ProductCard(product: _products[index]),
                      ),
          ),
        ],
      ),
    );
  }
}
