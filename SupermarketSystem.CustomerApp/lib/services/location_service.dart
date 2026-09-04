import 'package:geolocator/geolocator.dart';

/// يجلب موقع الزبون الحالي (GPS) - يُستخدم كنقطة بداية بخريطة اختيار
/// موقع التسليم، بدل ما الخريطة تفتح دايمًا على نفس الإحداثيات الافتراضية.
class LocationService {
  static final LocationService instance = LocationService._internal();
  LocationService._internal();

  Future<Position?> getCurrentPosition() async {
    var permission = await Geolocator.checkPermission();
    if (permission == LocationPermission.denied) {
      permission = await Geolocator.requestPermission();
    }

    if (permission == LocationPermission.denied || permission == LocationPermission.deniedForever) {
      return null;
    }

    if (!await Geolocator.isLocationServiceEnabled()) {
      return null;
    }

    return Geolocator.getCurrentPosition(desiredAccuracy: LocationAccuracy.high);
  }
}
