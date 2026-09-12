using Microsoft.Extensions.DependencyInjection;
using SupermarketSystem.Application.Common.Results;
using SupermarketSystem.Application.System.GetAdminSettings;
using SupermarketSystem.Application.System.UpdateAdminSetting;
using Xunit;

namespace SupermarketSystem.IntegrationTests.SystemDomain;

/// <summary>
/// SystemSetting ليس كيانًا IBranchOwned - اختبار Handler مباشر. ⚠️ كل
/// اختبار بيغيّر إعداد Whitelist لازم يرجّعه لقيمته الافتراضية بالنهاية
/// (finally) - راجع تعليق PlaceOrderTests عن IMemoryCache Singleton
/// (CachedSettingsProvider) بعمر التشغيلة الكاملة، ما بينصفّر مع Respawn.
/// نستخدم هون "Pos.AllowVoidSale" حصرًا - مفتاح لا يمسّه أي ملف اختبار آخر.
/// </summary>
[Collection(DatabaseCollection.Name)]
public sealed class AdminSettingsTests : IntegrationTestBase
{
    public AdminSettingsTests(DatabaseFixture fixture) : base(fixture) { }

    private const string TestKey = "Pos.AllowVoidSale";

    [Fact]
    public async Task جلب_الإعدادات_يتضمّن_المفتاح_بقيمته_المحدَّثة_بعد_التعديل()
    {
        using var scope = CreateScope();
        var updateHandler = scope.ServiceProvider.GetRequiredService<UpdateAdminSettingHandler>();
        var getHandler = scope.ServiceProvider.GetRequiredService<GetAdminSettingsHandler>();

        var setResult = await updateHandler.HandleAsync(new UpdateAdminSettingCommand(TestKey, "false"), CancellationToken.None);
        Assert.True(setResult.IsSuccess);

        try
        {
            var settings = await getHandler.HandleAsync(CancellationToken.None);
            var item = settings.Settings.Single(s => s.Key == TestKey);
            Assert.Equal("False", item.Value);
            Assert.Equal(AdminSettingDataType.Boolean, item.DataType);
        }
        finally
        {
            await updateHandler.HandleAsync(new UpdateAdminSettingCommand(TestKey, "true"), CancellationToken.None);
        }
    }

    [Fact]
    public async Task تعديل_مفتاح_غير_مسموح_يفشل_بخطأ_تحقق()
    {
        using var scope = CreateScope();
        var handler = scope.ServiceProvider.GetRequiredService<UpdateAdminSettingHandler>();

        var result = await handler.HandleAsync(
            new UpdateAdminSettingCommand("Telegram.BotToken", "sneaky-value"), CancellationToken.None);

        Assert.True(result.IsFailure);
        Assert.Equal("AdminSettings.UnknownKey", result.Error!.Code);
    }

    [Fact]
    public async Task تعديل_قيمة_منطقية_بنص_غير_صالح_يفشل_بخطأ_تحقق()
    {
        using var scope = CreateScope();
        var handler = scope.ServiceProvider.GetRequiredService<UpdateAdminSettingHandler>();

        var result = await handler.HandleAsync(new UpdateAdminSettingCommand(TestKey, "not-a-bool"), CancellationToken.None);

        Assert.True(result.IsFailure);
        Assert.Equal("AdminSettings.InvalidValue", result.Error!.Code);
    }

    [Fact]
    public async Task تعديل_قيمة_عشرية_بنص_غير_صالح_يفشل_بخطأ_تحقق()
    {
        const string decimalKey = "Pos.MaxManualDiscountPercentage";
        using var scope = CreateScope();
        var handler = scope.ServiceProvider.GetRequiredService<UpdateAdminSettingHandler>();

        var result = await handler.HandleAsync(new UpdateAdminSettingCommand(decimalKey, "عشرة بالمئة"), CancellationToken.None);

        Assert.True(result.IsFailure);
        Assert.Equal(ErrorType.Validation, result.Error!.Type);
    }
}
