using Microsoft.EntityFrameworkCore;
using SupermarketSystem.Application.Common.Interfaces;
using SupermarketSystem.Domain.Inventory;
using SupermarketSystem.Domain.Sales;

namespace SupermarketSystem.Application.Common.Notifications;

/// <summary>
/// نصوص عربية موحّدة لرسائل التنبيهات - التنبيه بينعرض بصفحة التنبيهات وبتلغرام، فلازم يكون
/// مفهوم لصاحب المحل بلا أسماء enums إنجليزية ولا معرّفات GUID (كان "السبب: CashierError"
/// و"الفرع: 27f6...").
/// </summary>
public static class AlertText
{
    public static string VoidReason(VoidReason reason) => reason switch
    {
        Domain.Sales.VoidReason.CashierError => "خطأ كاشير",
        Domain.Sales.VoidReason.CustomerCancelled => "ألغاها الزبون",
        Domain.Sales.VoidReason.SystemError => "خطأ نظام",
        _ => "أخرى"
    };

    public static string ReturnReason(ReturnReason reason) => reason switch
    {
        Domain.Sales.ReturnReason.Defective => "تالف/معيب",
        Domain.Sales.ReturnReason.CustomerChangedMind => "غيّر رأيه",
        Domain.Sales.ReturnReason.WrongItem => "صنف غلط",
        Domain.Sales.ReturnReason.Expired => "منتهي الصلاحية",
        _ => "أخرى"
    };

    public static string WasteReason(WasteReason reason) => reason switch
    {
        Domain.Inventory.WasteReason.Expired => "منتهي الصلاحية",
        Domain.Inventory.WasteReason.Broken => "مكسور",
        Domain.Inventory.WasteReason.StorageDamage => "تلف تخزين",
        _ => "أخرى"
    };

    public static async Task<string> UserNameAsync(IApplicationDbContext context, Guid? userId, CancellationToken cancellationToken)
    {
        if (userId is null)
        {
            return "غير معروف";
        }

        var user = await context.Users.IgnoreQueryFilters().AsNoTracking()
            .Where(u => u.Id == userId)
            .Select(u => new { u.FullName, u.Username })
            .FirstOrDefaultAsync(cancellationToken);
        return user is null ? "غير معروف" : $"{user.FullName} ({user.Username})";
    }

    public static async Task<string> BranchNameAsync(IApplicationDbContext context, Guid branchId, CancellationToken cancellationToken) =>
        await context.Branches.IgnoreQueryFilters().AsNoTracking()
            .Where(b => b.Id == branchId)
            .Select(b => b.Name)
            .FirstOrDefaultAsync(cancellationToken)
        ?? "فرع غير معروف";

    public static async Task<string> ProductNameAsync(IApplicationDbContext context, Guid productId, CancellationToken cancellationToken) =>
        await context.Products.IgnoreQueryFilters().AsNoTracking()
            .Where(p => p.Id == productId)
            .Select(p => p.Name)
            .FirstOrDefaultAsync(cancellationToken)
        ?? "منتج غير معروف";
}
