/// كوبون خصم ظاهر للزبون (29/9/2026) - يطابق CustomerCouponDto بالباك إند (CouponHandlers.cs).
class CustomerCoupon {
  final String code;
  final String title;
  final String discountDescription;
  final double minOrderAmount;
  final DateTime endAtUtc;
  final int remainingUses;
  final bool isPersonal;

  CustomerCoupon({
    required this.code,
    required this.title,
    required this.discountDescription,
    required this.minOrderAmount,
    required this.endAtUtc,
    required this.remainingUses,
    required this.isPersonal,
  });

  factory CustomerCoupon.fromJson(Map<String, dynamic> json) {
    return CustomerCoupon(
      code: json['code'] as String,
      title: json['title'] as String,
      discountDescription: json['discountDescription'] as String,
      minOrderAmount: (json['minOrderAmount'] as num).toDouble(),
      endAtUtc: DateTime.parse(json['endAtUtc'] as String),
      remainingUses: json['remainingUses'] as int,
      isPersonal: json['isPersonal'] as bool,
    );
  }
}

/// نتيجة فحص الكود على مجموع السلة (بلا حجز) - PreviewCouponResponse.
class CouponPreview {
  final String code;
  final String title;
  final double estimatedDiscount;
  final double estimatedTotalAfterDiscount;

  CouponPreview({
    required this.code,
    required this.title,
    required this.estimatedDiscount,
    required this.estimatedTotalAfterDiscount,
  });

  factory CouponPreview.fromJson(Map<String, dynamic> json) {
    return CouponPreview(
      code: json['code'] as String,
      title: json['title'] as String,
      estimatedDiscount: (json['estimatedDiscount'] as num).toDouble(),
      estimatedTotalAfterDiscount: (json['estimatedTotalAfterDiscount'] as num).toDouble(),
    );
  }
}
