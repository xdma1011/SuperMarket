using Microsoft.EntityFrameworkCore;
using SupermarketSystem.Application.Common.Interfaces;
using SupermarketSystem.Application.Common.Results;

namespace SupermarketSystem.Application.Catalog.SetPromotionBranchActive;

/// <summary>
/// يشغّل/يوقف عرض قائم بفرع معيّن - لو الفرع ما عنده صف PromotionBranch
/// أصلًا (عرض أُنشئ لفروع تانية فقط)، ينشئه بالحالة المطلوبة مباشرة
/// (يغطّي "ضيف هذا الفرع للعرض" بنفس الـcommand، بلا حاجة لـcommand
/// منفصل). التوقيف لا يحذف الصف أبدًا - العرض جاهز يترجّع بضغطة وحدة.
/// </summary>
public sealed record SetPromotionBranchActiveCommand(Guid PromotionId, Guid BranchId, bool IsActive);

public sealed class SetPromotionBranchActiveHandler
{
    private readonly IApplicationDbContext _context;
    private readonly ICatalogVersionService _catalogVersionService;

    public SetPromotionBranchActiveHandler(IApplicationDbContext context, ICatalogVersionService catalogVersionService)
    {
        _context = context;
        _catalogVersionService = catalogVersionService;
    }

    public async Task<Result> HandleAsync(SetPromotionBranchActiveCommand command, CancellationToken cancellationToken)
    {
        var promotion = await _context.Promotions
            .Include(p => p.Branches)
            .FirstOrDefaultAsync(p => p.Id == command.PromotionId, cancellationToken);

        if (promotion is null)
        {
            return Result.Failure(Error.NotFound("Promotion.NotFound", $"Promotion '{command.PromotionId}' was not found."));
        }

        var branchExists = await _context.Branches.AsNoTracking().AnyAsync(b => b.Id == command.BranchId, cancellationToken);
        if (!branchExists)
        {
            return Result.Failure(Error.NotFound("Promotion.BranchNotFound", $"Branch '{command.BranchId}' was not found."));
        }

        var existingLink = promotion.Branches.FirstOrDefault(b => b.BranchId == command.BranchId);
        if (existingLink is null)
        {
            var newLink = promotion.AddBranch(command.BranchId);
            if (!command.IsActive)
            {
                newLink.Deactivate();
            }
        }
        else if (command.IsActive)
        {
            existingLink.Activate();
        }
        else
        {
            existingLink.Deactivate();
        }

        await _context.SaveChangesAsync(cancellationToken);
        await _catalogVersionService.IncrementVersionAsync(cancellationToken);
        return Result.Success();
    }
}
