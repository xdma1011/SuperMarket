import 'package:flutter/material.dart';
import 'package:provider/provider.dart';
import '../../providers/branch_provider.dart';
import '../../services/branch_service.dart';
import '../catalog/catalog_screen.dart';

class BranchSelectScreen extends StatefulWidget {
  const BranchSelectScreen({super.key});

  @override
  State<BranchSelectScreen> createState() => _BranchSelectScreenState();
}

class _BranchSelectScreenState extends State<BranchSelectScreen> {
  late Future<List<Branch>> _branchesFuture;

  @override
  void initState() {
    super.initState();
    _branchesFuture = BranchService.instance.getBranches();
  }

  void _select(Branch branch) async {
    await context.read<BranchProvider>().select(branch.id, branch.name);
    if (!mounted) return;
    Navigator.of(context).pushReplacement(MaterialPageRoute(builder: (_) => const CatalogScreen()));
  }

  @override
  Widget build(BuildContext context) {
    return Scaffold(
      appBar: AppBar(title: const Text('اختر فرعك')),
      body: FutureBuilder<List<Branch>>(
        future: _branchesFuture,
        builder: (context, snapshot) {
          if (snapshot.connectionState != ConnectionState.done) {
            return const Center(child: CircularProgressIndicator());
          }
          if (snapshot.hasError) {
            return Center(child: Text('تعذّر تحميل الفروع: ${snapshot.error}'));
          }
          final branches = snapshot.data ?? [];
          if (branches.isEmpty) {
            return const Center(child: Text('لا يوجد فروع متاحة حاليًا.'));
          }
          return ListView.separated(
            padding: const EdgeInsets.all(16),
            itemCount: branches.length,
            separatorBuilder: (_, __) => const SizedBox(height: 8),
            itemBuilder: (context, index) {
              final branch = branches[index];
              return Card(
                child: ListTile(
                  title: Text(branch.name),
                  trailing: const Icon(Icons.arrow_back_ios),
                  onTap: () => _select(branch),
                ),
              );
            },
          );
        },
      ),
    );
  }
}
