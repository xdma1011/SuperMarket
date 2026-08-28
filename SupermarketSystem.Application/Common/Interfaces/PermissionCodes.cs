namespace SupermarketSystem.Application.Common.Interfaces;

/// <summary>
/// رموز الصلاحيات كثوابت — لا نصوص سحرية متكررة بين الاستعلامات (LoginHandler،
/// RefreshTokenHandler)، seed الصلاحيات بقاعدة البيانات، وفلاتر الـendpoints.
/// </summary>
public static class PermissionCodes
{
    public const string CrossBranchAccess = "System.CrossBranchAccess";

    public const string SalesCreate = "Sales.Create";
    public const string SalesVoid = "Sales.Void";
    public const string ReturnsProcess = "Returns.Process";
    public const string ReturnsReview = "Returns.Review";
    public const string PurchasingCreate = "Purchasing.Create";
    public const string CatalogManage = "Catalog.Manage";
    public const string SuppliersManage = "Suppliers.Manage";
    public const string BranchesManage = "Branches.Manage";
    public const string StocktakeManage = "Stocktake.Manage";
    public const string StocktakeApprove = "Stocktake.Approve";
    public const string CashClosingManage = "CashClosing.Manage";
    public const string ReportsView = "Reports.View";
    public const string BackupsManage = "Backups.Manage";
    public const string SessionsManage = "Sessions.Manage";
    public const string NotificationsView = "Notifications.View";
    public const string UsersManage = "Users.Manage";
    public const string ComplimentaryIssue = "Inventory.ComplimentaryIssue";

    /// <summary>كل الرموز دفعة وحدة — يخدم seed دور "Master Admin" (كل الصلاحيات مربوطة فيه) بلا سرد يدوي معرَّض للنسيان عند إضافة رمز جديد لاحقًا.</summary>
    public static readonly IReadOnlyList<string> All = new[]
    {
        CrossBranchAccess, SalesCreate, SalesVoid, ReturnsProcess, ReturnsReview,
        PurchasingCreate, CatalogManage, SuppliersManage, BranchesManage,
        StocktakeManage, StocktakeApprove, CashClosingManage, ReportsView,
        BackupsManage, SessionsManage, NotificationsView, UsersManage, ComplimentaryIssue
    };

    /// <summary>صلاحيات دور "كاشير" — شغل البيع اليومي بس، بلا أي إدارة.</summary>
    public static readonly IReadOnlyList<string> CashierDefaults = new[]
    {
        SalesCreate, SalesVoid, ReturnsProcess, NotificationsView
    };

    /// <summary>
    /// صلاحيات دور "مساعد أدمن" — كل العمليات اليومية والإدارية العادية،
    /// باستثناء الصلاحيات الأخطر (نسخ احتياطي، إدارة جلسات، فروع، تجاوز
    /// الفروع، إدارة مستخدمين) — هذي تبقى حصرًا لـMaster Admin.
    /// </summary>
    public static readonly IReadOnlyList<string> AssistantAdminDefaults = new[]
    {
        SalesCreate, SalesVoid, ReturnsProcess, ReturnsReview, PurchasingCreate,
        CatalogManage, SuppliersManage, StocktakeManage, StocktakeApprove,
        CashClosingManage, ReportsView, NotificationsView
    };
}
