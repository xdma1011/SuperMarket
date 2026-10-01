using System.Net;
using System.Text;
using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging.Abstractions;
using SupermarketSystem.Application.Common.Interfaces;
using SupermarketSystem.Application.Common.Notifications;
using SupermarketSystem.Application.Ordering.Coupons;
using SupermarketSystem.Application.Partners;
using SupermarketSystem.Domain.Customers;
using SupermarketSystem.Domain.Notifications;
using SupermarketSystem.Domain.Ordering;
using SupermarketSystem.Domain.Partners;
using SupermarketSystem.Infrastructure.Services;
using SupermarketSystem.IntegrationTests.Common;
using Xunit;

namespace SupermarketSystem.IntegrationTests.Notifications;

/// <summary>
/// تلغرام هو قناة التواصل الوحيدة (1/10/2026، صاحب المشروع: واتساب بزنس "لأ لأنه بمصاري، خليها تلغرام مؤقتا وجربها").
/// بيئة الاختبار ما بتوصل لـapi.telegram.org، فالعميلين الحقيقيين (TelegramNotificationSender لتنبيهات الإدارة،
/// TelegramBotClient لبوت الزبائن/الشركاء) بيشتغلوا هون بـHttpClient فوق معالج بيلتقط الطلب - يعني نفس الكود اللي
/// بيبعت فعليًا، والفحص على شكل طلب Bot API نفسه: الرابط بالتوكن، chat_id، النص، وparse_mode.
/// </summary>
[Collection(DatabaseCollection.Name)]
public sealed class TelegramDeliveryTests : IntegrationTestBase
{
    public TelegramDeliveryTests(DatabaseFixture fixture) : base(fixture) { }

    /// <summary>كاش الإعدادات (CachedSettingsProvider) عايش أطول من تصفير القاعدة بين الاختبارات - توكن بيضل بالكاش كان رح
    /// يخلّي باقي الاختبارات تحاول توصل لتلغرام الحقيقي. فكل اختبار هون بيصفّر التوكنات قبل وبعد.</summary>
    private async Task ClearTelegramTokensAsync()
    {
        using var scope = CreateScope();
        await TestDataBuilder.SetSettingAsync(scope, NotificationSettingsKeys.TelegramBotToken, "");
        await TestDataBuilder.SetSettingAsync(scope, NotificationSettingsKeys.TelegramChatId, "");
        await TestDataBuilder.SetSettingAsync(scope, TelegramSettingsKeys.BotToken, "");
        await TestDataBuilder.SetSettingAsync(scope, PartnerVerificationSettingsKeys.TelegramOtpEnabled, false);
    }

    /// <summary>بيلتقط كل طلب وبيرد زي تلغرام: 200 {"ok":true}، أو 400 "chat not found" للمحادثات المرفوضة.</summary>
    private sealed class FakeTelegramApi : HttpMessageHandler
    {
        private readonly HashSet<string> _rejectedChats;

        public FakeTelegramApi(params string[] rejectedChats) => _rejectedChats = rejectedChats.ToHashSet();

        public List<(string Url, JsonElement Body)> Requests { get; } = new();

        protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
        {
            Assert.Equal(HttpMethod.Post, request.Method);
            var body = JsonDocument.Parse(await request.Content!.ReadAsStringAsync(cancellationToken)).RootElement.Clone();
            Requests.Add((request.RequestUri!.ToString(), body));

            return _rejectedChats.Contains(body.GetProperty("chat_id").GetString()!)
                ? new HttpResponseMessage(HttpStatusCode.BadRequest)
                {
                    Content = new StringContent("{\"ok\":false,\"error_code\":400,\"description\":\"Bad Request: chat not found\"}", Encoding.UTF8, "application/json")
                }
                : new HttpResponseMessage(HttpStatusCode.OK) { Content = new StringContent("{\"ok\":true}", Encoding.UTF8, "application/json") };
        }
    }

    [Fact]
    public async Task تنبيه_خطير_بيوصل_تلغرام_بالتوكن_ولكل_محادثة_ومحادثة_مرفوضة_ما_بتوقف_الباقي_وبتنسجّل()
    {
        try { await AlertDeliveryCoreAsync(); }
        finally { await ClearTelegramTokensAsync(); }
    }

    private async Task AlertDeliveryCoreAsync()
    {
        // التطبيق الحقيقي مسجّل قناة تلغرام فعلًا (مش بس الكود موجود)
        using (var scope = CreateScope())
        {
            Assert.Contains(scope.ServiceProvider.GetServices<INotificationSender>(), s => s is TelegramNotificationSender);
        }

        var api = new FakeTelegramApi("1002");
        using (var scope = CreateScope())
        {
            await TestDataBuilder.SetSettingAsync(scope, NotificationSettingsKeys.TelegramBotToken, "111:AAA");
            await TestDataBuilder.SetSettingAsync(scope, NotificationSettingsKeys.TelegramChatId, "1001, 1002");

            var sender = new TelegramNotificationSender(new HttpClient(api), scope.ServiceProvider.GetRequiredService<ISettingsProvider>());
            var dispatcher = new NotificationDispatcher(
                scope.ServiceProvider.GetRequiredService<IApplicationDbContext>(),
                new INotificationSender[] { sender },
                scope.ServiceProvider.GetRequiredService<IDateTimeProvider>(),
                NullLogger<NotificationDispatcher>.Instance);

            await dispatcher.NotifyAsync("عجز بتقفيل الصندوق 1.500", "الكاشير: أحمد (الفرق -1.500 د.أ)", CancellationToken.None,
                NotificationSeverity.Critical, "/cash-closings");
        }

        Assert.Equal(2, api.Requests.Count);
        Assert.All(api.Requests, r =>
        {
            Assert.Equal("https://api.telegram.org/bot111:AAA/sendMessage", r.Url);
            Assert.Equal("MarkdownV2", r.Body.GetProperty("parse_mode").GetString());
            // الخطورة بأول العنوان، وكل رمز خاص بـMarkdownV2 (. - ( )) مهرَّب - غير هيك تلغرام بيرفض الرسالة كلها
            Assert.Equal("*🔴 خطير — عجز بتقفيل الصندوق 1\\.500*\nالكاشير: أحمد \\(الفرق \\-1\\.500 د\\.أ\\)",
                r.Body.GetProperty("text").GetString());
        });
        Assert.Equal(new[] { "1001", "1002" }, api.Requests.Select(r => r.Body.GetProperty("chat_id").GetString()).ToArray());

        using (var scope = CreateScope())
        {
            var db = CreateDbContext(scope);
            var inApp = await db.Notifications.SingleAsync(n => n.Channel == NotificationChannel.InApp);
            Assert.Equal(NotificationSeverity.Critical, inApp.Severity);
            Assert.Equal("/cash-closings", inApp.LinkRoute);

            // وصلت لمحادثة وحدة على الأقل = "انبعت"، والفشل الجزئي مكتوب بالسجل باسم المحادثة وسبب تلغرام
            var telegram = await db.Notifications.Include(n => n.DeliveryAttempts).SingleAsync(n => n.Channel == NotificationChannel.Telegram);
            Assert.Equal(NotificationStatus.Sent, telegram.Status);
            var attempt = Assert.Single(telegram.DeliveryAttempts);
            Assert.True(attempt.Success);
            Assert.Contains("1002", attempt.ErrorMessage);
            Assert.Contains("chat not found", attempt.ErrorMessage);
        }
    }

    [Fact]
    public async Task بلا_توكن_ولا_طلب_بيطلع_لتلغرام_والتنبيه_بالنظام_بينحفظ_عادي()
    {
        await ClearTelegramTokensAsync();
        var api = new FakeTelegramApi();
        using (var scope = CreateScope())
        {
            var settings = scope.ServiceProvider.GetRequiredService<ISettingsProvider>();
            var dispatcher = new NotificationDispatcher(
                scope.ServiceProvider.GetRequiredService<IApplicationDbContext>(),
                new INotificationSender[] { new TelegramNotificationSender(new HttpClient(api), settings) },
                scope.ServiceProvider.GetRequiredService<IDateTimeProvider>(),
                NullLogger<NotificationDispatcher>.Instance);
            await dispatcher.NotifyAsync("إلغاء فاتورة", "فاتورة 15", CancellationToken.None);

            Assert.False(await new TelegramBotClient(new HttpClient(api), settings).SendMessageAsync("chat-1", "مرحبا", CancellationToken.None));
        }

        Assert.Empty(api.Requests);
        using (var scope = CreateScope())
        {
            var db = CreateDbContext(scope);
            Assert.Equal(1, await db.Notifications.CountAsync(n => n.Channel == NotificationChannel.InApp));
            var telegram = await db.Notifications.SingleAsync(n => n.Channel == NotificationChannel.Telegram);
            Assert.Equal(NotificationStatus.Failed, telegram.Status);
        }
    }

    [Fact]
    public async Task كوبون_وكود_سحب_الشريك_بيوصلوا_من_بوت_الزبائن_للمحادثة_المربوطة_بالرقم()
    {
        try { await CustomerBotDeliveryCoreAsync(); }
        finally { await ClearTelegramTokensAsync(); }
    }

    private async Task CustomerBotDeliveryCoreAsync()
    {
        var api = new FakeTelegramApi();
        Guid partnerId;
        using (var scope = CreateScope())
        {
            await TestDataBuilder.ActAsAdminAsync(scope, Fixture);
            await TestDataBuilder.SetSettingAsync(scope, TelegramSettingsKeys.BotToken, "222:BBB");
            await TestDataBuilder.SetSettingAsync(scope, PartnerVerificationSettingsKeys.TelegramOtpEnabled, "true");
            var db = CreateDbContext(scope);

            // الزبون والشريك فتحوا البوت وشاركوا أرقامهم (صيغة دولية)، والنظام عنده الأرقام المحلية
            var customer = await TestDataBuilder.CreateCustomerAsync(db, "أبو سامي", "0795550001");
            db.TelegramChatLinks.Add(new TelegramChatLink("962795550001", "chat-77", DateTime.UtcNow));
            db.TelegramChatLinks.Add(new TelegramChatLink("962791112222", "chat-88", DateTime.UtcNow));
            db.Coupons.Add(new Coupon("TG-TEST", "خصم تجربة", CouponDiscountType.FixedAmount, 0.500m, null, 0m,
                DateTime.UtcNow.AddHours(-1), DateTime.UtcNow.AddDays(7), customer.Id, 1, null));
            var partner = new Partner(Fixture.TestBranchId, "محمد الشريك", PartnerType.Capital, null, null, null);
            partner.SetTelegramPhone("0791112222");
            db.Partners.Add(partner);
            await db.SaveChangesAsync();
            partnerId = partner.Id;

            var bot = new TelegramBotClient(new HttpClient(api), scope.ServiceProvider.GetRequiredService<ISettingsProvider>());
            var context = scope.ServiceProvider.GetRequiredService<IApplicationDbContext>();
            var clock = scope.ServiceProvider.GetRequiredService<IDateTimeProvider>();
            var couponId = await db.Coupons.Where(c => c.Code == "TG-TEST").Select(c => c.Id).SingleAsync();

            var sent = await new SendCouponHandler(context, scope.ServiceProvider.GetRequiredService<ICustomerPushNotifier>(), bot, clock)
                .HandleAsync(new SendCouponCommand(couponId, null), CancellationToken.None);
            Assert.True(sent.IsSuccess, sent.Error?.Message);
            Assert.Equal(1, sent.Value.TelegramSent);

            var otp = await new RequestPartnerOtpHandler(context, scope.ServiceProvider.GetRequiredService<ICurrentUserContext>(),
                    scope.ServiceProvider.GetRequiredService<ISettingsProvider>(), bot, clock)
                .HandleAsync(new RequestPartnerOtpCommand(Fixture.TestBranchId, partnerId, 12.5m), CancellationToken.None);
            Assert.True(otp.IsSuccess, otp.Error?.Message);
        }

        Assert.Equal(2, api.Requests.Count);
        Assert.All(api.Requests, r => Assert.Equal("https://api.telegram.org/bot222:BBB/sendMessage", r.Url));

        var coupon = api.Requests[0].Body;
        Assert.Equal("chat-77", coupon.GetProperty("chat_id").GetString());
        Assert.Contains("TG-TEST", coupon.GetProperty("text").GetString());

        var code = api.Requests[1].Body;
        Assert.Equal("chat-88", code.GetProperty("chat_id").GetString());
        Assert.Contains("12.500", code.GetProperty("text").GetString());
        Assert.Matches(@"\d{6}", code.GetProperty("text").GetString());
    }
}
