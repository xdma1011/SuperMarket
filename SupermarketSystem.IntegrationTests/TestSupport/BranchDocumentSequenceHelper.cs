using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using SupermarketSystem.Domain.Common;
using SupermarketSystem.Infrastructure.Persistence;

namespace SupermarketSystem.IntegrationTests;

/// <summary>
/// ملاحظة بنية اختبار مهمة (لا علاقة لها بكود الإنتاج - فروع حقيقية
/// تُنشأ دائمًا عبر CreateBranchHandler الحقيقي، اللي بيوفّر صفوف
/// BranchDocumentSequence أوتوماتيكيًا بنفس المعاملة - راجع تعليق
/// CreateBranchHandler): فرع الاختبار الثابت بـDatabaseFixture يُنشأ
/// مباشرة (`new Branch(...)`) بلا المرور بذاك الـhandler، فما عنده صفوف
/// تسلسل مستندات (BranchDocumentSequence) إطلاقًا. وبما إن هذا الجدول
/// **غير مستثنى** من تصفير Respawn بين الاختبارات (TablesToIgnore
/// بـDatabaseFixture ما يشمله)، أي اختبار يحتاج يُكمل بيعًا فعليًا
/// (CompleteSaleHandler - عبر مبيعات مباشرة أو CompleteOrder/CompleteDelivery)
/// لازم يستدعي هذا الهيلبر أول شي، وإلا DocumentNumberGenerator يرمي
/// InvalidOperationException ("No document sequence exists...").
/// </summary>
internal static class BranchDocumentSequenceHelper
{
    public static async Task EnsureProvisionedAsync(IServiceProvider services, Guid branchId)
    {
        using var scope = services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();

        var existingTypes = await db.BranchDocumentSequences
            .Where(s => s.BranchId == branchId)
            .Select(s => s.DocumentType)
            .ToListAsync();

        foreach (var documentType in Enum.GetValues<DocumentType>())
        {
            if (!existingTypes.Contains(documentType))
            {
                db.BranchDocumentSequences.Add(new BranchDocumentSequence(branchId, documentType));
            }
        }

        await db.SaveChangesAsync();
    }
}
