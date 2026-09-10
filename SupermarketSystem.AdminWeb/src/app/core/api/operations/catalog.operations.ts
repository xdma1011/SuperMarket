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
  UpdateUnitBarcode = '{productId}/units/{unitId}/barcode',
  GetByBarcode = 'by-barcode/{barcodeValue}',
  SetComplimentaryAllowed = '{productId}/complimentary-allowed',
  GetBranches = '{productId}/branches',
  AddBranch = '{productId}/branches',
  SetBranchAvailability = '{productId}/branches/{productBranchId}/availability',
  RequestPriceChange = '{productId}/branches/{productBranchId}/price-change-requests'
}

/** ApiController مباشر (لا Products) - PriceChangeRequests مسار مستقل عن /products، راجع تعليق CLAUDE.md §3.4 بـCatalogEndpoints.cs. */
export enum PriceChangeRequestsOperation {
  List = '',
  Approve = '{requestId}/approve',
  Reject = '{requestId}/reject'
}
