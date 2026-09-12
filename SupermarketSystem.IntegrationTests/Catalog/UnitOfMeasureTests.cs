using Microsoft.Extensions.DependencyInjection;
using SupermarketSystem.Application.Catalog.CreateUnitOfMeasure;
using SupermarketSystem.Application.Catalog.GetUnitsOfMeasure;
using SupermarketSystem.Application.Catalog.SetUnitOfMeasureActive;
using SupermarketSystem.Application.Common.Results;
using Xunit;

namespace SupermarketSystem.IntegrationTests.Catalog;

/// <summary>
/// وحدات القياس المرجعية العامة (CreateUnitOfMeasure/GetUnitsOfMeasure/
/// SetUnitOfMeasureActive) - ميزة واحدة قريبة، ملف واحد.
///
/// ملاحظة مهمة: جدول UnitsOfMeasure مستثنى عمدًا من تصفير Respawn
/// بـDatabaseFixture (راجع TablesToIgnore) - أي صف يُنشأ هون بيبقى للأبد
/// حتى بين تشغيلات dotnet test منفصلة على نفس القاعدة. فأسماء هالاختبارات
/// لازم تكون فريدة بكل تشغيلة (Guid) لا نصوص ثابتة، وإلا تشغيلة تانية
/// رح تصطدم بـConflict "الاسم موجود أصلًا" من تشغيلة سابقة.
/// </summary>
[Collection(DatabaseCollection.Name)]
public sealed class UnitOfMeasureTests : IntegrationTestBase
{
    public UnitOfMeasureTests(DatabaseFixture fixture) : base(fixture) { }

    private static string UniqueName(string prefix) => $"{prefix}-{Guid.NewGuid():N}"[..30];

    [Fact]
    public async Task إنشاء_وحدة_قياس_جديدة_ينجح()
    {
        using var scope = CreateScope();
        var handler = scope.ServiceProvider.GetRequiredService<CreateUnitOfMeasureHandler>();
        var name = UniqueName("كيلوغرام");

        var result = await handler.HandleAsync(new CreateUnitOfMeasureCommand(name), CancellationToken.None);

        Assert.True(result.IsSuccess);
        Assert.Equal(name, result.Value.Name);
    }

    [Fact]
    public async Task إنشاء_وحدة_باسم_موجود_أصلًا_يفشل_بـConflict()
    {
        using var scope = CreateScope();
        var handler = scope.ServiceProvider.GetRequiredService<CreateUnitOfMeasureHandler>();
        var name = UniqueName("لتر");

        await handler.HandleAsync(new CreateUnitOfMeasureCommand(name), CancellationToken.None);
        var result = await handler.HandleAsync(new CreateUnitOfMeasureCommand(name), CancellationToken.None);

        Assert.True(result.IsFailure);
        Assert.Equal(ErrorType.Conflict, result.Error!.Type);
        Assert.Equal("UnitOfMeasure.AlreadyExists", result.Error.Code);
    }

    [Fact]
    public async Task إنشاء_وحدة_باسم_فاضي_يفشل_بخطأ_تحقق()
    {
        using var scope = CreateScope();
        var handler = scope.ServiceProvider.GetRequiredService<CreateUnitOfMeasureHandler>();

        var result = await handler.HandleAsync(new CreateUnitOfMeasureCommand("   "), CancellationToken.None);

        Assert.True(result.IsFailure);
        Assert.Equal(ErrorType.Validation, result.Error!.Type);
        Assert.Equal("UnitOfMeasure.NameRequired", result.Error.Code);
    }

    [Fact]
    public async Task إيقاف_وحدة_قياس_يخفيها_من_القائمة_الفعالة_فقط()
    {
        using var scope = CreateScope();
        var createHandler = scope.ServiceProvider.GetRequiredService<CreateUnitOfMeasureHandler>();
        var created = await createHandler.HandleAsync(new CreateUnitOfMeasureCommand(UniqueName("غرام")), CancellationToken.None);
        Assert.True(created.IsSuccess);

        var setActiveHandler = scope.ServiceProvider.GetRequiredService<SetUnitOfMeasureActiveHandler>();
        var deactivateResult = await setActiveHandler.HandleAsync(
            new SetUnitOfMeasureActiveCommand(created.Value.UnitOfMeasureId, false), CancellationToken.None);
        Assert.True(deactivateResult.IsSuccess);

        var getHandler = scope.ServiceProvider.GetRequiredService<GetUnitsOfMeasureHandler>();

        var activeOnly = await getHandler.HandleAsync(new GetUnitsOfMeasureQuery(ActiveOnly: true), CancellationToken.None);
        Assert.DoesNotContain(activeOnly, u => u.Id == created.Value.UnitOfMeasureId);

        var all = await getHandler.HandleAsync(new GetUnitsOfMeasureQuery(ActiveOnly: false), CancellationToken.None);
        var deactivated = Assert.Single(all, u => u.Id == created.Value.UnitOfMeasureId);
        Assert.False(deactivated.IsActive);
    }

    [Fact]
    public async Task إيقاف_وحدة_غير_موجودة_يفشل_بـNotFound()
    {
        using var scope = CreateScope();
        var handler = scope.ServiceProvider.GetRequiredService<SetUnitOfMeasureActiveHandler>();

        var result = await handler.HandleAsync(new SetUnitOfMeasureActiveCommand(Guid.NewGuid(), true), CancellationToken.None);

        Assert.True(result.IsFailure);
        Assert.Equal(ErrorType.NotFound, result.Error!.Type);
        Assert.Equal("UnitOfMeasure.NotFound", result.Error.Code);
    }
}
