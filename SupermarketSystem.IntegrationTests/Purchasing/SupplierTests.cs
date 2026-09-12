using Microsoft.Extensions.DependencyInjection;
using SupermarketSystem.Application.Common.Pagination;
using SupermarketSystem.Application.Common.Results;
using SupermarketSystem.Application.Purchasing.CreateSupplier;
using SupermarketSystem.Application.Purchasing.GetSuppliers;
using SupermarketSystem.Application.Purchasing.UpdateSupplier;
using SupermarketSystem.IntegrationTests.Common;
using Xunit;

namespace SupermarketSystem.IntegrationTests.Purchasing;

/// <summary>CreateSupplierHandler / UpdateSupplierHandler / GetSuppliersHandler.</summary>
[Collection(DatabaseCollection.Name)]
public sealed class SupplierTests : IntegrationTestBase
{
    public SupplierTests(DatabaseFixture fixture) : base(fixture) { }

    [Fact]
    public async Task إنشاء_مورد_بحقول_كاملة_ينجح()
    {
        using var scope = CreateScope();
        var handler = scope.ServiceProvider.GetRequiredService<CreateSupplierHandler>();

        var result = await handler.HandleAsync(
            new CreateSupplierCommand("شركة التوريد", "أبو محمد", "0790000001", "supplier@example.com", "شارع الملك", "عمان", "11190", "الأردن"),
            CancellationToken.None);

        Assert.True(result.IsSuccess);
        Assert.Equal("شركة التوريد", result.Value.Name);
    }

    [Fact]
    public async Task إنشاء_مورد_باسم_فاضي_يفشل_بخطأ_تحقق()
    {
        using var scope = CreateScope();
        var handler = scope.ServiceProvider.GetRequiredService<CreateSupplierHandler>();

        var result = await handler.HandleAsync(
            new CreateSupplierCommand("", null, null, null, null, null, null, null), CancellationToken.None);

        Assert.True(result.IsFailure);
        Assert.Equal(ErrorType.Validation, result.Error!.Type);
        Assert.Equal("Supplier.NameRequired", result.Error.Code);
    }

    [Fact]
    public async Task إنشاء_مورد_ببريد_غير_صحيح_يفشل_بخطأ_تحقق()
    {
        using var scope = CreateScope();
        var handler = scope.ServiceProvider.GetRequiredService<CreateSupplierHandler>();

        var result = await handler.HandleAsync(
            new CreateSupplierCommand("مورد ببريد غلط", null, null, "not-an-email", null, null, null, null), CancellationToken.None);

        Assert.True(result.IsFailure);
        Assert.Equal("Supplier.EmailInvalid", result.Error!.Code);
    }

    [Fact]
    public async Task تعديل_مورد_موجود_يحدّث_بياناته()
    {
        using var scope = CreateScope();
        var db = CreateDbContext(scope);
        var supplier = await TestDataBuilder.CreateSupplierAsync(db, "مورد قبل التعديل");

        var handler = scope.ServiceProvider.GetRequiredService<UpdateSupplierHandler>();
        var result = await handler.HandleAsync(
            new UpdateSupplierCommand(supplier.Id, "مورد بعد التعديل", "جهة اتصال جديدة", "0790000002", null),
            CancellationToken.None);

        Assert.True(result.IsSuccess);

        var updated = await db.Suppliers.FindAsync(supplier.Id);
        Assert.Equal("مورد بعد التعديل", updated!.Name);
    }

    [Fact]
    public async Task تعديل_مورد_غير_موجود_يفشل_بـNotFound()
    {
        using var scope = CreateScope();
        var handler = scope.ServiceProvider.GetRequiredService<UpdateSupplierHandler>();

        var result = await handler.HandleAsync(
            new UpdateSupplierCommand(Guid.NewGuid(), "اسم", null, null, null), CancellationToken.None);

        Assert.True(result.IsFailure);
        Assert.Equal(ErrorType.NotFound, result.Error!.Type);
    }

    [Fact]
    public async Task قائمة_الموردين_تدعم_البحث_بالاسم()
    {
        using var scope = CreateScope();
        var db = CreateDbContext(scope);
        await TestDataBuilder.CreateSupplierAsync(db, "شركة الألبان الأردنية");
        await TestDataBuilder.CreateSupplierAsync(db, "مؤسسة الحبوب");

        var handler = scope.ServiceProvider.GetRequiredService<GetSuppliersHandler>();
        var result = await handler.HandleAsync(
            new GetSuppliersQuery(new PagedRequest { Search = "الألبان" }), CancellationToken.None);

        var item = Assert.Single(result.Items);
        Assert.Equal("شركة الألبان الأردنية", item.Name);
    }
}
