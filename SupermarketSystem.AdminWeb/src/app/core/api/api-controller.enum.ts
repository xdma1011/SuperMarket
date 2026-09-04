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
  Inventory = 'inventory',
  ProductCategories = 'product-categories',
  Products = 'products',
  PurchaseInvoices = 'purchase-invoices',
  Notifications = 'notifications',
  Orders = 'orders',
  PaymentMethods = 'payment-methods',
  Reports = 'reports',
  Returns = 'returns',
  Reviews = 'reviews',
  Sales = 'sales',
  Stocktakes = 'stocktakes',
  Suppliers = 'suppliers',
  System = 'system',
  Users = 'users'
}
