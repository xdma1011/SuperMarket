import { ReportsOperation } from '../../core/api/operations';

export type ColumnType = 'text' | 'number' | 'currency' | 'date' | 'enum' | 'boolean';

export interface ReportColumn {
  key: string;
  label: string;
  type: ColumnType;
  /** مفتاحها اسم العضو بالـC# ("Defective") - الباك إند بيسلسل الـenums كنصوص (JsonStringEnumConverter عام بـProgram.cs). */
  enumMap?: Record<string, string>;
}

export interface ReportConfig {
  id: string;
  title: string;
  operation: ReportsOperation;
  columns: ReportColumn[];
  requiresDateRange?: boolean;
  /** لتقارير تحتاج branchId إلزامي بالباك إند. */
  requiresBranch?: boolean;
}

// مطابقة لـReturnReason / VoidReason / WasteReason بالـDomain.
const returnReasonMap: Record<string, string> = {
  Defective: 'تالف/معيب',
  CustomerChangedMind: 'غيّر رأيه',
  WrongItem: 'صنف غلط',
  Expired: 'منتهي الصلاحية',
  Other: 'أخرى'
};

const voidReasonMap: Record<string, string> = {
  CashierError: 'خطأ كاشير',
  CustomerCancelled: 'ألغاها الزبون',
  SystemError: 'خطأ نظام',
  Other: 'أخرى'
};

const wasteReasonMap: Record<string, string> = {
  Expired: 'منتهي الصلاحية',
  Broken: 'مكسور',
  StorageDamage: 'تلف تخزين',
  Other: 'أخرى'
};

/**
 * القالب العام (ReportsComponent) بيقرأ من هالقائمة حصرًا — إضافة تقرير
 * جديد لاحقًا يعني سطر واحد هون. التقريران الخاصان (ملخّص المبيعات،
 * رأس المال) مُستثنيان عمدًا — شكلهم مختلف كليًا، معالجان بمكوّنات
 * منفصلة داخل نفس الصفحة.
 */
export const REPORT_CONFIGS: ReportConfig[] = [
  {
    id: 'recent-returns',
    title: 'الإرجاعات الأخيرة',
    operation: ReportsOperation.RecentReturns,
    requiresDateRange: true,
    columns: [
      { key: 'invoiceNumber', label: 'رقم الإرجاع', type: 'text' },
      { key: 'cashierUsername', label: 'الكاشير', type: 'text' },
      { key: 'reason', label: 'السبب', type: 'enum', enumMap: returnReasonMap },
      { key: 'totalAmount', label: 'الإجمالي', type: 'currency' },
      { key: 'totalRefundedAmount', label: 'المسترجَع', type: 'currency' },
      { key: 'createdAtUtc', label: 'التاريخ', type: 'date' }
    ]
  },
  {
    id: 'voided-sales',
    title: 'المبيعات الملغاة',
    operation: ReportsOperation.VoidedSales,
    requiresDateRange: true,
    columns: [
      { key: 'invoiceNumber', label: 'رقم الفاتورة', type: 'text' },
      { key: 'totalAmount', label: 'الإجمالي', type: 'currency' },
      { key: 'voidedByUsername', label: 'ألغاها', type: 'text' },
      { key: 'voidReason', label: 'السبب', type: 'enum', enumMap: voidReasonMap },
      { key: 'voidNotes', label: 'ملاحظات', type: 'text' },
      { key: 'voidedAtUtc', label: 'تاريخ الإلغاء', type: 'date' }
    ]
  },
  {
    id: 'return-frequency',
    title: 'تكرار الإرجاع حسب المنتج',
    operation: ReportsOperation.ReturnFrequencyByProduct,
    requiresDateRange: true,
    columns: [
      { key: 'productName', label: 'المنتج', type: 'text' },
      { key: 'returnCount', label: 'عدد مرات الإرجاع', type: 'number' },
      { key: 'totalQuantityReturned', label: 'الكمية المرتجعة', type: 'number' },
      { key: 'totalValueReturned', label: 'القيمة المرتجعة', type: 'currency' }
    ]
  },
  {
    id: 'manual-discounts',
    title: 'الخصومات اليدوية',
    operation: ReportsOperation.ManualDiscounts,
    requiresDateRange: true,
    columns: [
      { key: 'level', label: 'المستوى', type: 'text' },
      { key: 'invoiceNumber', label: 'رقم الفاتورة', type: 'text' },
      { key: 'discountAmount', label: 'قيمة الخصم', type: 'currency' },
      { key: 'createdAtUtc', label: 'التاريخ', type: 'date' }
    ]
  },
  {
    id: 'negative-stock',
    title: 'المخزون السالب',
    operation: ReportsOperation.NegativeStock,
    columns: [
      { key: 'productName', label: 'المنتج', type: 'text' },
      { key: 'quantityOnHand', label: 'الكمية الحالية', type: 'number' }
    ]
  },
  {
    id: 'best-cashiers',
    title: 'أفضل الكاشيرات',
    operation: ReportsOperation.BestCashiers,
    requiresDateRange: true,
    columns: [
      { key: 'cashierUsername', label: 'الكاشير', type: 'text' },
      { key: 'invoiceCount', label: 'عدد الفواتير', type: 'number' },
      { key: 'totalSales', label: 'إجمالي المبيعات', type: 'currency' }
    ]
  },
  {
    id: 'best-customers',
    title: 'أفضل الزبائن',
    operation: ReportsOperation.BestCustomers,
    requiresDateRange: true,
    columns: [
      { key: 'fullName', label: 'الاسم', type: 'text' },
      { key: 'phone', label: 'الهاتف', type: 'text' },
      { key: 'invoiceCount', label: 'عدد الفواتير', type: 'number' },
      { key: 'totalPurchases', label: 'إجمالي المشتريات', type: 'currency' }
    ]
  },
  {
    id: 'stagnant-products',
    title: 'الأصناف الراكدة',
    operation: ReportsOperation.StagnantProducts,
    requiresBranch: true,
    requiresDateRange: true,
    columns: [
      { key: 'productName', label: 'المنتج', type: 'text' },
      { key: 'sellingPrice', label: 'سعر البيع', type: 'currency' },
      { key: 'currentStock', label: 'المخزون الحالي', type: 'number' }
    ]
  },
  {
    id: 'reorder-needed',
    title: 'أصناف تحتاج إعادة طلب',
    operation: ReportsOperation.ReorderNeededProducts,
    requiresBranch: true,
    columns: [
      { key: 'productName', label: 'المنتج', type: 'text' },
      { key: 'currentStock', label: 'المخزون الحالي', type: 'number' },
      { key: 'minimumStock', label: 'الحد الأدنى', type: 'number' },
      { key: 'maximumStock', label: 'الحد الأقصى', type: 'number' }
    ]
  },
  {
    id: 'consumption-levels',
    title: 'مستوى الاستهلاك',
    operation: ReportsOperation.ConsumptionLevels,
    requiresBranch: true,
    requiresDateRange: true,
    columns: [
      { key: 'productName', label: 'المنتج', type: 'text' },
      { key: 'quantitySold', label: 'الكمية المباعة بالفترة', type: 'number' },
      { key: 'levelTitle', label: 'المستوى', type: 'text' }
    ]
  },
  {
    id: 'supplier-price-comparison',
    title: 'مقارنة أسعار الموردين',
    operation: ReportsOperation.SupplierPriceComparison,
    columns: [
      { key: 'supplierName', label: 'المورد', type: 'text' },
      { key: 'unitCost', label: 'تكلفة الوحدة', type: 'currency' },
      { key: 'quantity', label: 'الكمية', type: 'number' },
      { key: 'purchaseInvoiceNumber', label: 'رقم الفاتورة', type: 'text' },
      { key: 'purchasedAtUtc', label: 'تاريخ الشراء', type: 'date' }
    ]
  },
  {
    id: 'expiring-batches',
    title: 'قرب انتهاء الصلاحية',
    operation: ReportsOperation.ExpiringBatches,
    requiresBranch: true,
    columns: [
      { key: 'productName', label: 'المنتج', type: 'text' },
      { key: 'batchNumber', label: 'رقم الدفعة', type: 'text' },
      { key: 'expiryDate', label: 'تاريخ الانتهاء', type: 'date' },
      { key: 'daysRemaining', label: 'الأيام المتبقية', type: 'number' },
      { key: 'quantityOnHand', label: 'الكمية المتوفرة', type: 'number' }
    ]
  },
  {
    id: 'cashier-variance',
    title: 'فروقات تقفيل الصندوق لكل كاشير',
    operation: ReportsOperation.CashierVariance,
    requiresDateRange: true,
    columns: [
      { key: 'username', label: 'الكاشير', type: 'text' },
      { key: 'closingsCount', label: 'عدد التقفيلات', type: 'number' },
      { key: 'deficitCount', label: 'مرات العجز', type: 'number' },
      { key: 'surplusCount', label: 'مرات الزيادة', type: 'number' },
      { key: 'totalVariance', label: 'إجمالي الفرق', type: 'currency' },
      { key: 'averageVariance', label: 'متوسط الفرق', type: 'currency' }
    ]
  },
  {
    id: 'waste-log',
    title: 'سجل التلف والهلاك',
    operation: ReportsOperation.WasteLog,
    requiresBranch: true,
    columns: [
      { key: 'productName', label: 'المنتج', type: 'text' },
      { key: 'quantityBase', label: 'الكمية', type: 'number' },
      { key: 'reason', label: 'السبب', type: 'enum', enumMap: wasteReasonMap },
      { key: 'notes', label: 'ملاحظات', type: 'text' },
      { key: 'isReplacedBySupplier', label: 'مستبدَل من الشركة', type: 'boolean' },
      { key: 'occurredAtUtc', label: 'التاريخ', type: 'date' }
    ]
  },
  {
    id: 'supplier-payment-due',
    title: 'تنبيه استحقاق دفعة مورد',
    operation: ReportsOperation.SupplierPaymentDue,
    requiresBranch: true,
    columns: [
      { key: 'supplierName', label: 'المورد', type: 'text' },
      { key: 'invoiceNumber', label: 'رقم الفاتورة', type: 'text' },
      { key: 'remainingDebt', label: 'المتبقي', type: 'currency' },
      { key: 'dueDate', label: 'تاريخ الاستحقاق', type: 'date' },
      { key: 'daysRemaining', label: 'الأيام المتبقية', type: 'number' }
    ]
  },
  {
    id: 'recent-returned-items',
    title: 'آخر الأصناف المرتجعة',
    operation: ReportsOperation.RecentReturnedItems,
    requiresDateRange: true,
    columns: [
      { key: 'returnInvoiceNumber', label: 'رقم الإرجاع', type: 'text' },
      { key: 'productName', label: 'المنتج', type: 'text' },
      { key: 'quantity', label: 'الكمية', type: 'number' },
      { key: 'lineTotal', label: 'القيمة', type: 'currency' },
      { key: 'reason', label: 'السبب', type: 'enum', enumMap: returnReasonMap },
      { key: 'returnedAtUtc', label: 'التاريخ', type: 'date' }
    ]
  }
];
