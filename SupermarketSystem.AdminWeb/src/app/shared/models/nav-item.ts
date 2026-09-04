export interface NavItem {
  id: string;
  label: string;
  route: string;
  badge?: string;
  /** null = ظاهر لكل مستخدم مسجّل دخول، بغض النظر عن صلاحياته (الرئيسية مثلًا). */
  requiredPermission: string | null;
}

/** يطابق NAV بتصميم Claude Design حرفيًا - نفس الترتيب، نفس الأسماء. رمز الصلاحية يطابق PermissionCodes بالباك إند حرفيًا. */
export const NAV_ITEMS: NavItem[] = [
  { id: 'home', label: 'الرئيسية', route: '/', requiredPermission: null },
  { id: 'sales', label: 'المبيعات', route: '/sales', requiredPermission: 'Sales.Create' },
  { id: 'orders', label: 'طلبات الزبائن', route: '/orders', requiredPermission: 'Sales.Create' },
  { id: 'returns', label: 'الإرجاعات', route: '/returns', requiredPermission: 'Returns.Process' },
  { id: 'reviews', label: 'المراجعات', route: '/reviews', requiredPermission: 'Returns.Review' },
  { id: 'purchases', label: 'المشتريات', route: '/purchases', requiredPermission: 'Purchasing.Create' },
  { id: 'purchases-drafts', label: 'مسودات AI للمراجعة', route: '/purchases/drafts', requiredPermission: 'Purchasing.Create' },
  { id: 'upload-invoice', label: 'رفع فاتورة (AI)', route: '/purchases/upload-invoice', requiredPermission: 'Purchasing.CreateDraft' },
  { id: 'complimentary', label: 'الضيافة', route: '/complimentary', requiredPermission: 'Inventory.ComplimentaryIssue' },
  { id: 'catalog', label: 'الكتالوج', route: '/catalog', requiredPermission: 'Catalog.Manage' },
  { id: 'current-stock', label: 'المخزون الحالي', route: '/current-stock', requiredPermission: 'Reports.View' },
  { id: 'suppliers', label: 'الموردين', route: '/suppliers', requiredPermission: 'Suppliers.Manage' },
  { id: 'stocktake', label: 'الجرد', route: '/stocktakes', requiredPermission: 'Stocktake.Manage' },
  { id: 'reports', label: 'التقارير', route: '/reports', requiredPermission: 'Reports.View' },
  { id: 'backup', label: 'النسخ الاحتياطي', route: '/backup', requiredPermission: 'Backups.Manage' },
  { id: 'notifications', label: 'الإشعارات', route: '/notifications', badge: '5', requiredPermission: 'Notifications.View' },
  { id: 'users', label: 'المستخدمون', route: '/users', requiredPermission: 'Users.Manage' },
  { id: 'sessions', label: 'الجلسات', route: '/sessions', requiredPermission: 'Sessions.Manage' },
  { id: 'admin-settings', label: 'إعدادات حسّاسة', route: '/admin-settings', requiredPermission: 'System.SettingsManage' }
];
