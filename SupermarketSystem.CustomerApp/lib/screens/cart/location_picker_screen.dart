import 'package:flutter/material.dart';
import 'package:flutter_map/flutter_map.dart';
import 'package:latlong2/latlong.dart';
import '../../services/location_service.dart';

/// خريطة مجانية (OpenStreetMap - بلا مفتاح API، راجع نقاش صاحب المشروع)
/// لاختيار موقع التسليم يدويًا. تفتح افتراضيًا على موقع الزبون الحالي
/// (GPS) لو متاح، وإلا على مركز افتراضي.
class LocationPickerScreen extends StatefulWidget {
  const LocationPickerScreen({super.key});

  @override
  State<LocationPickerScreen> createState() => _LocationPickerScreenState();
}

class _LocationPickerScreenState extends State<LocationPickerScreen> {
  static const _defaultCenter = LatLng(31.9539, 35.9106); // عمّان - نقطة بداية افتراضية فقط.

  LatLng _selected = _defaultCenter;
  final _mapController = MapController();

  @override
  void initState() {
    super.initState();
    _goToCurrentLocation();
  }

  Future<void> _goToCurrentLocation() async {
    final position = await LocationService.instance.getCurrentPosition();
    if (position != null && mounted) {
      final current = LatLng(position.latitude, position.longitude);
      setState(() => _selected = current);
      _mapController.move(current, 15);
    }
  }

  @override
  Widget build(BuildContext context) {
    return Scaffold(
      appBar: AppBar(
        title: const Text('اختر موقع التسليم'),
        actions: [IconButton(icon: const Icon(Icons.my_location), onPressed: _goToCurrentLocation)],
      ),
      body: FlutterMap(
        mapController: _mapController,
        options: MapOptions(
          initialCenter: _selected,
          initialZoom: 13,
          onTap: (tapPosition, point) => setState(() => _selected = point),
        ),
        children: [
          TileLayer(
            urlTemplate: 'https://tile.openstreetmap.org/{z}/{x}/{y}.png',
            userAgentPackageName: 'com.supermarket.customer_app',
          ),
          MarkerLayer(
            markers: [
              Marker(point: _selected, width: 40, height: 40, child: const Icon(Icons.location_pin, color: Colors.red, size: 40)),
            ],
          ),
        ],
      ),
      bottomNavigationBar: Padding(
        padding: const EdgeInsets.all(16),
        child: ElevatedButton(
          onPressed: () => Navigator.of(context).pop(_selected),
          child: const Text('تأكيد الموقع'),
        ),
      ),
    );
  }
}
