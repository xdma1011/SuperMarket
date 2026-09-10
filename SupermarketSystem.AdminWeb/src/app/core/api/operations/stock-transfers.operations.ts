/** ApiController.StockTransfers */
export enum StockTransfersOperation {
  List = '',
  Create = '',
  Detail = '{stockTransferId}',
  Receive = '{stockTransferId}/receive',
  ProductBatches = 'product-batches'
}
