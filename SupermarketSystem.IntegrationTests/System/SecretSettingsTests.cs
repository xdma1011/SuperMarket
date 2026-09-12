using Microsoft.Extensions.DependencyInjection;
using SupermarketSystem.Application.System.GetSecretSettings;
using SupermarketSystem.Application.System.UpdateSecretSetting;
using Xunit;

namespace SupermarketSystem.IntegrationTests.SystemDomain;

/// <summary>راجع تعليق AdminSettingsTests عن إلزامية إرجاع الإعداد لقيمته الافتراضية بالنهاية (IMemoryCache Singleton).</summary>
[Collection(DatabaseCollection.Name)]
public sealed class SecretSettingsTests : IntegrationTestBase
{
    public SecretSettingsTests(DatabaseFixture fixture) : base(fixture) { }

    private const string TestKey = "Telegram.BotToken";

    [Fact]
    public async Task المفتاح_السري_يظهر_غير_معدّ_ثم_معدّ_بعد_التحديث_بلا_كشف_القيمة_نفسها()
    {
        using var scope = CreateScope();
        var getHandler = scope.ServiceProvider.GetRequiredService<GetSecretSettingsHandler>();
        var updateHandler = scope.ServiceProvider.GetRequiredService<UpdateSecretSettingHandler>();

        var before = await getHandler.HandleAsync(CancellationToken.None);
        Assert.False(before.Secrets.Single(s => s.Key == TestKey).IsSet);

        var setResult = await updateHandler.HandleAsync(new UpdateSecretSettingCommand(TestKey, "real-token-value"), CancellationToken.None);
        Assert.True(setResult.IsSuccess);

        try
        {
            var after = await getHandler.HandleAsync(CancellationToken.None);
            Assert.True(after.Secrets.Single(s => s.Key == TestKey).IsSet);
        }
        finally
        {
            await updateHandler.HandleAsync(new UpdateSecretSettingCommand(TestKey, ""), CancellationToken.None);
        }
    }

    [Fact]
    public async Task تعديل_مفتاح_سري_غير_مسموح_يفشل_بخطأ_تحقق()
    {
        using var scope = CreateScope();
        var handler = scope.ServiceProvider.GetRequiredService<UpdateSecretSettingHandler>();

        var result = await handler.HandleAsync(
            new UpdateSecretSettingCommand("Pos.AllowVoidSale", "true"), CancellationToken.None);

        Assert.True(result.IsFailure);
        Assert.Equal("SecretSetting.UnknownKey", result.Error!.Code);
    }
}
