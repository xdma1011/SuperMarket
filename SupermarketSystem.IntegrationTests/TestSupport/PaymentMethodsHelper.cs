using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using SupermarketSystem.Infrastructure.Persistence;

namespace SupermarketSystem.IntegrationTests;

/// <summary>
/// PaymentMethods مبذورة بالـmigrations نفسها ومستثناة من تصفير Respawn
/// (راجع DatabaseFixture.TablesToIgnore) - موجودة دومًا طول التشغيلة.
/// PaymentMethod ليست IBranchOwned، فقراءتها آمنة بلا أي هوية/فرع.
/// </summary>
internal static class PaymentMethodsHelper
{
    /// <summary>
    /// طريقة دفع فعّالة **ما بتتطلب** ExternalReference (زي "نقدي" -
    /// راجع PaymentMethod.RequiresExternalReference) - أبسط اختيار
    /// لاختبارات ما همّها اختبار هذا الشرط بالتحديد (لو أخذنا أي طريقة
    /// فعّالة عشوائية، ممكن تقع على "CliQ" مثلًا وترفض الدفعة بخطأ غير
    /// متعلّق بما يُختبَر أصلًا).
    /// </summary>
    public static async Task<Guid> GetAnyActivePaymentMethodIdAsync(IServiceProvider services)
    {
        using var scope = services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        return await db.PaymentMethods.AsNoTracking()
            .Where(p => p.IsActive && !p.RequiresExternalReference)
            .Select(p => p.Id)
            .FirstAsync();
    }
}
