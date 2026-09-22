using System.Security.Claims;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.DependencyInjection;
using SupermarketSystem.Application.CashManagement.CompleteCashClosing;
using SupermarketSystem.Application.Common.Pagination;
using SupermarketSystem.Application.Reporting.GetCashierVarianceReport;
using SupermarketSystem.Application.Sales.CompleteSale;
using SupermarketSystem.Domain.Identity;
using SupermarketSystem.Infrastructure.Services;
using SupermarketSystem.IntegrationTests.Common;
using Xunit;

namespace SupermarketSystem.IntegrationTests.Reporting;

/// <summary>
/// GetCashierVarianceReportHandler — الهدف كشف نمط عجز/زيادة متكرر لكل
/// كاشير عبر عدة تقفيلات، لا حادثة معزولة واحدة.
/// </summary>
[Collection(DatabaseCollection.Name)]
public sealed class GetCashierVarianceReportTests : IntegrationTestBase
{
    public GetCashierVarianceReportTests(DatabaseFixture fixture) : base(fixture) { }

    [Fact]
    public async Task عجز_كاشير_وزيادة_كاشير_آخر_يظهران_مجمَّعين_كلٌّ_لحاله()
    {
        Guid secondCashierId;
        string secondCashierUsername;

        // ICurrentUserContext يلتقط الـHttpContext لحظة إنشائه بالـScope
        // مرة وحدة (لقطة لا قراءة حية - راجع تعليق ActAsAdminAsync) - يعني
        // تغيير المستخدم "الحالي" بمنتصف نفس الـScope ما بينعكس على أي
        // Handler اتحلّ منه سابقًا. لازم Scope منفصل لكل ممثّل (نفس نمط
        // ProcessReturnTests الموثَّق).
        using (var setupScope = CreateScope())
        {
            await TestDataBuilder.ActAsAdminAsync(setupScope, Fixture);
            var db = CreateDbContext(setupScope);

            var (product, unit) = await TestDataBuilder.CreateActiveProductAsync(db, "منتج فروقات الصندوق");
            await TestDataBuilder.CreateProductBranchAsync(db, product.Id, Fixture.TestBranchId, 25m);
            await TestDataBuilder.SetStockAsync(db, product.Id, Fixture.TestBranchId, 10m);

            var saleHandler = setupScope.ServiceProvider.GetRequiredService<CompleteSaleHandler>();
            var sale = await saleHandler.HandleAsync(
                new CompleteSaleCommand(
                    Fixture.TestBranchId, Guid.NewGuid(), null, 0m,
                    new[] { new CompleteSaleItemDto(product.Id, unit.Id, 2m, 0m, null) },
                    new[] { new CompleteSalePaymentDto(TestDataBuilder.CashPaymentMethodId, 50m, null, Guid.NewGuid()) }),
                CancellationToken.None);
            Assert.True(sale.IsSuccess);

            // كاشير 1 (Admin) - عجز 10 (عدّ 40 مقابل متوقّع 50).
            var closingHandler = setupScope.ServiceProvider.GetRequiredService<CompleteCashClosingHandler>();
            var deficitClosing = await closingHandler.HandleAsync(
                new CompleteCashClosingCommand(
                    Fixture.TestBranchId, DateOnly.FromDateTime(DateTime.UtcNow), CountedCash: 40m,
                    CountedDetails: Array.Empty<CompleteCashClosingCountDto>()),
                CancellationToken.None);
            Assert.True(deficitClosing.IsSuccess);

            var uniqueSuffix = Guid.NewGuid().ToString("N")[..8];
            var secondCashier = new User(
                "كاشير الوردية الثانية", $"shift2-cashier-{uniqueSuffix}", $"shift2-{uniqueSuffix}@test.local");
            db.Users.Add(secondCashier);
            await db.SaveChangesAsync();
            secondCashierId = secondCashier.Id;
            secondCashierUsername = secondCashier.Username;
        }

        // كاشير 2 (مستخدم جديد، Scope منفصل) - زيادة 10 (عدّ 10 مقابل
        // متوقّع 0 - بلا مبيعات جديدة منذ آخر تقفيل)، بنفس اليوم/الفرع لكن
        // وردية مختلفة.
        using (var secondScope = CreateScope())
        {
            var accessor = secondScope.ServiceProvider.GetRequiredService<IHttpContextAccessor>();
            var identity = new ClaimsIdentity(new[]
            {
                new Claim(ClaimTypes.NameIdentifier, secondCashierId.ToString()),
                new Claim(JwtTokenService.BranchIdClaim, Fixture.TestBranchId.ToString()),
                new Claim(JwtTokenService.CrossBranchClaim, "true")
            }, authenticationType: "TestFakeAuth");
            accessor.HttpContext = new DefaultHttpContext { User = new ClaimsPrincipal(identity) };

            var closingHandler = secondScope.ServiceProvider.GetRequiredService<CompleteCashClosingHandler>();
            var surplusClosing = await closingHandler.HandleAsync(
                new CompleteCashClosingCommand(
                    Fixture.TestBranchId, DateOnly.FromDateTime(DateTime.UtcNow), CountedCash: 10m,
                    CountedDetails: Array.Empty<CompleteCashClosingCountDto>(), ShiftNumber: 2),
                CancellationToken.None);
            Assert.True(surplusClosing.IsSuccess);
        }

        using var reportScope = CreateScope();
        var reportHandler = reportScope.ServiceProvider.GetRequiredService<GetCashierVarianceReportHandler>();
        var result = await reportHandler.HandleAsync(
            new GetCashierVarianceReportQuery(
                new PagedRequest(), Fixture.TestBranchId,
                DateTime.UtcNow.AddDays(-1), DateTime.UtcNow.AddDays(1)),
            CancellationToken.None);

        Assert.Equal(2, result.Items.Count);

        var deficitRow = Assert.Single(result.Items, i => i.UserId == Fixture.AdminUserId);
        Assert.Equal(1, deficitRow.DeficitCount);
        Assert.Equal(0, deficitRow.SurplusCount);
        Assert.Equal(-10m, deficitRow.TotalVariance);

        var surplusRow = Assert.Single(result.Items, i => i.UserId == secondCashierId);
        Assert.Equal(0, surplusRow.DeficitCount);
        Assert.Equal(1, surplusRow.SurplusCount);
        Assert.Equal(10m, surplusRow.TotalVariance);
        Assert.Equal(secondCashierUsername, surplusRow.Username);

        // مرتَّب تصاعديًا حسب TotalVariance - الأكتر عجزًا أولًا.
        Assert.Equal(deficitRow.UserId, result.Items[0].UserId);
    }

    [Fact]
    public async Task تقفيل_بلا_فرق_لا_يُحتسب_عجزًا_ولا_زيادة()
    {
        using var scope = CreateScope();
        await TestDataBuilder.ActAsAdminAsync(scope, Fixture);
        var db = CreateDbContext(scope);

        var closingHandler = scope.ServiceProvider.GetRequiredService<CompleteCashClosingHandler>();
        var closing = await closingHandler.HandleAsync(
            new CompleteCashClosingCommand(
                Fixture.TestBranchId, DateOnly.FromDateTime(DateTime.UtcNow), CountedCash: 0m,
                CountedDetails: Array.Empty<CompleteCashClosingCountDto>()),
            CancellationToken.None);
        Assert.True(closing.IsSuccess);

        var reportHandler = scope.ServiceProvider.GetRequiredService<GetCashierVarianceReportHandler>();
        var result = await reportHandler.HandleAsync(
            new GetCashierVarianceReportQuery(
                new PagedRequest(), Fixture.TestBranchId,
                DateTime.UtcNow.AddDays(-1), DateTime.UtcNow.AddDays(1)),
            CancellationToken.None);

        var row = Assert.Single(result.Items);
        Assert.Equal(0, row.DeficitCount);
        Assert.Equal(0, row.SurplusCount);
        Assert.Equal(0m, row.TotalVariance);
    }
}
