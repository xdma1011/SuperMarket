using System.Net;
using System.Net.Http.Json;
using System.Security.Cryptography;
using System.Text;
using Microsoft.Extensions.DependencyInjection;
using SupermarketSystem.Application.Common.Results;
using SupermarketSystem.Application.CustomerAuth.LinkTelegramContact;
using SupermarketSystem.Application.CustomerAuth.RequestCustomerOtp;
using SupermarketSystem.Application.CustomerAuth.VerifyCustomerOtp;
using SupermarketSystem.Domain.Customers;
using Xunit;

namespace SupermarketSystem.IntegrationTests.CustomerAuth;

/// <summary>
/// لا TelegramChatLink ولا CustomerOtpCode كيانات IBranchOwned (زبائن
/// عامة مشتركة بكل الفروع) - اختبارات Handler مباشر بلا حاجة سياق فرع.
/// BotToken غير مُعدّ بقاعدة بيانات الاختبار، فـTelegramBotClient.SendMessageAsync
/// بيرجع false بهدوء بلا أي اتصال شبكة فعلي (راجع تعليق TelegramBotClient) -
/// آمن للاختبار بلا اعتماديات خارجية.
/// </summary>
[Collection(DatabaseCollection.Name)]
public sealed class CustomerOtpFlowTests : IntegrationTestBase
{
    public CustomerOtpFlowTests(DatabaseFixture fixture) : base(fixture) { }

    private static string HashCode(string code)
        => Convert.ToBase64String(SHA256.HashData(Encoding.UTF8.GetBytes(code)));

    [Fact]
    public async Task طلب_كود_لرقم_غير_مربوط_بتلغرام_يرجع_TelegramLinked_false()
    {
        using var scope = CreateScope();
        var handler = scope.ServiceProvider.GetRequiredService<RequestCustomerOtpHandler>();

        var result = await handler.HandleAsync(new RequestCustomerOtpCommand("0794000001"), CancellationToken.None);

        Assert.True(result.IsSuccess);
        Assert.False(result.Value.TelegramLinked);
    }

    [Fact]
    public async Task طلب_كود_برقم_فاضي_يفشل_بخطأ_تحقق()
    {
        using var scope = CreateScope();
        var handler = scope.ServiceProvider.GetRequiredService<RequestCustomerOtpHandler>();

        var result = await handler.HandleAsync(new RequestCustomerOtpCommand("   "), CancellationToken.None);

        Assert.True(result.IsFailure);
        Assert.Equal(ErrorType.Validation, result.Error!.Type);
    }

    [Fact]
    public async Task ربط_تلغرام_ثم_طلب_كود_ينشئ_سطر_CustomerOtpCode_فعليًا()
    {
        const string phone = "0794000002";
        using var scope = CreateScope();
        var db = CreateDbContext(scope);
        var linkHandler = scope.ServiceProvider.GetRequiredService<LinkTelegramContactHandler>();
        var otpHandler = scope.ServiceProvider.GetRequiredService<RequestCustomerOtpHandler>();

        await linkHandler.HandleAsync(new LinkTelegramContactCommand("123456789", "+" + phone), CancellationToken.None);

        var result = await otpHandler.HandleAsync(new RequestCustomerOtpCommand(phone), CancellationToken.None);

        Assert.True(result.IsSuccess);
        Assert.True(result.Value.TelegramLinked);
        Assert.True(db.CustomerOtpCodes.Any(o => o.Phone == phone));
    }

    [Fact]
    public async Task إعادة_ربط_نفس_الرقم_من_محادثة_جديدة_يحدّث_الربط_لا_يكرّره()
    {
        const string phone = "0794000003";
        using var scope = CreateScope();
        var db = CreateDbContext(scope);
        var linkHandler = scope.ServiceProvider.GetRequiredService<LinkTelegramContactHandler>();

        await linkHandler.HandleAsync(new LinkTelegramContactCommand("111", phone), CancellationToken.None);
        await linkHandler.HandleAsync(new LinkTelegramContactCommand("222", phone), CancellationToken.None);

        var links = db.TelegramChatLinks.Where(l => l.Phone == phone).ToList();
        Assert.Single(links);
        Assert.Equal("222", links[0].ChatId);
    }

    [Fact]
    public async Task تحقق_بكود_صحيح_ينجح_ويصدر_توكن_وبكود_غلط_يفشل()
    {
        const string phone = "0794000004";
        using var scope = CreateScope();
        var db = CreateDbContext(scope);
        var verifyHandler = scope.ServiceProvider.GetRequiredService<VerifyCustomerOtpHandler>();

        const string code = "123456";
        db.CustomerOtpCodes.Add(new CustomerOtpCode(phone, HashCode(code), DateTime.UtcNow, DateTime.UtcNow.AddMinutes(5)));
        await db.SaveChangesAsync();

        var wrongCode = await verifyHandler.HandleAsync(new VerifyCustomerOtpCommand(phone, "000000"), CancellationToken.None);
        Assert.True(wrongCode.IsFailure);
        Assert.Equal("CustomerOtp.InvalidOrExpired", wrongCode.Error!.Code);

        var correct = await verifyHandler.HandleAsync(new VerifyCustomerOtpCommand(phone, code), CancellationToken.None);
        Assert.True(correct.IsSuccess);
        Assert.False(string.IsNullOrWhiteSpace(correct.Value.AccessToken));
        Assert.NotEqual(Guid.Empty, correct.Value.CustomerId);
    }

    [Fact]
    public async Task تحقق_بنفس_الكود_مرتين_يفشل_ثانية_مرة_لأنه_استُهلك()
    {
        const string phone = "0794000005";
        using var scope = CreateScope();
        var db = CreateDbContext(scope);
        var verifyHandler = scope.ServiceProvider.GetRequiredService<VerifyCustomerOtpHandler>();

        const string code = "654321";
        db.CustomerOtpCodes.Add(new CustomerOtpCode(phone, HashCode(code), DateTime.UtcNow, DateTime.UtcNow.AddMinutes(5)));
        await db.SaveChangesAsync();

        var first = await verifyHandler.HandleAsync(new VerifyCustomerOtpCommand(phone, code), CancellationToken.None);
        Assert.True(first.IsSuccess);

        var second = await verifyHandler.HandleAsync(new VerifyCustomerOtpCommand(phone, code), CancellationToken.None);
        Assert.True(second.IsFailure);
        Assert.Equal("CustomerOtp.InvalidOrExpired", second.Error!.Code);
    }

    [Fact]
    public async Task تحقق_بكود_منتهي_الصلاحية_يفشل()
    {
        const string phone = "0794000006";
        using var scope = CreateScope();
        var db = CreateDbContext(scope);
        var verifyHandler = scope.ServiceProvider.GetRequiredService<VerifyCustomerOtpHandler>();

        const string code = "111222";
        db.CustomerOtpCodes.Add(new CustomerOtpCode(
            phone, HashCode(code), DateTime.UtcNow.AddMinutes(-10), DateTime.UtcNow.AddMinutes(-5)));
        await db.SaveChangesAsync();

        var result = await verifyHandler.HandleAsync(new VerifyCustomerOtpCommand(phone, code), CancellationToken.None);
        Assert.True(result.IsFailure);
        Assert.Equal("CustomerOtp.InvalidOrExpired", result.Error!.Code);
    }

    [Fact]
    public async Task تحقق_لزبون_محظور_يفشل_بـForbidden()
    {
        const string phone = "0794000007";
        using var scope = CreateScope();
        var db = CreateDbContext(scope);
        var verifyHandler = scope.ServiceProvider.GetRequiredService<VerifyCustomerOtpHandler>();

        var customer = new Customer(phone, phone, null);
        customer.Block();
        db.Customers.Add(customer);

        const string code = "999888";
        db.CustomerOtpCodes.Add(new CustomerOtpCode(phone, HashCode(code), DateTime.UtcNow, DateTime.UtcNow.AddMinutes(5)));
        await db.SaveChangesAsync();

        var result = await verifyHandler.HandleAsync(new VerifyCustomerOtpCommand(phone, code), CancellationToken.None);
        Assert.True(result.IsFailure);
        Assert.Equal(ErrorType.Forbidden, result.Error!.Type);
    }

    [Fact]
    public async Task endpoints_طلب_وتحقق_OTP_مسموحة_بلا_توكن()
    {
        var client = CreateAnonymousClient();

        var requestResponse = await client.PostAsJsonAsync("/api/v1/customer-auth/request-otp", new { Phone = "0794000008" });
        Assert.Equal(HttpStatusCode.OK, requestResponse.StatusCode);

        var verifyResponse = await client.PostAsJsonAsync("/api/v1/customer-auth/verify-otp", new { Phone = "0794000008", Code = "000000" });
        Assert.Equal(HttpStatusCode.BadRequest, verifyResponse.StatusCode);
    }

    [Fact]
    public async Task webhook_تلغرام_بسر_غلط_يرجع_401()
    {
        using var scope = CreateScope();
        var updateSettingHandler = scope.ServiceProvider
            .GetRequiredService<SupermarketSystem.Application.System.UpdateSecretSetting.UpdateSecretSettingHandler>();

        var setResult = await updateSettingHandler.HandleAsync(
            new SupermarketSystem.Application.System.UpdateSecretSetting.UpdateSecretSettingCommand(
                "Telegram.WebhookSecret", "real-secret"), CancellationToken.None);
        Assert.True(setResult.IsSuccess);

        try
        {
            var client = CreateAnonymousClient();
            client.DefaultRequestHeaders.Add("X-Telegram-Bot-Api-Secret-Token", "wrong-secret");

            var response = await client.PostAsJsonAsync("/api/v1/telegram/webhook", new { });
            Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
        }
        finally
        {
            // إلزامي - راجع تعليق PlaceOrderTests عن IMemoryCache Singleton بعمر التشغيلة.
            await updateSettingHandler.HandleAsync(
                new SupermarketSystem.Application.System.UpdateSecretSetting.UpdateSecretSettingCommand(
                    "Telegram.WebhookSecret", ""), CancellationToken.None);
        }
    }
}
