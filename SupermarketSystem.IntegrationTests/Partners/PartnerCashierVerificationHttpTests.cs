using System.Net;
using System.Net.Http.Json;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Text.RegularExpressions;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using SupermarketSystem.Application.Common.Interfaces;
using SupermarketSystem.Application.Partners;
using SupermarketSystem.Domain.CashManagement;
using SupermarketSystem.Domain.Customers;
using SupermarketSystem.Domain.Partners;
using SupermarketSystem.IntegrationTests.Common;
using Xunit;

namespace SupermarketSystem.IntegrationTests.Partners;

/// <summary>
/// تحقق الشريك بالكاشير بطرق إضافية (29/9/2026): باركود شخصي وكود تلغرام - كل وحدة بتتفعّل لحالها من الإعدادات،
/// والكود مربوط بالمبلغ اللي انطلب عشانه. الكاشير داخل بحسابه (Sales.Create)، نفس شكل طلب تطبيق الويندوز.
/// </summary>
[Collection(DatabaseCollection.Name)]
public sealed class PartnerCashierVerificationHttpTests : IntegrationTestBase
{
    public PartnerCashierVerificationHttpTests(DatabaseFixture fixture) : base(fixture) { }

    private sealed class CapturingTelegramBot : ITelegramBotClient
    {
        public List<(string ChatId, string Text)> Sent { get; } = new();

        public Task<bool> SendMessageAsync(string chatId, string text, CancellationToken cancellationToken)
        {
            Sent.Add((chatId, text));
            return Task.FromResult(true);
        }

        public Task<bool> RequestContactAsync(string chatId, string text, CancellationToken cancellationToken) => Task.FromResult(true);
    }

    private static async Task<JsonElement> OkJsonAsync(HttpResponseMessage response, string what)
    {
        var body = await response.Content.ReadAsStringAsync();
        Assert.True(response.IsSuccessStatusCode, $"{what}: {(int)response.StatusCode} {body}");
        return string.IsNullOrWhiteSpace(body) ? default : JsonDocument.Parse(body).RootElement;
    }

    private async Task<(HttpClient Admin, HttpClient Cashier, Guid PartnerId)> SetUpAsync(bool otpEnabled, bool barcodeEnabled)
    {
        Guid partnerId;
        using (var scope = CreateScope())
        {
            await TestDataBuilder.ActAsAdminAsync(scope, Fixture);
            await TestDataBuilder.SetSettingAsync(scope, PartnerVerificationSettingsKeys.TelegramOtpEnabled, otpEnabled);
            await TestDataBuilder.SetSettingAsync(scope, PartnerVerificationSettingsKeys.BarcodeEnabled, barcodeEnabled);
            var db = CreateDbContext(scope);
            var partner = new Partner(Fixture.TestBranchId, "محمد الشريك", PartnerType.Capital, null, null, null);
            db.Partners.Add(partner);
            await db.SaveChangesAsync();
            partnerId = partner.Id;
        }

        var admin = await CreateAuthenticatedClientAsync();
        var (_, cashierName) = await UsersTestDataHelper.CreateUserWithRoleReturningUsernameAsync(
            Fixture.Factory.Services, Fixture.TestBranchId, UsersTestDataHelper.CashierRoleId, "verify.cashier");
        var cashier = await LoginHelper.LoginAsAsync(Fixture, cashierName, UsersTestDataHelper.DefaultPassword, appType: "Cashier");
        return (admin, cashier, partnerId);
    }

    private Task<HttpResponseMessage> WithdrawAsync(HttpClient cashier, decimal amount, Guid clientRequestId,
        string? barcode = null, Guid? otpChallengeId = null, string? otpCode = null) =>
        cashier.PostAsJsonAsync("/api/v1/partner-withdrawals/verified", new
        {
            branchId = Fixture.TestBranchId, username = (string?)null, password = (string?)null, amount, notes = (string?)null,
            clientRequestId, otpChallengeId, otpCode, barcode
        });

    [Fact]
    public async Task باركود_الشريك_الشخصي_مطفي_افتراضيًا_وبعد_التفعيل_بيسحب_وإصدار_جديد_بيلغي_القديم()
    {
        var (admin, cashier, partnerId) = await SetUpAsync(otpEnabled: false, barcodeEnabled: false);

        var issued = await OkJsonAsync(await admin.PostAsync($"/api/v1/partners/{partnerId}/cashier-barcode", null), "إصدار باركود");
        var barcode = issued.GetProperty("barcode").GetString()!;
        Assert.Matches("^[0-9]{20}$", barcode);

        // مطفي من الإعدادات = مرفوض حتى بباركود صحيح
        Assert.Equal(HttpStatusCode.Forbidden, (await WithdrawAsync(cashier, 5m, Guid.NewGuid(), barcode: barcode)).StatusCode);
        var options = await OkJsonAsync(await cashier.GetAsync($"/api/v1/partner-withdrawals/verification-options?branchId={Fixture.TestBranchId}"), "خيارات");
        Assert.True(options.GetProperty("passwordEnabled").GetBoolean());
        Assert.False(options.GetProperty("barcodeEnabled").GetBoolean());

        using (var scope = CreateScope())
        {
            await TestDataBuilder.SetSettingAsync(scope, PartnerVerificationSettingsKeys.BarcodeEnabled, true);
        }

        // الماسح ممكن يبعت مسافات - بتنشال
        var spaced = string.Join(' ', barcode.Chunk(5).Select(c => new string(c)));
        var withdrawal = await OkJsonAsync(await WithdrawAsync(cashier, 5m, Guid.NewGuid(), barcode: spaced), "سحب بالباركود");
        Assert.Equal("محمد الشريك", withdrawal.GetProperty("partnerName").GetString());
        Assert.Equal(-5m, withdrawal.GetProperty("newBalance").GetDecimal());

        Assert.Equal(HttpStatusCode.Forbidden, (await WithdrawAsync(cashier, 1m, Guid.NewGuid(), barcode: "12345678901234567890")).StatusCode);

        // كرت ضايع: إصدار جديد، القديم ما عاد يمشي
        var reissued = (await OkJsonAsync(await admin.PostAsync($"/api/v1/partners/{partnerId}/cashier-barcode", null), "إعادة إصدار"))
            .GetProperty("barcode").GetString()!;
        Assert.Equal(HttpStatusCode.Forbidden, (await WithdrawAsync(cashier, 1m, Guid.NewGuid(), barcode: barcode)).StatusCode);
        await OkJsonAsync(await WithdrawAsync(cashier, 1m, Guid.NewGuid(), barcode: reissued), "سحب بالباركود الجديد");

        var partners = await OkJsonAsync(await admin.GetAsync($"/api/v1/partners?branchId={Fixture.TestBranchId}"), "الشركاء");
        var row = partners.EnumerateArray().Single(p => p.GetProperty("id").GetGuid() == partnerId);
        Assert.True(row.GetProperty("hasCashierBarcode").GetBoolean());
        Assert.Equal(-6m, row.GetProperty("currentBalance").GetDecimal());

        // الإلغاء
        await OkJsonAsync(await admin.DeleteAsync($"/api/v1/partners/{partnerId}/cashier-barcode"), "إلغاء الباركود");
        Assert.Equal(HttpStatusCode.Forbidden, (await WithdrawAsync(cashier, 1m, Guid.NewGuid(), barcode: reissued)).StatusCode);

        // السحب من الصندوق: حركتين PayOut (5 + 1)
        using var check = CreateScope();
        await TestDataBuilder.ActAsAdminAsync(check, Fixture);
        var db = CreateDbContext(check);
        var payOuts = await db.CashDrawerLogs.AsNoTracking()
            .Where(l => l.ReferenceType == CashDrawerReferenceType.PartnerWithdrawal).SumAsync(l => l.Amount);
        Assert.Equal(6m, payOuts);
    }

    [Fact]
    public async Task كود_تلغرام_مربوط_بالمبلغ_ولسحب_واحد_وإعادة_الإرسال_بتنقبل_و5_محاولات_غلط_بتحرقه()
    {
        var (admin, cashier, partnerId) = await SetUpAsync(otpEnabled: true, barcodeEnabled: false);

        // الشريك فاتح البوت وشارك رقمه (بصيغة دولية)، والإدارة كتبت رقمه المحلي
        using (var scope = CreateScope())
        {
            var db = CreateDbContext(scope);
            db.TelegramChatLinks.Add(new TelegramChatLink("962791112222", "chat-555", DateTime.UtcNow));
            await db.SaveChangesAsync();
        }

        Assert.Equal(HttpStatusCode.BadRequest,
            (await admin.PutAsJsonAsync($"/api/v1/partners/{partnerId}/telegram-phone", new { telegramPhone = "abc" })).StatusCode);
        await OkJsonAsync(await admin.PutAsJsonAsync($"/api/v1/partners/{partnerId}/telegram-phone", new { telegramPhone = "0791112222" }), "رقم تلغرام");
        var row = (await OkJsonAsync(await admin.GetAsync($"/api/v1/partners?branchId={Fixture.TestBranchId}"), "الشركاء"))
            .EnumerateArray().Single(p => p.GetProperty("id").GetGuid() == partnerId);
        Assert.True(row.GetProperty("telegramLinked").GetBoolean());

        var options = await OkJsonAsync(await cashier.GetAsync($"/api/v1/partner-withdrawals/verification-options?branchId={Fixture.TestBranchId}"), "خيارات");
        Assert.True(options.GetProperty("telegramOtpEnabled").GetBoolean());
        Assert.Contains(options.GetProperty("otpPartners").EnumerateArray(), p => p.GetProperty("partnerId").GetGuid() == partnerId);

        // بيئة الاختبار بلا توكن بوت: الإرسال بيفشل بوضوح (مش نجاح صامت بلا كود)
        var noBot = await cashier.PostAsJsonAsync("/api/v1/partner-withdrawals/otp", new { branchId = Fixture.TestBranchId, partnerId, amount = 7.5m });
        Assert.Equal(HttpStatusCode.UnprocessableEntity, noBot.StatusCode);

        // نفس الـhandler ببوت بيلتقط الرسالة
        var bot = new CapturingTelegramBot();
        Guid challengeId;
        using (var scope = CreateScope())
        {
            await TestDataBuilder.ActAsAdminAsync(scope, Fixture);
            var handler = new RequestPartnerOtpHandler(
                scope.ServiceProvider.GetRequiredService<IApplicationDbContext>(),
                scope.ServiceProvider.GetRequiredService<ICurrentUserContext>(),
                scope.ServiceProvider.GetRequiredService<ISettingsProvider>(),
                bot,
                scope.ServiceProvider.GetRequiredService<IDateTimeProvider>());
            var requested = await handler.HandleAsync(new RequestPartnerOtpCommand(Fixture.TestBranchId, partnerId, 7.5m), CancellationToken.None);
            Assert.True(requested.IsSuccess, requested.Error?.Message);
            challengeId = requested.Value.ChallengeId;

            var again = await handler.HandleAsync(new RequestPartnerOtpCommand(Fixture.TestBranchId, partnerId, 7.5m), CancellationToken.None);
            Assert.Equal("PartnerOtp.TooFrequent", again.Error?.Code);
        }

        var (chatId, text) = Assert.Single(bot.Sent);
        Assert.Equal("chat-555", chatId);
        Assert.Contains("7.500", text);
        var code = Regex.Match(text, @":\s(\d{6})").Groups[1].Value;
        Assert.Equal(6, code.Length);

        Assert.Equal(HttpStatusCode.Forbidden, (await WithdrawAsync(cashier, 7.5m, Guid.NewGuid(), otpChallengeId: challengeId, otpCode: "000000")).StatusCode);

        // الكود صح بس لمبلغ تاني = مرفوض (الكاشير ما بيقدر يطلب كود لمبلغ ويسحب غيره)
        var mismatch = await WithdrawAsync(cashier, 70m, Guid.NewGuid(), otpChallengeId: challengeId, otpCode: code);
        Assert.Equal(HttpStatusCode.Forbidden, mismatch.StatusCode);
        Assert.Contains("7.500", await mismatch.Content.ReadAsStringAsync());

        var requestId = Guid.NewGuid();
        var withdrawn = await OkJsonAsync(await WithdrawAsync(cashier, 7.5m, requestId, otpChallengeId: challengeId, otpCode: code), "سحب بالكود");
        Assert.Equal(7.5m, withdrawn.GetProperty("amount").GetDecimal());
        Assert.False(withdrawn.GetProperty("wasReplay").GetBoolean());

        // انقطاع نت بعد التسجيل: نفس الطلب مرة تانية = نفس السحب
        var replay = await OkJsonAsync(await WithdrawAsync(cashier, 7.5m, requestId, otpChallengeId: challengeId, otpCode: code), "إعادة إرسال");
        Assert.True(replay.GetProperty("wasReplay").GetBoolean());

        // سحب تاني بنفس الكود = مرفوض
        Assert.Equal(HttpStatusCode.Forbidden, (await WithdrawAsync(cashier, 7.5m, Guid.NewGuid(), otpChallengeId: challengeId, otpCode: code)).StatusCode);

        // 5 محاولات غلط بتحرق الكود حتى لو الصح إجا بعدها
        Guid burnedId;
        using (var scope = CreateScope())
        {
            await TestDataBuilder.ActAsAdminAsync(scope, Fixture);
            var db = CreateDbContext(scope);
            var hash = Convert.ToBase64String(SHA256.HashData(Encoding.UTF8.GetBytes("123456")));
            var burned = new PartnerOtpChallenge(Fixture.TestBranchId, partnerId, hash, 2m, DateTime.UtcNow, DateTime.UtcNow.AddMinutes(5), Fixture.AdminUserId);
            db.PartnerOtpChallenges.Add(burned);
            await db.SaveChangesAsync();
            burnedId = burned.Id;
        }

        for (var i = 0; i < PartnerOtpChallenge.MaxFailedAttempts; i++)
        {
            await WithdrawAsync(cashier, 2m, Guid.NewGuid(), otpChallengeId: burnedId, otpCode: "999999");
        }

        var afterBurn = await WithdrawAsync(cashier, 2m, Guid.NewGuid(), otpChallengeId: burnedId, otpCode: "123456");
        Assert.Equal(HttpStatusCode.Forbidden, afterBurn.StatusCode);
        Assert.Contains("اطلب كود جديد", await afterBurn.Content.ReadAsStringAsync());

        // وبعد إطفاء الطريقة من الإعدادات، الكود ما بيمشي
        using (var scope = CreateScope())
        {
            await TestDataBuilder.SetSettingAsync(scope, PartnerVerificationSettingsKeys.TelegramOtpEnabled, false);
        }

        var disabled = await cashier.PostAsJsonAsync("/api/v1/partner-withdrawals/otp", new { branchId = Fixture.TestBranchId, partnerId, amount = 1m });
        Assert.Equal(HttpStatusCode.Forbidden, disabled.StatusCode);
    }
}
