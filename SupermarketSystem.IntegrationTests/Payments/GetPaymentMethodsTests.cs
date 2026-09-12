using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using SupermarketSystem.Application.Payments.GetPaymentMethods;
using SupermarketSystem.Infrastructure.Persistence;
using SupermarketSystem.IntegrationTests.Common;
using Xunit;

namespace SupermarketSystem.IntegrationTests.Payments;

/// <summary>GetPaymentMethodsHandler — يرجّع فقط الفعّالة (IsActive)، بترتيب أبجدي بالاسم.</summary>
[Collection(DatabaseCollection.Name)]
public sealed class GetPaymentMethodsTests : IntegrationTestBase
{
    public GetPaymentMethodsTests(DatabaseFixture fixture) : base(fixture) { }

    [Fact]
    public async Task جلب_طرق_الدفع_يرجّع_الطرق_المبذورة_الثلاثة_الفعّالة()
    {
        using var scope = CreateScope();
        var handler = scope.ServiceProvider.GetRequiredService<GetPaymentMethodsHandler>();

        var result = await handler.HandleAsync(CancellationToken.None);

        Assert.Equal(3, result.Count);
        Assert.Contains(result, m => m.Id == TestDataBuilder.CashPaymentMethodId && m.Name == "Cash");
        Assert.Contains(result, m => m.Id == TestDataBuilder.VisaPaymentMethodId && m.RequiresExternalReference);
    }

    [Fact]
    public async Task تعطيل_طريقة_دفع_يخفيها_من_القائمة()
    {
        // PaymentMethods مستثناة من تصفير Respawn (مبذورة عبر Migration
        // HasData وتُعامَل كمرجعية ثابتة - راجع DatabaseFixture.TablesToIgnore)،
        // فأي تعديل هون بيضل موجود عبر كل الاختبارات اللاحقة (وحتى تشغيلات
        // dotnet test منفصلة) ما لم يُعاد صراحة - لازم نعيدها Active بنهاية
        // الاختبار، وإلا اختبار "جلب طرق الدفع" (أو غيره) بيفشل لاحقًا بعدد
        // خاطئ من طرق الدفع الفعّالة.
        using var scope = CreateScope();
        var db = CreateDbContext(scope);
        var cliq = await db.PaymentMethods.FirstAsync(pm => pm.Id == TestDataBuilder.CliqPaymentMethodId);

        try
        {
            cliq.Deactivate();
            await db.SaveChangesAsync();

            var handler = scope.ServiceProvider.GetRequiredService<GetPaymentMethodsHandler>();
            var result = await handler.HandleAsync(CancellationToken.None);

            Assert.Equal(2, result.Count);
            Assert.DoesNotContain(result, m => m.Id == TestDataBuilder.CliqPaymentMethodId);
        }
        finally
        {
            cliq.Activate();
            await db.SaveChangesAsync();
        }
    }
}
