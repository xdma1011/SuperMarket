/** ApiController.ProductCategories */
export enum ProductCategoriesOperation {
  Create = '',
  List = '',
  Update = '{categoryId}'
}

/** ApiController.Products */
export enum ProductsOperation {
  Create = '',
  List = '',
  Update = '{productId}',
  AddUnit = '{productId}/units',
  GetUnits = '{productId}/units',
  GetByBarcode = 'by-barcode/{barcodeValue}',
  SetComplimentaryAllowed = '{productId}/complimentary-allowed',
  GetBranches = '{productId}/branches',
  AddBranch = '{productId}/branches'
}
