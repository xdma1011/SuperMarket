/** ApiController.Stocktakes */
export enum StocktakesOperation {
  Create = '',
  List = '',
  GetById = '{id}',
  RecordCount = '{id}/items/{itemId}/count',
  Complete = '{id}/complete',
  Approve = '{id}/approve'
}
