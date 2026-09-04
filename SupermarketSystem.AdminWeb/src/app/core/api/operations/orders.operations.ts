/** ApiController.Orders */
export enum OrdersOperation {
  List = '',
  GetById = '{orderId}',
  Accept = '{orderId}/accept',
  Reject = '{orderId}/reject',
  Complete = '{orderId}/complete'
}
