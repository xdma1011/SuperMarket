using Microsoft.Extensions.DependencyInjection;
using SupermarketSystem.Infrastructure.Persistence;

namespace SupermarketSystem.IntegrationTests;

/// <summary>
/// أساس مشترك لكل اختبار integration. كل اختبار (test method) بيصفّر
/// قاعدة البيانات التجارية قبل ما يبلش (IAsyncLifetime.InitializeAsync) —
/// عزل كامل بين الاختبارات بلا الحاجة لكل اختبار يفكّر بتنظيف بياناته
/// بنفسه، وبلا إعادة تشغيل migrations من الصفر (بطيء ومش لازم).
///
/// [Collection(DatabaseCollection.Name)] لازم تُكرَّر على كل صنف اختبار
/// يرث من هاد (xUnit ما بيورّث سمات الأصناف) — راجع أي اختبار مثال هون.
/// </summary>
public abstract class IntegrationTestBase : IAsyncLifetime
{
    protected readonly DatabaseFixture Fixture;

    protected IntegrationTestBase(DatabaseFixture fixture)
    {
        Fixture = fixture;
    }

    public virtual async Task InitializeAsync()
    {
        await Fixture.ResetDatabaseAsync();
    }

    public virtual Task DisposeAsync() => Task.CompletedTask;

    /// <summary>سكوب DI جديد بخدمات الـAPI الحقيقية — لاختبار Handler مباشرة بلا HTTP.</summary>
    protected IServiceScope CreateScope() => Fixture.Factory.Services.CreateScope();

    protected AppDbContext CreateDbContext(IServiceScope scope) =>
        scope.ServiceProvider.GetRequiredService<AppDbContext>();

    protected Task<HttpClient> CreateAuthenticatedClientAsync() => Fixture.CreateAuthenticatedClientAsync();

    protected HttpClient CreateAnonymousClient() => Fixture.Factory.CreateClient();
}
