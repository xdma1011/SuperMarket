using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using SupermarketSystem.Application.Users.CreateUser;
using SupermarketSystem.Domain.Catalog;
using SupermarketSystem.Domain.Inventory;
using SupermarketSystem.Infrastructure.Persistence;
using SupermarketSystem.IntegrationTests;
using SupermarketSystem.IntegrationTests.Common;

namespace SupermarketSystem.CashierHeadlessTests.Support;

public sealed record SeededItem(Guid ProductId, Guid ProductBranchId, Guid BaseUnitId, string BaseBarcode, Guid? CartonUnitId, string? CartonBarcode);

/// <summary>بذر مباشر بالقاعدة (نفس أسلوب TestDataBuilder) - منتجات بباركود (والكرتونة بباركودها)، فرع، كاشيرات.</summary>
public static class StoreSeed
{
    public static readonly Guid CashierRoleId = Guid.Parse("f3b401c7-84f6-4a0f-9f17-b689979c5d8c");
    public const string CashierPassword = "Test_User_P@ss123!";

    public static async Task<Guid> CreateBranchAsync(DatabaseFixture fixture, string code)
    {
        using var scope = fixture.Factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        var branch = await TestDataBuilder.CreateBranchAsync(db, $"فرع {code}", code);
        await TestDataBuilder.EnsureDocumentSequencesProvisionedAsync(db, branch.Id);
        return branch.Id;
    }

    public static async Task<SeededItem> CreateItemAsync(
        DatabaseFixture fixture, Guid branchId, string name, decimal price, decimal stock, decimal? cartonFactor = null)
    {
        using var scope = fixture.Factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        var category = await TestDataBuilder.CreateCategoryAsync(db, $"تصنيف {name}");

        var product = new Product($"{name} {Guid.NewGuid().ToString("N")[..6]}", category.Id, isBatchTracked: false);
        product.ChangeStatus(ProductStatus.Active);
        var baseUnit = product.AddUnit("حبة", 1m, isBaseUnit: true);
        var baseBarcode = NewBarcode();
        product.AddBarcode(baseBarcode, baseUnit.Id);

        ProductUnit? carton = null;
        string? cartonBarcode = null;
        if (cartonFactor is { } factor)
        {
            carton = product.AddUnit("كرتونة", factor, isBaseUnit: false);
            cartonBarcode = NewBarcode();
            product.AddBarcode(cartonBarcode, carton.Id);
        }

        db.Products.Add(product);
        await db.SaveChangesAsync();

        var productBranch = await TestDataBuilder.CreateProductBranchAsync(db, product.Id, branchId, price);
        await TestDataBuilder.SetStockAsync(db, product.Id, branchId, stock);
        return new SeededItem(product.Id, productBranch.Id, baseUnit.Id, baseBarcode, carton?.Id, cartonBarcode);
    }

    public static async Task<string> CreateCashierAsync(DatabaseFixture fixture, Guid branchId, string prefix)
    {
        using var scope = fixture.Factory.Services.CreateScope();
        var handler = scope.ServiceProvider.GetRequiredService<CreateUserHandler>();
        var username = $"{prefix}.{Guid.NewGuid().ToString("N")[..10]}";
        var result = await handler.HandleAsync(new CreateUserCommand(
            FullName: $"كاشير {prefix}", Username: username, Email: $"{Guid.NewGuid():N}@test.local",
            Password: CashierPassword, RoleId: CashierRoleId, BranchId: branchId), CancellationToken.None);
        Assert.True(result.IsSuccess, result.Error?.Message);
        return username;
    }

    public static async Task<decimal> GetStockAsync(DatabaseFixture fixture, Guid productId, Guid branchId)
    {
        using var scope = fixture.Factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        return await db.Stocks.IgnoreQueryFilters().AsNoTracking()
            .Where(s => s.ProductId == productId && s.BranchId == branchId)
            .SumAsync(s => s.QuantityOnHand);
    }

    private static string NewBarcode() => "29" + Random.Shared.NextInt64(10_000_000_000, 99_999_999_999).ToString();
}
