/** ApiController.Coupons - كوبونات خصم تطبيق الزبائن (Customers.Manage)، راجع CouponEndpoints.cs. */
export enum CouponsOperation {
  List = '',
  Create = '',
  Update = '{id}',
  SetActive = '{id}/active',
  Send = '{id}/send'
}
