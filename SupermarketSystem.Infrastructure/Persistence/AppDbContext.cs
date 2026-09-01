using System.Reflection;
using Microsoft.EntityFrameworkCore;
using SupermarketSystem.Application.Common.Interfaces;
using SupermarketSystem.Domain.Audit;
using SupermarketSystem.Domain.Backups;
using SupermarketSystem.Domain.Branches;
using SupermarketSystem.Domain.CashManagement;
using SupermarketSystem.Domain.Catalog;
using SupermarketSystem.Domain.Common;
using SupermarketSystem.Domain.Customers;
using SupermarketSystem.Domain.Identity;
using SupermarketSystem.Domain.Inventory;
using SupermarketSystem.Domain.Notifications;
using SupermarketSystem.Domain.Payments;
using SupermarketSystem.Domain.Purchasing;
using SupermarketSystem.Domain.Sales;
using SupermarketSystem.Domain.Settings;

namespace SupermarketSystem.Infrastructure.Persistence;

/// <summary>
/// Implements IApplicationDbContext (dependency inversion — Application
/// defines the contract, this is the one and only implementation).
///
/// Two reflection-driven global query filter passes run in
/// OnModelCreating: every IBranchOwned entity gets a branch filter, every
/// ISoftDeletable entity gets a not-deleted filter. This is exactly what
/// Architecture Review §9 asked for — nobody has to remember to add a
/// per-entity filter — with the explicit caveat repeated there: these
/// filters are NOT the authorization mechanism by themselves.
/// Application-layer branch-access checks (against UserBranch) are what
/// actually stop a user from writing to a branch they can't reach; the
/// filter only shapes what a query returns.
/// </summary>
public class AppDbContext : DbContext, IApplicationDbContext
{
    private readonly Guid? _currentBranchId;
    private readonly bool _isCrossBranchAccessAllowed;

    public AppDbContext(DbContextOptions<AppDbContext> options, ICurrentUserContext currentUserContext)
        : base(options)
    {
        _currentBranchId = currentUserContext.BranchId;
        _isCrossBranchAccessAllowed = currentUserContext.IsCrossBranchAccessAllowed;
    }

    // Identity
    public DbSet<User> Users => Set<User>();
    public DbSet<Role> Roles => Set<Role>();
    public DbSet<Permission> Permissions => Set<Permission>();
    public DbSet<UserRole> UserRoles => Set<UserRole>();
    public DbSet<RolePermission> RolePermissions => Set<RolePermission>();
    public DbSet<UserBranch> UserBranches => Set<UserBranch>();
    public DbSet<UserLoginLog> UserLoginLogs => Set<UserLoginLog>();
    public DbSet<UserDevice> UserDevices => Set<UserDevice>();
    public DbSet<UserSession> UserSessions => Set<UserSession>();

    // Branches
    public DbSet<Branch> Branches => Set<Branch>();

    // Catalog
    public DbSet<Product> Products => Set<Product>();
    public DbSet<ProductUnit> ProductUnits => Set<ProductUnit>();
    public DbSet<ProductBarcode> ProductBarcodes => Set<ProductBarcode>();
    public DbSet<ProductCategory> ProductCategories => Set<ProductCategory>();
    public DbSet<ProductBranch> ProductBranches => Set<ProductBranch>();

    // Inventory
    public DbSet<ProductBatch> ProductBatches => Set<ProductBatch>();
    public DbSet<StockMovement> StockMovements => Set<StockMovement>();
    public DbSet<Stock> Stocks => Set<Stock>();
    public DbSet<Stocktake> Stocktakes => Set<Stocktake>();
    public DbSet<StocktakeItem> StocktakeItems => Set<StocktakeItem>();

    // Purchasing
    public DbSet<Supplier> Suppliers => Set<Supplier>();
    public DbSet<PurchaseInvoice> PurchaseInvoices => Set<PurchaseInvoice>();
    public DbSet<PurchaseInvoicePayment> PurchaseInvoicePayments => Set<PurchaseInvoicePayment>();
    public DbSet<PurchaseInvoiceItem> PurchaseInvoiceItems => Set<PurchaseInvoiceItem>();
    public DbSet<PurchaseInvoiceDraft> PurchaseInvoiceDrafts => Set<PurchaseInvoiceDraft>();

    // Sales
    public DbSet<SaleInvoice> SaleInvoices => Set<SaleInvoice>();
    public DbSet<SaleInvoiceItem> SaleInvoiceItems => Set<SaleInvoiceItem>();
    public DbSet<SaleInvoicePayment> SaleInvoicePayments => Set<SaleInvoicePayment>();
    public DbSet<SuspendedSale> SuspendedSales => Set<SuspendedSale>();
    public DbSet<Discount> Discounts => Set<Discount>();
    public DbSet<ReturnInvoice> ReturnInvoices => Set<ReturnInvoice>();
    public DbSet<ReturnInvoiceItem> ReturnInvoiceItems => Set<ReturnInvoiceItem>();
    public DbSet<ReturnInvoicePayment> ReturnInvoicePayments => Set<ReturnInvoicePayment>();

    // Payments
    public DbSet<PaymentMethod> PaymentMethods => Set<PaymentMethod>();

    // Cash Management
    public DbSet<CashDrawerLog> CashDrawerLogs => Set<CashDrawerLog>();
    public DbSet<CashClosing> CashClosings => Set<CashClosing>();

    // Customers
    public DbSet<Customer> Customers => Set<Customer>();

    // Settings
    public DbSet<SystemSetting> SystemSettings => Set<SystemSetting>();
    public DbSet<UserSetting> UserSettings => Set<UserSetting>();
    public DbSet<NotificationSetting> NotificationSettings => Set<NotificationSetting>();

    // Notifications
    public DbSet<Notification> Notifications => Set<Notification>();

    // Audit
    public DbSet<AuditLog> AuditLogs => Set<AuditLog>();

    // Common
    public DbSet<BranchDocumentSequence> BranchDocumentSequences => Set<BranchDocumentSequence>();

    // Backups
    public DbSet<DatabaseBackup> DatabaseBackups => Set<DatabaseBackup>();

    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        base.OnModelCreating(modelBuilder);

        modelBuilder.ApplyConfigurationsFromAssembly(Assembly.GetExecutingAssembly());

        ApplyGlobalQueryFilters(modelBuilder);
    }

    /// <summary>
    /// One reflection pass, two filter kinds. Each entity type gets at most
    /// one HasQueryFilter call — if a future entity ever implements both
    /// IBranchOwned and ISoftDeletable, the two conditions must be combined
    /// into a single filter expression here, since EF Core only honours the
    /// last HasQueryFilter call per entity type. No entity in the current
    /// model implements both, so this is safe as written.
    /// </summary>
    private void ApplyGlobalQueryFilters(ModelBuilder modelBuilder)
    {
        foreach (var entityType in modelBuilder.Model.GetEntityTypes())
        {
            var clrType = entityType.ClrType;

            if (typeof(IBranchOwned).IsAssignableFrom(clrType))
            {
                typeof(AppDbContext)
                    .GetMethod(nameof(SetBranchFilter), BindingFlags.NonPublic | BindingFlags.Instance)!
                    .MakeGenericMethod(clrType)
                    .Invoke(this, new object[] { modelBuilder });
            }
            else if (typeof(ISoftDeletable).IsAssignableFrom(clrType))
            {
                typeof(AppDbContext)
                    .GetMethod(nameof(SetSoftDeleteFilter), BindingFlags.NonPublic | BindingFlags.Instance)!
                    .MakeGenericMethod(clrType)
                    .Invoke(this, new object[] { modelBuilder });
            }
        }
    }

    private void SetBranchFilter<TEntity>(ModelBuilder modelBuilder)
        where TEntity : class, IBranchOwned
    {
        modelBuilder.Entity<TEntity>()
            .HasQueryFilter(e => _isCrossBranchAccessAllowed || e.BranchId == _currentBranchId);
    }

    private void SetSoftDeleteFilter<TEntity>(ModelBuilder modelBuilder)
        where TEntity : class, ISoftDeletable
    {
        modelBuilder.Entity<TEntity>().HasQueryFilter(e => !e.IsDeleted);
    }
}
