using System.Net;
using Microsoft.Extensions.DependencyInjection;
using SupermarketSystem.Application.Common.Results;
using SupermarketSystem.Application.System.BootstrapAdmin;
using Xunit;

namespace SupermarketSystem.IntegrationTests.SystemDomain;

/// <summary>
/// ⚠️ ملاحظة تغطية: حالة النجاح (نظام فارغ تمامًا، صفر مستخدمين) **غير
/// قابلة للاختبار هون عمدًا** - DatabaseFixture بيبذر مستخدم Master Admin
/// ثابت (test.admin) طول التشغيلة، ومستثنى صراحة من تصفير Respawn بين
/// الاختبارات (TablesToIgnore يشمل "Users") لأن كل اختبار آخر بالتشغيلة
/// محتاجه لتسجيل الدخول. يعني BootstrapAdminHandler.HandleAsync هون
/// بيلاقي دومًا مستخدمًا موجودًا فعليًا، فهذا الاختبار يوثّق فرع الفشل
/// (Conflict) فقط - وهو فعليًا الفرع الوحيد القابل للوصول بهذه البيئة
/// المشتركة. حالة النجاح مغطّاة ضمنيًا بمنطق DatabaseFixture.SeedFixedDataAsync
/// نفسه (اللي بيكرّر نفس تسلسل الإنشاء يدويًا - فرع + مستخدم + دور).
/// </summary>
[Collection(DatabaseCollection.Name)]
public sealed class BootstrapAdminTests : IntegrationTestBase
{
    public BootstrapAdminTests(DatabaseFixture fixture) : base(fixture) { }

    [Fact]
    public async Task التمهيد_على_نظام_فيه_مستخدم_أصلًا_يفشل_بـConflict()
    {
        using var scope = CreateScope();
        var handler = scope.ServiceProvider.GetRequiredService<BootstrapAdminHandler>();

        var result = await handler.HandleAsync(CancellationToken.None);

        Assert.True(result.IsFailure);
        Assert.Equal(ErrorType.Conflict, result.Error!.Type);
        Assert.Equal("System.AlreadyBootstrapped", result.Error.Code);
    }

    [Fact]
    public async Task endpoint_التمهيد_مسموح_بلا_توكن_ويرجع_409_فعليًا()
    {
        var client = CreateAnonymousClient();
        var response = await client.PostAsync("/api/v1/system/bootstrap-admin", content: null);
        Assert.Equal(HttpStatusCode.Conflict, response.StatusCode);
    }
}
