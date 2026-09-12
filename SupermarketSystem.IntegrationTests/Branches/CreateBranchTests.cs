using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using SupermarketSystem.Application.Branches.CreateBranch;
using SupermarketSystem.Application.Common.Results;
using SupermarketSystem.Domain.Common;
using Xunit;

namespace SupermarketSystem.IntegrationTests.Branches;

/// <summary>
/// ملاحظة: جدول Branches مستثنى عمدًا من تصفير Respawn بـDatabaseFixture
/// (البيانات فيه تبقى بين تشغيلات dotnet test منفصلة) - أكواد الفروع هون
/// لازم تكون فريدة بكل تشغيلة (Guid) لا نصوص ثابتة، وإلا تشغيلة تانية
/// تصطدم بـConflict "الكود موجود أصلًا" من تشغيلة سابقة.
/// </summary>
[Collection(DatabaseCollection.Name)]
public sealed class CreateBranchTests : IntegrationTestBase
{
    public CreateBranchTests(DatabaseFixture fixture) : base(fixture) { }

    private static string UniqueCode(string prefix) => $"{prefix}{Guid.NewGuid():N}"[..12];

    [Fact]
    public async Task إنشاء_فرع_ينشئ_تسلسلات_ترقيم_المستندات_معه_بنفس_العملية()
    {
        using var scope = CreateScope();
        // BranchDocumentSequence كيان Branch-owned - المرشِّح العام بيحجب
        // القراءة المباشرة بلا HttpContext (راجع TestAuthContext).
        scope.ActAsCrossBranchUser();
        var handler = scope.ServiceProvider.GetRequiredService<CreateBranchHandler>();
        var code = UniqueCode("BR");

        var result = await handler.HandleAsync(
            new CreateBranchCommand("فرع جديد", code, "0790000000", "شارع الملك", "عمّان", null, "الأردن"),
            CancellationToken.None);

        Assert.True(result.IsSuccess);
        Assert.Equal(code.ToUpperInvariant(), result.Value.Code);

        var db = CreateDbContext(scope);
        var sequenceCount = await db.BranchDocumentSequences.AsNoTracking()
            .CountAsync(s => s.BranchId == result.Value.BranchId);

        // تسلسل واحد لكل نوع مستند (DocumentType) - بلا هذا أول فاتورة بيع
        // بهاد الفرع كانت رح تفشل بلا أي سبب واضح بالواجهة.
        Assert.Equal(Enum.GetValues<DocumentType>().Length, sequenceCount);
    }

    [Fact]
    public async Task إنشاء_فرع_بكود_مستخدم_أصلًا_يفشل_بـConflict()
    {
        using var scope = CreateScope();
        var handler = scope.ServiceProvider.GetRequiredService<CreateBranchHandler>();
        var code = UniqueCode("DUP");

        var first = await handler.HandleAsync(
            new CreateBranchCommand("فرع أول", code, null, null, null, null, null), CancellationToken.None);
        Assert.True(first.IsSuccess);

        var second = await handler.HandleAsync(
            new CreateBranchCommand("فرع ثاني", code.ToLowerInvariant(), null, null, null, null, null), CancellationToken.None);

        Assert.True(second.IsFailure);
        Assert.Equal(ErrorType.Conflict, second.Error!.Type);
        Assert.Equal("Branch.CodeAlreadyExists", second.Error.Code);
    }

    [Fact]
    public async Task إنشاء_فرع_باسم_فاضي_يفشل_بخطأ_تحقق()
    {
        using var scope = CreateScope();
        var handler = scope.ServiceProvider.GetRequiredService<CreateBranchHandler>();

        var result = await handler.HandleAsync(
            new CreateBranchCommand("   ", UniqueCode("CODE"), null, null, null, null, null), CancellationToken.None);

        Assert.True(result.IsFailure);
        Assert.Equal(ErrorType.Validation, result.Error!.Type);
        Assert.Equal("Branch.NameRequired", result.Error.Code);
    }
}
