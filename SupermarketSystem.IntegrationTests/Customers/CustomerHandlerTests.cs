using Microsoft.Extensions.DependencyInjection;
using SupermarketSystem.Application.Common.Results;
using SupermarketSystem.Application.Customers.BlockCustomer;
using SupermarketSystem.Application.Customers.FileComplaint;
using SupermarketSystem.Application.Customers.GetCustomerLoyaltyBalance;
using SupermarketSystem.Application.Customers.GetCustomerQrToken;
using SupermarketSystem.Application.Customers.GetCustomers;
using SupermarketSystem.Application.Customers.RedeemLoyaltyPoints;
using SupermarketSystem.Application.Customers.RegisterCustomerDeviceToken;
using SupermarketSystem.Application.Customers.ResolveCustomerQrToken;
using SupermarketSystem.Application.Customers.UnblockCustomer;
using SupermarketSystem.Domain.Customers;
using Xunit;

namespace SupermarketSystem.IntegrationTests.Customers;

/// <summary>
/// Customer ليس كيانًا IBranchOwned (راجع Customer.cs - زبون عام مشترك بكل
/// الفروع)، فاختباراته هون Handler مباشر بلا حاجة سياق مصادقة/فرع - نفس
/// نمط CreateProductCategoryTests المرجعي.
/// </summary>
[Collection(DatabaseCollection.Name)]
public sealed class CustomerHandlerTests : IntegrationTestBase
{
    public CustomerHandlerTests(DatabaseFixture fixture) : base(fixture) { }

    private async Task<Guid> SeedCustomerAsync(string phone = "0793000001")
    {
        using var scope = CreateScope();
        var db = CreateDbContext(scope);
        var customer = new Customer("زبون اختبار", phone, email: null);
        db.Customers.Add(customer);
        await db.SaveChangesAsync();
        return customer.Id;
    }

    [Fact]
    public async Task توليد_رمز_QR_لزبون_موجود_ينجح_ولزبون_غير_موجود_يفشل_بـNotFound()
    {
        var customerId = await SeedCustomerAsync();
        using var scope = CreateScope();
        var handler = scope.ServiceProvider.GetRequiredService<GetCustomerQrTokenHandler>();

        var success = await handler.HandleAsync(new GetCustomerQrTokenQuery(customerId), CancellationToken.None);
        Assert.True(success.IsSuccess);
        Assert.False(string.IsNullOrWhiteSpace(success.Value.QrToken));

        var notFound = await handler.HandleAsync(new GetCustomerQrTokenQuery(Guid.NewGuid()), CancellationToken.None);
        Assert.True(notFound.IsFailure);
        Assert.Equal(ErrorType.NotFound, notFound.Error!.Type);
    }

    [Fact]
    public async Task حل_رمز_QR_صحيح_يرجع_هوية_الزبون_ورمز_ملفَّق_يفشل_بخطأ_تحقق()
    {
        var customerId = await SeedCustomerAsync("0793000002");
        using var scope = CreateScope();
        var qrHandler = scope.ServiceProvider.GetRequiredService<GetCustomerQrTokenHandler>();
        var resolveHandler = scope.ServiceProvider.GetRequiredService<ResolveCustomerQrTokenHandler>();

        var token = (await qrHandler.HandleAsync(new GetCustomerQrTokenQuery(customerId), CancellationToken.None)).Value.QrToken;

        var resolved = await resolveHandler.HandleAsync(new ResolveCustomerQrTokenQuery(token), CancellationToken.None);
        Assert.True(resolved.IsSuccess);
        Assert.Equal(customerId, resolved.Value.CustomerId);

        var tampered = await resolveHandler.HandleAsync(new ResolveCustomerQrTokenQuery(token + "x"), CancellationToken.None);
        Assert.True(tampered.IsFailure);
        Assert.Equal(ErrorType.Validation, tampered.Error!.Type);
    }

    [Fact]
    public async Task استبدال_نقاط_ولاء_أكثر_من_الرصيد_يفشل_وبعد_الاكتساب_ينجح_ويحسب_الرصيد_صح()
    {
        var customerId = await SeedCustomerAsync("0793000003");
        using var scope = CreateScope();
        var db = CreateDbContext(scope);
        var redeemHandler = scope.ServiceProvider.GetRequiredService<RedeemLoyaltyPointsHandler>();
        var balanceHandler = scope.ServiceProvider.GetRequiredService<GetCustomerLoyaltyBalanceHandler>();

        var overRedeem = await redeemHandler.HandleAsync(new RedeemLoyaltyPointsCommand(customerId, 10), CancellationToken.None);
        Assert.True(overRedeem.IsFailure);
        Assert.Equal("Loyalty.InsufficientBalance", overRedeem.Error!.Code);

        db.CustomerLoyaltyPointsEntries.Add(new CustomerLoyaltyPointsEntry(
            customerId, 100, LoyaltyPointsReason.EarnedFromOrder, orderId: null, DateTime.UtcNow));
        await db.SaveChangesAsync();

        var redeemOk = await redeemHandler.HandleAsync(new RedeemLoyaltyPointsCommand(customerId, 30), CancellationToken.None);
        Assert.True(redeemOk.IsSuccess);

        var balance = await balanceHandler.HandleAsync(new GetCustomerLoyaltyBalanceQuery(customerId), CancellationToken.None);
        Assert.True(balance.IsSuccess);
        Assert.Equal(70, balance.Value.Balance);
    }

    [Fact]
    public async Task استبدال_نقاط_بعدد_سالب_أو_صفر_يفشل_بخطأ_تحقق()
    {
        var customerId = await SeedCustomerAsync("0793000004");
        using var scope = CreateScope();
        var handler = scope.ServiceProvider.GetRequiredService<RedeemLoyaltyPointsHandler>();

        var result = await handler.HandleAsync(new RedeemLoyaltyPointsCommand(customerId, 0), CancellationToken.None);
        Assert.True(result.IsFailure);
        Assert.Equal(ErrorType.Validation, result.Error!.Type);
    }

    [Fact]
    public async Task تسجيل_توكن_جهاز_جديد_ينجح_وإعادة_تسجيل_نفس_التوكن_لزبون_آخر_يستبدل_القديم()
    {
        var customer1 = await SeedCustomerAsync("0793000005");
        var customer2 = await SeedCustomerAsync("0793000006");

        using var scope = CreateScope();
        var handler = scope.ServiceProvider.GetRequiredService<RegisterCustomerDeviceTokenHandler>();
        var db = CreateDbContext(scope);

        const string token = "fcm-token-shared-device";

        var first = await handler.HandleAsync(
            new RegisterCustomerDeviceTokenCommand(customer1, token, DevicePlatform.Android), CancellationToken.None);
        Assert.True(first.IsSuccess);

        var second = await handler.HandleAsync(
            new RegisterCustomerDeviceTokenCommand(customer2, token, DevicePlatform.Ios), CancellationToken.None);
        Assert.True(second.IsSuccess);

        var rows = db.CustomerDeviceTokens.Where(t => t.Token == token).ToList();
        Assert.Single(rows);
        Assert.Equal(customer2, rows[0].CustomerId);
    }

    [Fact]
    public async Task حظر_زبون_ثم_رفع_الحظر_ينجحان_وزبون_غير_موجود_يفشل_بـNotFound()
    {
        var customerId = await SeedCustomerAsync("0793000007");
        using var scope = CreateScope();
        var blockHandler = scope.ServiceProvider.GetRequiredService<BlockCustomerHandler>();
        var unblockHandler = scope.ServiceProvider.GetRequiredService<UnblockCustomerHandler>();
        var db = CreateDbContext(scope);

        var blockResult = await blockHandler.HandleAsync(new BlockCustomerCommand(customerId), CancellationToken.None);
        Assert.True(blockResult.IsSuccess);
        Assert.True((await db.Customers.FindAsync(customerId))!.IsBlocked);

        var unblockResult = await unblockHandler.HandleAsync(new UnblockCustomerCommand(customerId), CancellationToken.None);
        Assert.True(unblockResult.IsSuccess);
        Assert.False((await db.Customers.FindAsync(customerId))!.IsBlocked);

        var notFound = await blockHandler.HandleAsync(new BlockCustomerCommand(Guid.NewGuid()), CancellationToken.None);
        Assert.True(notFound.IsFailure);
        Assert.Equal(ErrorType.NotFound, notFound.Error!.Type);
    }

    [Fact]
    public async Task قائمة_الزبائن_تدعم_البحث_بالاسم_أو_الهاتف()
    {
        await SeedCustomerAsync("0793000008");
        using var scope = CreateScope();
        var db = CreateDbContext(scope);
        db.Customers.Add(new Customer("محمد الأحمد", "0799999999", null));
        await db.SaveChangesAsync();

        var handler = scope.ServiceProvider.GetRequiredService<GetCustomersHandler>();
        var result = await handler.HandleAsync(
            new GetCustomersQuery(new SupermarketSystem.Application.Common.Pagination.PagedRequest { Search = "محمد" }),
            CancellationToken.None);

        Assert.Equal(1, result.TotalCount);
        Assert.Equal("محمد الأحمد", result.Items[0].FullName);
    }

    [Fact]
    public async Task تسجيل_شكوى_بنص_فاضي_يفشل_ولزبون_غير_موجود_يفشل_ولزبون_موجود_ينجح()
    {
        using var scope = CreateScope();
        var handler = scope.ServiceProvider.GetRequiredService<FileComplaintHandler>();

        var customerId = await SeedCustomerAsync("0793000009");

        var emptyText = await handler.HandleAsync(new FileComplaintCommand(customerId, null, "  "), CancellationToken.None);
        Assert.True(emptyText.IsFailure);
        Assert.Equal("Complaint.TextRequired", emptyText.Error!.Code);

        var missingCustomer = await handler.HandleAsync(new FileComplaintCommand(Guid.NewGuid(), null, "شكوى"), CancellationToken.None);
        Assert.True(missingCustomer.IsFailure);
        Assert.Equal(ErrorType.NotFound, missingCustomer.Error!.Type);

        var ok = await handler.HandleAsync(new FileComplaintCommand(customerId, null, "الطلب تأخر كثيرًا"), CancellationToken.None);
        Assert.True(ok.IsSuccess);
        Assert.NotEqual(Guid.Empty, ok.Value.ComplaintId);
    }
}
