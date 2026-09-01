using Microsoft.EntityFrameworkCore;
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

namespace SupermarketSystem.Application.Common.Interfaces;

/// <summary>
/// The persistence contract Application code depends on, implemented by
/// Infrastructure's AppDbContext (dependency inversion — Application never
/// references EF Core's DbContext type directly, only DbSet&lt;T&gt;/
/// SaveChangesAsync, which is why Application references the EF Core
/// abstractions package). This is the deliberate alternative to a generic
/// IRepository&lt;T&gt;: EF Core's DbSet/change-tracking already IS the
/// repository/unit-of-work, per the Architecture Review's explicit
/// instruction not to wrap it in another layer of abstraction.
///
/// No command/query handlers exist yet in Phase C — this interface exists
/// so Infrastructure has something concrete to implement now, ready for
/// Application-layer command/query handlers in a later phase.
/// </summary>
public interface IApplicationDbContext
{
    // Identity
    DbSet<User> Users { get; }
    DbSet<Role> Roles { get; }
    DbSet<Permission> Permissions { get; }
    DbSet<UserRole> UserRoles { get; }
    DbSet<RolePermission> RolePermissions { get; }
    DbSet<UserBranch> UserBranches { get; }
    DbSet<UserLoginLog> UserLoginLogs { get; }
    DbSet<UserDevice> UserDevices { get; }
    DbSet<UserSession> UserSessions { get; }

    // Branches
    DbSet<Branch> Branches { get; }

    // Catalog
    DbSet<Product> Products { get; }
    DbSet<ProductUnit> ProductUnits { get; }
    DbSet<ProductBarcode> ProductBarcodes { get; }
    DbSet<ProductCategory> ProductCategories { get; }
    DbSet<ProductBranch> ProductBranches { get; }

    // Inventory
    DbSet<ProductBatch> ProductBatches { get; }
    DbSet<StockMovement> StockMovements { get; }
    DbSet<Stock> Stocks { get; }
    DbSet<Stocktake> Stocktakes { get; }
    DbSet<StocktakeItem> StocktakeItems { get; }

    // Purchasing
    DbSet<Supplier> Suppliers { get; }
    DbSet<PurchaseInvoice> PurchaseInvoices { get; }
    DbSet<PurchaseInvoicePayment> PurchaseInvoicePayments { get; }
    DbSet<PurchaseInvoiceItem> PurchaseInvoiceItems { get; }
    DbSet<PurchaseInvoiceDraft> PurchaseInvoiceDrafts { get; }

    // Sales
    DbSet<SaleInvoice> SaleInvoices { get; }
    DbSet<SaleInvoiceItem> SaleInvoiceItems { get; }
    DbSet<SaleInvoicePayment> SaleInvoicePayments { get; }
    DbSet<SuspendedSale> SuspendedSales { get; }
    DbSet<Discount> Discounts { get; }
    DbSet<ReturnInvoice> ReturnInvoices { get; }
    DbSet<ReturnInvoiceItem> ReturnInvoiceItems { get; }
    DbSet<ReturnInvoicePayment> ReturnInvoicePayments { get; }

    // Payments
    DbSet<PaymentMethod> PaymentMethods { get; }

    // Cash Management
    DbSet<CashDrawerLog> CashDrawerLogs { get; }
    DbSet<CashClosing> CashClosings { get; }

    // Customers
    DbSet<Customer> Customers { get; }

    // Settings
    DbSet<SystemSetting> SystemSettings { get; }
    DbSet<UserSetting> UserSettings { get; }
    DbSet<NotificationSetting> NotificationSettings { get; }

    // Notifications
    DbSet<Notification> Notifications { get; }

    // Audit
    DbSet<AuditLog> AuditLogs { get; }

    // Common
    DbSet<BranchDocumentSequence> BranchDocumentSequences { get; }

    // Backups
    DbSet<DatabaseBackup> DatabaseBackups { get; }

    Task<int> SaveChangesAsync(CancellationToken cancellationToken);
}
