using Microsoft.EntityFrameworkCore;
using SupermarketSystem.Application.Common.Interfaces;

namespace SupermarketSystem.Application.Branches.GetPublicBranches;

public sealed record PublicBranchDto(Guid Id, string Name);

/// <summary>
/// منفصل عمدًا عن GetBranchesHandler (الإداري، خلف صلاحية
/// Branches.Manage). هذا الاستعلام مُعفى من المصادقة كليًا — الغرض
/// الوحيد منه تعبئة قائمة اختيار الفرع بصفحة تسجيل الدخول *قبل* ما يكون
/// عند المستخدم توكن أصلًا. يكشف الاسم فقط، أقل بيانات ممكنة تكفي لهدف
/// "اختر فرعك".
/// </summary>
public sealed class GetPublicBranchesHandler
{
    private readonly IApplicationDbContext _context;

    public GetPublicBranchesHandler(IApplicationDbContext context)
    {
        _context = context;
    }

    public async Task<IReadOnlyList<PublicBranchDto>> HandleAsync(CancellationToken cancellationToken)
    {
        return await _context.Branches.AsNoTracking()
            .Where(b => b.IsActive)
            .OrderBy(b => b.Name)
            .Select(b => new PublicBranchDto(b.Id, b.Name))
            .ToListAsync(cancellationToken);
    }
}
