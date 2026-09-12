using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.DependencyInjection;
using SupermarketSystem.Infrastructure.Services;
using System.Security.Claims;

namespace SupermarketSystem.IntegrationTests;

/// <summary>
/// اختبار Handler مباشر (بلا HTTP) لأي Handler بيلمس كيان "Branch-owned"
/// (ProductBranch, Stock, SaleInvoice, PurchaseInvoice, ReturnInvoice, إلخ)
/// بيصطدم بمرشِّح الفروع العام بـAppDbContext (راجع
/// AppDbContext.ApplyGlobalQueryFilters): المرشِّح بيعتمد على
/// ICurrentUserContext.BranchId/IsCrossBranchAccessAllowed، واللي بدورهما
/// (RealCurrentUserContext) بيعتمدان حصرًا على IHttpContextAccessor.HttpContext
/// الحالي. بلا طلب HTTP فعلي، الاثنان null/false، فالمرشِّح بيرجّع صفر صفوف
/// دايمًا لأي كيان Branch-owned - حتى لو الصف فعليًا موجود بنفس الفرع
/// المطلوب.
///
/// هذا الصنف يحاكي "مستخدم Master Admin مسجَّل دخول بصلاحية تجاوز الفروع"
/// (نفس أثر CrossBranchAccess الحقيقي بتوكن JWT) بضبط IHttpContextAccessor
/// الحالي (Scoped Async-Local، مسجَّل Singleton فعليًا) *قبل* أول Resolve
/// لأي Handler/AppDbContext من نفس الـscope - AppDbContext بيقرأ
/// ICurrentUserContext مرة وحدة بمنشئه، فلازم يصير الضبط قبل إنشائه.
///
/// بلا لمس CustomWebApplicationFactory.cs أو DatabaseFixture.cs (ممنوعين) -
/// هذا حل محصور بملفات الاختبار نفسها فقط.
/// </summary>
internal static class TestAuthContext
{
    /// <summary>يفعّل تجاوز فحص الفرع بهذا الـscope - لا يضبط هوية مستخدم حقيقية (CreatedByUserId بيضل null، نفس سلوك PlaceholderCurrentUserContext التوثيقي).</summary>
    public static void ActAsCrossBranchUser(this IServiceScope scope)
    {
        var accessor = scope.ServiceProvider.GetRequiredService<IHttpContextAccessor>();
        var identity = new ClaimsIdentity(new[]
        {
            new Claim(JwtTokenService.CrossBranchClaim, "true")
        });

        accessor.HttpContext = new DefaultHttpContext { User = new ClaimsPrincipal(identity) };
    }
}
