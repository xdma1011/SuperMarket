export type NavGroupId = 'main' | 'sales' | 'inventory' | 'purchasing' | 'finance' | 'system';

export interface NavItem {
  id: string;
  label: string;
  route: string;
  group: NavGroupId;
  icon: NavIconName;
  badge?: string;
  /** null = ظاهر لكل مستخدم مسجّل دخول، بغض النظر عن صلاحياته (الرئيسية مثلًا). */
  requiredPermission: string | null;
}

export interface NavGroup {
  id: NavGroupId;
  /** null = بلا عنوان (مجموعة "الرئيسية" لحالها فوق). */
  label: string | null;
}

/** ترتيب المجموعات بالقائمة - حسب تكرار الاستخدام اليومي: البيع أول، إعدادات النظام آخر. */
export const NAV_GROUPS: NavGroup[] = [
  { id: 'main', label: null },
  { id: 'sales', label: 'البيع' },
  { id: 'inventory', label: 'المخزون والكتالوج' },
  { id: 'purchasing', label: 'المشتريات' },
  { id: 'finance', label: 'المالية والرقابة' },
  { id: 'system', label: 'النظام' }
];

/** رمز الصلاحية يطابق PermissionCodes بالباك إند حرفيًا. */
export const NAV_ITEMS: NavItem[] = [
  { id: 'home', label: 'الرئيسية', route: '/', group: 'main', icon: 'home', requiredPermission: null },

  { id: 'sales', label: 'المبيعات', route: '/sales', group: 'sales', icon: 'receipt', requiredPermission: 'Sales.Create' },
  { id: 'orders', label: 'طلبات الزبائن', route: '/orders', group: 'sales', icon: 'bag', requiredPermission: 'Sales.Create' },
  { id: 'returns', label: 'الإرجاعات', route: '/returns', group: 'sales', icon: 'undo', requiredPermission: 'Returns.Process' },
  { id: 'cash-closings', label: 'تقفيل الصندوق', route: '/cash-closings', group: 'sales', icon: 'cash', requiredPermission: 'CashClosing.Manage' },
  { id: 'customers', label: 'الزبائن', route: '/customers', group: 'sales', icon: 'users', requiredPermission: 'Customers.Manage' },
  { id: 'driver', label: 'طلباتي (توصيل)', route: '/driver', group: 'sales', icon: 'truck', requiredPermission: 'Orders.Deliver' },

  { id: 'catalog', label: 'الكتالوج', route: '/catalog', group: 'inventory', icon: 'tag', requiredPermission: 'Catalog.Manage' },
  { id: 'current-stock', label: 'المخزون الحالي', route: '/current-stock', group: 'inventory', icon: 'boxes', requiredPermission: 'Reports.View' },
  { id: 'stocktake', label: 'الجرد', route: '/stocktakes', group: 'inventory', icon: 'clipboard', requiredPermission: 'Stocktake.Manage' },
  { id: 'stock-transfers', label: 'نقل المخزون', route: '/stock-transfers', group: 'inventory', icon: 'transfer', requiredPermission: 'StockTransfer.Manage' },
  { id: 'price-change-requests', label: 'طلبات تعديل السعر', route: '/price-change-requests', group: 'inventory', icon: 'percent', requiredPermission: 'Catalog.ChangePriceDirect' },
  { id: 'complimentary', label: 'الضيافة', route: '/complimentary', group: 'inventory', icon: 'cup', requiredPermission: 'Inventory.ComplimentaryIssue' },
  { id: 'waste', label: 'التلف والهلاك', route: '/waste', group: 'inventory', icon: 'trash', requiredPermission: 'Inventory.WasteIssue' },
  { id: 'units-of-measure', label: 'وحدات القياس', route: '/units-of-measure', group: 'inventory', icon: 'ruler', requiredPermission: 'Catalog.Manage' },

  { id: 'purchases', label: 'فواتير الشراء', route: '/purchases', group: 'purchasing', icon: 'invoice', requiredPermission: 'Purchasing.Create' },
  { id: 'suppliers', label: 'الموردين', route: '/suppliers', group: 'purchasing', icon: 'factory', requiredPermission: 'Suppliers.Manage' },
  { id: 'upload-invoice', label: 'رفع فاتورة (AI)', route: '/purchases/upload-invoice', group: 'purchasing', icon: 'upload', requiredPermission: 'Purchasing.CreateDraft' },
  { id: 'purchases-drafts', label: 'مسودات AI للمراجعة', route: '/purchases/drafts', group: 'purchasing', icon: 'sparkle', requiredPermission: 'Purchasing.Create' },

  { id: 'finance', label: 'المالية', route: '/finance', group: 'finance', icon: 'wallet', requiredPermission: 'Finance.Manage' },
  { id: 'reports', label: 'التقارير', route: '/reports', group: 'finance', icon: 'chart', requiredPermission: 'Reports.View' },
  { id: 'reviews', label: 'المراجعات', route: '/reviews', group: 'finance', icon: 'check', requiredPermission: 'Returns.Review' },
  { id: 'notifications', label: 'الإشعارات', route: '/notifications', group: 'finance', icon: 'bell', requiredPermission: 'Notifications.View' },

  { id: 'branches', label: 'الفروع', route: '/branches', group: 'system', icon: 'store', requiredPermission: 'Branches.Manage' },
  { id: 'users', label: 'المستخدمون', route: '/users', group: 'system', icon: 'user', requiredPermission: 'Users.Manage' },
  { id: 'sessions', label: 'الجلسات', route: '/sessions', group: 'system', icon: 'monitor', requiredPermission: 'Sessions.Manage' },
  { id: 'admin-settings', label: 'إعدادات حسّاسة', route: '/admin-settings', group: 'system', icon: 'sliders', requiredPermission: 'System.SettingsManage' },
  { id: 'admin-secrets', label: 'مفاتيح التكاملات', route: '/admin-secrets', group: 'system', icon: 'key', requiredPermission: 'System.SettingsManage' },
  { id: 'backup', label: 'النسخ الاحتياطي', route: '/backup', group: 'system', icon: 'database', requiredPermission: 'Backups.Manage' }
];

/**
 * أيقونات خطّية (viewBox 24، stroke) - كل أيقونة مجموعة مسارات `d` بس
 * (لا circle/rect)، عشان تنرسم بـ[attr.d] مباشرة بلا innerHTML (Angular
 * بيعقّم SVG بـinnerHTML وبيشيله).
 */
export const NAV_ICONS = {
  home: ['M3 10.5 12 3l9 7.5', 'M5 9v12h14V9', 'M10 21v-6h4v6'],
  receipt: ['M6 2h12v20l-3-2-3 2-3-2-3 2z', 'M9 7h6', 'M9 11h6', 'M9 15h4'],
  bag: ['M5 8h14l-1 13H6z', 'M9 8V6a3 3 0 0 1 6 0v2'],
  undo: ['M9 14 4 9l5-5', 'M4 9h11a5 5 0 0 1 0 10h-3'],
  cash: ['M2 6h20v12H2z', 'M12 9a3 3 0 1 0 0 6a3 3 0 1 0 0-6', 'M6 9v.01', 'M18 15v.01'],
  users: ['M9 11a4 4 0 1 0 0-8a4 4 0 1 0 0 8', 'M2 21v-1a6 6 0 0 1 12 0v1', 'M16 3.5a4 4 0 0 1 0 7', 'M18 14a6 6 0 0 1 4 6v1'],
  truck: ['M1 4h14v12H1z', 'M15 9h4l3 3v4h-7', 'M5.5 16a2 2 0 1 0 0 4a2 2 0 1 0 0-4', 'M18.5 16a2 2 0 1 0 0 4a2 2 0 1 0 0-4'],
  tag: ['M3 3h8l10 10-8 8L3 11z', 'M7.5 7.5v.01'],
  boxes: ['M3 7l9-4 9 4-9 4z', 'M3 7v10l9 4 9-4V7', 'M12 11v10'],
  clipboard: ['M9 3h6v3H9z', 'M8 4.5H5V21h14V4.5h-3', 'M9 12l2 2 4-4'],
  transfer: ['M4 8h16', 'M16 4l4 4-4 4', 'M20 16H4', 'M8 12l-4 4 4 4'],
  percent: ['M19 5 5 19', 'M6.5 4a2.5 2.5 0 1 0 0 5a2.5 2.5 0 1 0 0-5', 'M17.5 15a2.5 2.5 0 1 0 0 5a2.5 2.5 0 1 0 0-5'],
  cup: ['M4 8h13v5a6 6 0 0 1-6 6h-1a6 6 0 0 1-6-6z', 'M17 9h1.5a2.5 2.5 0 0 1 0 5H17', 'M8 2v3', 'M12 2v3'],
  trash: ['M3 6h18', 'M8 6V4h8v2', 'M5 6l1 15h12l1-15', 'M10 11v6', 'M14 11v6'],
  ruler: ['M3 17 17 3l4 4L7 21z', 'M7 13l2 2', 'M10 10l2 2', 'M13 7l2 2'],
  invoice: ['M6 2h9l4 4v16H6z', 'M14 2v5h5', 'M9 13h7', 'M9 17h7'],
  factory: ['M2 21V10l6 4V10l6 4V4h8v17z', 'M6 17h2', 'M12 17h2', 'M18 17h1'],
  upload: ['M12 16V4', 'M7 9l5-5 5 5', 'M4 16v4h16v-4'],
  sparkle: ['M12 3l2 6 6 2-6 2-2 6-2-6-6-2 6-2z', 'M19 3v4', 'M17 5h4'],
  wallet: ['M3 6h16v14H3z', 'M3 6l12-3v3', 'M15 11h6v5h-6z'],
  chart: ['M3 3v18h18', 'M7 16v-5', 'M12 16V7', 'M17 16v-8'],
  check: ['M12 22a10 10 0 1 0 0-20a10 10 0 1 0 0 20', 'M8 12l3 3 5-6'],
  bell: ['M6 9a6 6 0 0 1 12 0v4l2 3H4l2-3z', 'M10 19a2 2 0 0 0 4 0'],
  store: ['M3 9l2-6h14l2 6', 'M3 9h18v2a3 3 0 0 1-6 0 3 3 0 0 1-6 0 3 3 0 0 1-6 0z', 'M5 13v8h14v-8', 'M10 21v-5h4v5'],
  user: ['M12 12a4.5 4.5 0 1 0 0-9a4.5 4.5 0 1 0 0 9', 'M4 21v-1a8 8 0 0 1 16 0v1'],
  monitor: ['M2 4h20v13H2z', 'M8 21h8', 'M12 17v4'],
  sliders: ['M4 6h10', 'M18 6h2', 'M16 4v4', 'M4 12h4', 'M12 12h8', 'M10 10v4', 'M4 18h12', 'M20 18h0', 'M18 16v4'],
  key: ['M8 15a4 4 0 1 0 0-8a4 4 0 1 0 0 8', 'M11 11h10', 'M18 11v3', 'M21 11v2'],
  database: ['M4 5c0-1.7 3.6-3 8-3s8 1.3 8 3-3.6 3-8 3-8-1.3-8-3', 'M4 5v14c0 1.7 3.6 3 8 3s8-1.3 8-3V5', 'M4 12c0 1.7 3.6 3 8 3s8-1.3 8-3']
} satisfies Record<string, string[]>;

export type NavIconName = keyof typeof NAV_ICONS;
