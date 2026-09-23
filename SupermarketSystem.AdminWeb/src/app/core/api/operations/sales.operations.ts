/** ApiController.Sales */
export enum SalesOperation {
  Complete = '',
  Void = '{id}/void',
  List = '',
  GetById = '{id}',
  RecordPayment = '{id}/payments',
  CustomerDebts = 'customer-debts'
}
