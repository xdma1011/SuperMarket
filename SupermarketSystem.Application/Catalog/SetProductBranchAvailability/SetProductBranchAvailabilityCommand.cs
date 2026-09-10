using Microsoft.EntityFrameworkCore;
using SupermarketSystem.Application.Common.Interfaces;
using SupermarketSystem.Application.Common.Results;

namespace SupermarketSystem.Application.Catalog.SetProductBranchAvailability;

public sealed record SetProductBranchAvailabilityCommand(Guid ProductBranchId, bool IsAvailableForSale);

/// <summary>
/// كانت مفقودة بالكامل - ProductBranch.MakeAvailable/MakeUnavailable
/// موجودتان بالـDomain من البداية، بس ولا Command كان يستدعيهما، فما كان
/// فيه أي طريقة (ولا حتى عبر الـAPI مباشرة) توقف بيع منتج بفرع معيّن غير
/// SQL مباشر على القاعدة. هذا سبب خطأ Sale.ProductNotActive الغامض وقت
/// البيع - المنتج معلَّم IsAvailableForSale=false يدويًا بقاعدة البيانات،
/// بلا أي أثر بالواجهة يشرح ليش.
/// </summary>
public sealed class SetProductBranchAvailabilityHandler
{
    private readonly IApplicationDbContext _context;
    private readonly ICatalogVersionService _catalogVersionService;

    public SetProductBranchAvailabilityHandler(IApplicationDbContext context, ICatalogVersionService catalogVersionService)
    {
        _context = context;
        _catalogVersionService = catalogVersionService;
    }

    public async Task<Result> HandleAsync(SetProductBranchAvailabilityCommand command, CancellationToken cancellationToken)
    {
        var productBranch = await _context.ProductBranches
            .FirstOrDefaultAsync(pb => pb.Id == command.ProductBranchId, cancellationToken);

        if (productBranch is null)
        {
            return Result.Failure(Error.NotFound("ProductBranch.NotFound", $"الربط '{command.ProductBranchId}' غير موجود."));
        }

        if (command.IsAvailableForSale)
        {
            productBranch.MakeAvailable();
        }
        else
        {
            productBranch.MakeUnavailable();
        }

        await _context.SaveChangesAsync(cancellationToken);
        await _catalogVersionService.IncrementVersionAsync(cancellationToken);

        return Result.Success();
    }
}
