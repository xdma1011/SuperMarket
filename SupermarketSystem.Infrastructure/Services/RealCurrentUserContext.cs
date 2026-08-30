using System.Linq;
using System.Security.Claims;
using Microsoft.AspNetCore.Http;
using SupermarketSystem.Application.Common.Interfaces;

namespace SupermarketSystem.Infrastructure.Services;

/// <summary>
/// ═══════════════════════════════════════════════════════════════════
/// قيد تصميم حاسم: ممنوع هذا الصنف يستعلم من قاعدة البيانات — أبدًا.
/// ═══════════════════════════════════════════════════════════════════
/// AppDbContext نفسه يستقبل ICurrentUserContext بمنشئه (ليبني المرشِّح
/// العام حسب الفرع). لو حاول هذا الصنف يستخدم AppDbContext ليجيب
/// صلاحيات المستخدم، بتصير اعتمادية دائرية: بناء AppDbContext محتاج
/// ICurrentUserContext، وICurrentUserContext محتاج AppDbContext.
/// الاعتماد الوحيد المسموح هون هو IHttpContextAccessor — قراءة مما هو
/// موجود أصلًا بالطلب (الـclaims)، لا أي مصدر خارجي.
///
/// لهذا السبب بالذات: IsCrossBranchAccessAllowed تُقرأ من claim محدَّد
/// وقت إصدار التوكن (باللوغن أو التجديد)، لا تُفحص حيًّا هون. الثمن
/// الموثَّق: سحب صلاحية "تجاوز الفروع" من مستخدم بتاخد لحد عمر التوكن
/// (15 دقيقة افتراضيًا) لتصير فعّالة — مقبول لأنها صلاحية نادرة التغيير،
/// ولأن الحالات العاجلة عندها الإيقاف الفوري للجلسة (RevokeSessionHandler).
///
/// طلب بلا توكن صالح = بلا هوية بالكامل. UserId وBranchId يرجعوا null،
/// وIsCrossBranchAccessAllowed ترجع false — يعني المرشِّح العام
/// (false || BranchId == null) بيرجّع صفر صفوف لأي كيان بفرع. هذا مقصود:
/// طلب مجهول الهوية ما لازم يشوف ولا صف بيانات بفرع، بغض النظر عن أي
/// فحص صلاحية إضافي بطبقة أعلى.
/// </summary>
public sealed class RealCurrentUserContext : ICurrentUserContext
{
    private readonly HttpContext? _httpContext;

    public RealCurrentUserContext(IHttpContextAccessor httpContextAccessor)
    {
        _httpContext = httpContextAccessor.HttpContext;
    }

    // "sub" fallback لأن JwtBearer الافتراضي بيحوّل claim "sub" لـ
    // ClaimTypes.NameIdentifier تلقائيًا (ما عطّلنا MapInboundClaims) —
    // الاثنان يُفحصان دفاعًا بالعمق، لا اعتمادًا على سلوك افتراضي وحيد.
    public Guid? UserId => TryGetGuidClaim(ClaimTypes.NameIdentifier) ?? TryGetGuidClaim("sub");

    public Guid? BranchId => TryGetGuidClaim(JwtTokenService.BranchIdClaim);

    public bool IsCrossBranchAccessAllowed
        => _httpContext?.User.FindFirst(JwtTokenService.CrossBranchClaim)?.Value == "true";

    // X-Forwarded-For أولًا: خلف أي reverse proxy (Nginx/IIS)،
    // Connection.RemoteIpAddress بيرجّع IP البروكسي نفسه ثابتًا لكل الطلبات
    // — بلا فحص الهيدر هذا، سجلات AuditLog كلها كانت رح تحمل نفس الـIP
    // بغض النظر عن العميل الحقيقي. أول قيمة بالهيدر (الأقرب للعميل الأصلي
    // بسلسلة بروكسيات متعددة) هي المعتمدة؛ الهيدر غير موجود = اتصال مباشر،
    // نرجع لـRemoteIpAddress.
    public string? IpAddress
    {
        get
        {
            if (_httpContext is null)
            {
                return null;
            }

            var forwardedFor = _httpContext.Request.Headers["X-Forwarded-For"].FirstOrDefault();
            if (!string.IsNullOrWhiteSpace(forwardedFor))
            {
                return forwardedFor.Split(',')[0].Trim();
            }

            return _httpContext.Connection.RemoteIpAddress?.ToString();
        }
    }

    // معرّف واحد يُولَّد أول مرة يُطلب بها خلال الطلب، ويُخزَّن بـHttpContext.Items
    // — كل سطور AuditLog الناتجة من نفس عملية SaveChanges (وحتى من أكتر من
    // SaveChanges بنفس الطلب) تحمل نفس القيمة. Guid جديد لا TraceIdentifier:
    // عمود CorrelationId بـAuditLog من النوع uniqueidentifier أصلًا، وشكل
    // TraceIdentifier الافتراضي بـASP.NET Core مو GUID قابل للتحويل مباشرة.
    private static readonly object CorrelationIdItemsKey = new();

    public Guid? CorrelationId
    {
        get
        {
            if (_httpContext is null)
            {
                return null;
            }

            if (_httpContext.Items[CorrelationIdItemsKey] is Guid existing)
            {
                return existing;
            }

            var generated = Guid.NewGuid();
            _httpContext.Items[CorrelationIdItemsKey] = generated;
            return generated;
        }
    }

    private Guid? TryGetGuidClaim(string claimType)
    {
        var value = _httpContext?.User.FindFirst(claimType)?.Value;
        return Guid.TryParse(value, out var parsed) ? parsed : null;
    }
}
