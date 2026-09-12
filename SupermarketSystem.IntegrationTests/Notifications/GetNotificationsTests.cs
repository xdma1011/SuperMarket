using System.Net;
using System.Text.Json;
using Microsoft.Extensions.DependencyInjection;
using SupermarketSystem.Application.Common.Pagination;
using SupermarketSystem.Application.Notifications.GetNotifications;
using SupermarketSystem.Domain.Notifications;
using Xunit;

namespace SupermarketSystem.IntegrationTests.Notifications;

/// <summary>
/// Notification ليس كيانًا IBranchOwned - اختبار Handler مباشر بلا حاجة
/// سياق فرع. راجع أيضًا endpoint_محمي_401_بلا_توكن للتحقق من صلاحية
/// Notifications.View، وقيمة enum المُرسَلة فعليًا بالـJSON (راجع CLAUDE.md
/// §3.1 - النمط هون مختلف شوي: الـhandler بيرجّع NotificationChannel/
/// NotificationStatus كـenum مباشر لا Code+CodeTitle، فالتحقق هون من
/// النص الفعلي اللي JsonStringEnumConverter (المسجَّل بـProgram.cs) بيولّده).
/// </summary>
[Collection(DatabaseCollection.Name)]
public sealed class GetNotificationsTests : IntegrationTestBase
{
    public GetNotificationsTests(DatabaseFixture fixture) : base(fixture) { }

    [Fact]
    public async Task قائمة_الإشعارات_تعرض_قناة_InApp_فقط_وتستثني_باقي_القنوات()
    {
        using var scope = CreateScope();
        var db = CreateDbContext(scope);

        var inApp = new Notification(null, "إشعار داخلي", "رسالة", NotificationChannel.InApp);
        var telegram = new Notification(null, "إشعار تلغرام", "رسالة", NotificationChannel.Telegram);
        db.Notifications.AddRange(inApp, telegram);
        await db.SaveChangesAsync();

        var handler = scope.ServiceProvider.GetRequiredService<GetNotificationsHandler>();
        var result = await handler.HandleAsync(
            new GetNotificationsQuery(new PagedRequest { PageSize = 50 }, UnreadOnly: false), CancellationToken.None);

        Assert.Contains(result.Items, i => i.Id == inApp.Id);
        Assert.DoesNotContain(result.Items, i => i.Id == telegram.Id);
    }

    [Fact]
    public async Task فلتر_غير_مقروء_فقط_يستثني_الإشعار_المقروء()
    {
        using var scope = CreateScope();
        var db = CreateDbContext(scope);

        var unread = new Notification(null, "غير مقروء", "رسالة", NotificationChannel.InApp);
        var read = new Notification(null, "مقروء", "رسالة", NotificationChannel.InApp);
        read.MarkRead(DateTime.UtcNow);
        db.Notifications.AddRange(unread, read);
        await db.SaveChangesAsync();

        var handler = scope.ServiceProvider.GetRequiredService<GetNotificationsHandler>();
        var result = await handler.HandleAsync(
            new GetNotificationsQuery(new PagedRequest { PageSize = 50 }, UnreadOnly: true), CancellationToken.None);

        Assert.Contains(result.Items, i => i.Id == unread.Id);
        Assert.DoesNotContain(result.Items, i => i.Id == read.Id);
    }

    [Fact]
    public async Task مجموعة_الإشعارات_محمية_401_بلا_توكن_ومسموحة_بتوكن_صحيح_وقيمة_Channel_نص_وليست_رقمًا()
    {
        var anonymous = CreateAnonymousClient();
        var anonymousResponse = await anonymous.GetAsync("/api/v1/notifications");
        Assert.Equal(HttpStatusCode.Unauthorized, anonymousResponse.StatusCode);

        using (var scope = CreateScope())
        {
            var db = CreateDbContext(scope);
            db.Notifications.Add(new Notification(null, "إشعار للتحقق من التسلسل", "رسالة", NotificationChannel.InApp));
            await db.SaveChangesAsync();
        }

        var client = await CreateAuthenticatedClientAsync();
        var response = await client.GetAsync("/api/v1/notifications");
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);

        var json = JsonDocument.Parse(await response.Content.ReadAsStringAsync()).RootElement;
        var firstItem = json.GetProperty("items")[0];
        // القيمة المتوقَّعة فعليًا (JsonStringEnumConverter مسجَّل بـProgram.cs) -
        // نص اسم العضو ("InApp")، لا الرقم الخام (1).
        Assert.Equal("InApp", firstItem.GetProperty("channel").GetString());
    }
}
