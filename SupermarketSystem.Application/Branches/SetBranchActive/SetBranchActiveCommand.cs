using Microsoft.EntityFrameworkCore;
using SupermarketSystem.Application.Common.Interfaces;
using SupermarketSystem.Application.Common.Results;

namespace SupermarketSystem.Application.Branches.SetBranchActive;

public sealed record SetBranchActiveCommand(Guid BranchId, bool IsActive);

/// <summary>إيقاف فرع - لا حذف (Branch أبدًا ما تُحذف فعليًا، راجع تعليق Branch.cs). فرع موقَف يبقى بكل تاريخه، بس ما يظهر كخيار جديد بنقاط بيع/طلبات.</summary>
public sealed class SetBranchActiveHandler
{
    private readonly IApplicationDbContext _context;

    public SetBranchActiveHandler(IApplicationDbContext context)
    {
        _context = context;
    }

    public async Task<Result> HandleAsync(SetBranchActiveCommand command, CancellationToken cancellationToken)
    {
        var branch = await _context.Branches.FirstOrDefaultAsync(b => b.Id == command.BranchId, cancellationToken);
        if (branch is null)
        {
            return Result.Failure(Error.NotFound("Branch.NotFound", $"الفرع '{command.BranchId}' غير موجود."));
        }

        if (command.IsActive)
        {
            branch.Activate();
        }
        else
        {
            branch.Deactivate();
        }

        await _context.SaveChangesAsync(cancellationToken);
        return Result.Success();
    }
}
