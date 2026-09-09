using Microsoft.EntityFrameworkCore;
using SupermarketSystem.Application.Common.Interfaces;
using SupermarketSystem.Application.Common.Results;
using SupermarketSystem.Domain.Common;

namespace SupermarketSystem.Application.Branches.UpdateBranch;

/// <summary>Code عمدًا غير موجود هون - راجع تعليق Branch.UpdateDetails.</summary>
public sealed record UpdateBranchCommand(Guid BranchId, string Name, string? PhoneNumber);

public sealed class UpdateBranchHandler
{
    private readonly IApplicationDbContext _context;

    public UpdateBranchHandler(IApplicationDbContext context)
    {
        _context = context;
    }

    public async Task<Result> HandleAsync(UpdateBranchCommand command, CancellationToken cancellationToken)
    {
        if (string.IsNullOrWhiteSpace(command.Name))
        {
            return Result.Failure(Error.Validation("Branch.NameRequired", "اسم الفرع مطلوب."));
        }

        var branch = await _context.Branches.FirstOrDefaultAsync(b => b.Id == command.BranchId, cancellationToken);
        if (branch is null)
        {
            return Result.Failure(Error.NotFound("Branch.NotFound", $"الفرع '{command.BranchId}' غير موجود."));
        }

        // العنوان الحالي (لو موجود) يبقى كما هو - هاي الشاشة الخفيفة ما
        // تعدّل العنوان، بس ما لازم تصفّره سهوًا.
        branch.UpdateDetails(command.Name.Trim(), command.PhoneNumber?.Trim(), branch.Address);

        await _context.SaveChangesAsync(cancellationToken);
        return Result.Success();
    }
}
