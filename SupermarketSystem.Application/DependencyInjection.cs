using Microsoft.Extensions.DependencyInjection;
using SupermarketSystem.Application.Authentication.Login;
using SupermarketSystem.Application.Authentication.GetActiveSessions;
using SupermarketSystem.Application.Authentication.GetMyPermissions;
using SupermarketSystem.Application.Authentication.Logout;
using SupermarketSystem.Application.Authentication.RefreshToken;
using SupermarketSystem.Application.Authentication.RevokeSession;
using SupermarketSystem.Application.Backups.DeleteBackup;
using SupermarketSystem.Application.System.BootstrapAdmin;
using SupermarketSystem.Application.System.GetAdminSettings;
using SupermarketSystem.Application.System.UpdateAdminSetting;
using SupermarketSystem.Application.Catalog.UpdateProduct;
using SupermarketSystem.Application.Catalog.UpdateProductCategory;
using SupermarketSystem.Application.Purchasing.UpdateSupplier;
using SupermarketSystem.Application.Users.CreateUser;
using SupermarketSystem.Application.Users.UpdateUser;
using SupermarketSystem.Application.Users.GetRoles;
using SupermarketSystem.Application.Users.GetUsers;
using SupermarketSystem.Application.Backups.GetBackupById;
using SupermarketSystem.Application.Backups.GetBackups;
using SupermarketSystem.Application.Backups.TriggerBackup;
using SupermarketSystem.Application.Branches.CreateBranch;
using SupermarketSystem.Application.Branches.GetPublicBranches;
using SupermarketSystem.Application.Inventory.ApproveStocktake;
using SupermarketSystem.Application.Inventory.GetCurrentStock;
using SupermarketSystem.Application.Inventory.RecordComplimentaryIssue;
using SupermarketSystem.Application.Inventory.CompleteStocktake;
using SupermarketSystem.Application.Inventory.CreateStocktake;
using SupermarketSystem.Application.Inventory.GetStocktakeById;
using SupermarketSystem.Application.Inventory.GetStocktakes;
using SupermarketSystem.Application.Inventory.RecordStocktakeCount;
using SupermarketSystem.Application.Common.Interfaces;
using SupermarketSystem.Application.Common.Notifications;
using SupermarketSystem.Application.Common.Services;
using SupermarketSystem.Application.CashManagement.CompleteCashClosing;
using SupermarketSystem.Application.Branches.GetBranches;
using SupermarketSystem.Application.Catalog.CreateProduct;
using SupermarketSystem.Application.Catalog.CreateProductBranch;
using SupermarketSystem.Application.Catalog.AddProductUnit;
using SupermarketSystem.Application.Catalog.GetProductBranches;
using SupermarketSystem.Application.Catalog.GetProductByBarcode;
using SupermarketSystem.Application.Catalog.GetProductUnits;
using SupermarketSystem.Application.Catalog.SetProductComplimentaryAllowed;
using SupermarketSystem.Application.Catalog.CreateProductCategory;
using SupermarketSystem.Application.Catalog.GetProductCategories;
using SupermarketSystem.Application.Catalog.GetProducts;
using SupermarketSystem.Application.Payments.GetPaymentMethods;
using SupermarketSystem.Application.Purchasing.CompletePurchaseInvoice;
using SupermarketSystem.Application.Purchasing.GetPurchaseInvoices;
using SupermarketSystem.Application.Purchasing.CreateSupplier;
using SupermarketSystem.Application.Purchasing.GetSupplierDebts;
using SupermarketSystem.Application.Purchasing.GetSuppliers;
using SupermarketSystem.Application.Purchasing.PurchaseInvoiceDrafts;
using SupermarketSystem.Application.Purchasing.RecordPurchaseInvoicePayment;
using SupermarketSystem.Application.Reviews.GetPendingReviews;
using SupermarketSystem.Application.Reviews.MarkPurchaseInvoiceItemReviewed;
using SupermarketSystem.Application.Reviews.MarkStockMovementReviewed;
using SupermarketSystem.Application.Reporting.GetBestCashiers;
using SupermarketSystem.Application.Reporting.GetCurrentCapitalValue;
using SupermarketSystem.Application.Reporting.GetBestCustomers;
using SupermarketSystem.Application.Reporting.GetManualDiscounts;
using SupermarketSystem.Application.Reporting.GetNegativeStock;
using SupermarketSystem.Application.Sales.CompleteSale;
using SupermarketSystem.Application.Sales.GetSaleInvoiceById;
using SupermarketSystem.Application.Sales.GetSaleInvoices;
using SupermarketSystem.Application.Sales.MarkReturnReviewed;
using SupermarketSystem.Application.Sales.ProcessReturn;
using SupermarketSystem.Application.Sales.VoidSale;
using SupermarketSystem.Application.Reporting.GetRecentReturns;
using SupermarketSystem.Application.Reporting.GetRecentReturnedItems;
using SupermarketSystem.Application.Reporting.GetReorderNeededProducts;
using SupermarketSystem.Application.Reporting.GetReturnFrequencyByProduct;
using SupermarketSystem.Application.Reporting.GetSalesSummary;
using SupermarketSystem.Application.CashierSync.GetCatalogSyncPage;
using SupermarketSystem.Application.CashierSync.GetCatalogVersion;
using SupermarketSystem.Application.Reporting.GetProductConsumptionLevels;
using SupermarketSystem.Application.Reporting.GetStagnantProducts;
using SupermarketSystem.Application.Reporting.GetSupplierPriceComparison;
using SupermarketSystem.Application.Reporting.GetVoidedSales;
using SupermarketSystem.Application.Notifications.GetNotifications;

namespace SupermarketSystem.Application;

public static class DependencyInjection
{
    /// <summary>
    /// Handlers are registered explicitly rather than via assembly scanning
    /// or a mediator pipeline. With no MediatR in the solution (brief §23:
    /// "do not implement unnecessary CQRS complexity"), an endpoint injects
    /// the handler it needs directly — one fewer indirection, and the call
    /// path from HTTP to database stays traceable in a single stack trace.
    ///
    /// If the handler count grows large enough that this list becomes
    /// tedious, that is the point to reconsider scanning — not before.
    /// </summary>
    public static IServiceCollection AddApplication(this IServiceCollection services)
    {
        services.AddScoped<IInvoiceExtractionService, FallbackInvoiceOcrService>();

        services.AddScoped<LoginHandler>();
        services.AddScoped<GetMyPermissionsHandler>();
        services.AddScoped<BootstrapAdminHandler>();
        services.AddScoped<GetAdminSettingsHandler>();
        services.AddScoped<UpdateAdminSettingHandler>();
        services.AddScoped<CreateUserHandler>();
        services.AddScoped<UpdateUserHandler>();
        services.AddScoped<UpdateSupplierHandler>();
        services.AddScoped<UpdateProductHandler>();
        services.AddScoped<UpdateProductCategoryHandler>();
        services.AddScoped<GetUsersHandler>();
        services.AddScoped<GetRolesHandler>();
        services.AddScoped<RefreshTokenHandler>();
        services.AddScoped<LogoutHandler>();
        services.AddScoped<RevokeSessionHandler>();
        services.AddScoped<GetActiveSessionsHandler>();

        services.AddScoped<CreateBranchHandler>();
        services.AddScoped<GetPublicBranchesHandler>();
        services.AddScoped<GetBranchesHandler>();

        services.AddScoped<CreateProductCategoryHandler>();
        services.AddScoped<GetProductCategoriesHandler>();
        services.AddScoped<CreateProductHandler>();
        services.AddScoped<GetProductsHandler>();
        services.AddScoped<CreateProductBranchHandler>();
        services.AddScoped<GetProductUnitsHandler>();
        services.AddScoped<GetProductByBarcodeHandler>();
        services.AddScoped<GetProductBranchesHandler>();
        services.AddScoped<AddProductUnitHandler>();
        services.AddScoped<SetProductComplimentaryAllowedHandler>();

        services.AddScoped<CreateSupplierHandler>();
        services.AddScoped<GetSuppliersHandler>();
        services.AddScoped<GetSupplierDebtsHandler>();
        services.AddScoped<RecordPurchaseInvoicePaymentHandler>();
        services.AddScoped<GetPendingReviewsHandler>();
        services.AddScoped<MarkStockMovementReviewedHandler>();
        services.AddScoped<MarkPurchaseInvoiceItemReviewedHandler>();
        services.AddScoped<CompletePurchaseInvoiceHandler>();
        services.AddScoped<GetPaymentMethodsHandler>();
        services.AddScoped<GetPurchaseInvoicesHandler>();
        services.AddScoped<CreatePurchaseInvoiceDraftFromImageHandler>();
        services.AddScoped<GetPurchaseInvoiceDraftsHandler>();
        services.AddScoped<GetPurchaseInvoiceDraftByIdHandler>();
        services.AddScoped<UpdatePurchaseInvoiceDraftHandler>();
        services.AddScoped<CompletePurchaseInvoiceDraftHandler>();
        services.AddScoped<DiscardPurchaseInvoiceDraftHandler>();
        services.AddScoped<GetPurchaseInvoiceDraftImagePathHandler>();

        services.AddScoped<CompleteSaleHandler>();
        services.AddScoped<VoidSaleHandler>();
        services.AddScoped<GetSaleInvoicesHandler>();
        services.AddScoped<GetSaleInvoiceByIdHandler>();
        services.AddScoped<ProcessReturnHandler>();
        services.AddScoped<MarkReturnReviewedHandler>();

        services.AddScoped<CompleteCashClosingHandler>();

        services.AddScoped<CreateStocktakeHandler>();
        services.AddScoped<RecordStocktakeCountHandler>();
        services.AddScoped<CompleteStocktakeHandler>();
        services.AddScoped<ApproveStocktakeHandler>();
        services.AddScoped<RecordComplimentaryIssueHandler>();
        services.AddScoped<GetCurrentStockHandler>();
        services.AddScoped<GetStocktakeByIdHandler>();
        services.AddScoped<GetStocktakesHandler>();

        services.AddScoped<GetRecentReturnsHandler>();
        services.AddScoped<GetVoidedSalesHandler>();
        services.AddScoped<GetReturnFrequencyByProductHandler>();
        services.AddScoped<GetManualDiscountsHandler>();
        services.AddScoped<GetNegativeStockHandler>();
        services.AddScoped<GetSalesSummaryHandler>();
        services.AddScoped<GetBestCashiersHandler>();
        services.AddScoped<GetBestCustomersHandler>();
        services.AddScoped<GetStagnantProductsHandler>();
        services.AddScoped<GetProductConsumptionLevelsHandler>();
        services.AddScoped<GetCatalogVersionHandler>();
        services.AddScoped<GetCatalogSyncPageHandler>();
        services.AddScoped<GetReorderNeededProductsHandler>();
        services.AddScoped<GetSupplierPriceComparisonHandler>();
        services.AddScoped<GetRecentReturnedItemsHandler>();
        services.AddScoped<GetCurrentCapitalValueHandler>();
        services.AddScoped<GetNotificationsHandler>();

        services.AddScoped<TriggerBackupHandler>();
        services.AddScoped<GetBackupsHandler>();
        services.AddScoped<GetBackupByIdHandler>();
        services.AddScoped<DeleteBackupHandler>();

        services.AddScoped<INotificationDispatcher, NotificationDispatcher>();

        return services;
    }
}
