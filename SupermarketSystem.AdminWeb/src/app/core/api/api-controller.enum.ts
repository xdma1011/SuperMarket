/**
 * كل قيمة هون = المسار الأساسي لملف Endpoints مطابق بالباك إند (بلا
 * البادئة api/v1، مُضافة مركزيًا بـApiClient). أي controller جديد
 * بالباك إند يحتاج قيمة جديدة هون أول شي، قبل أي خدمة تستخدمه.
 */
export enum ApiController {
  Auth = 'auth',
  AuthSessions = 'auth/sessions',
  Backups = 'backups',
  Branches = 'branches',
  CashClosings = 'cash-closings',
  Customers = 'customers',
  Driver = 'driver',
  Inventory = 'inventory',
  ProductCategories = 'product-categories',
  PriceChangeRequests = 'price-change-requests',
  Products = 'products',
  PurchaseInvoices = 'purchase-invoices',
  Notifications = 'notifications',
  Orders = 'orders',
  PaymentMethods = 'payment-methods',
  Reports = 'reports',
  Returns = 'returns',
  Reviews = 'reviews',
  Sales = 'sales',
  StockTransfers = 'stock-transfers',
  Stocktakes = 'stocktakes',
  Suppliers = 'suppliers',
  System = 'system',
  UnitsOfMeasure = 'units-of-measure',
  Users = 'users'
}
