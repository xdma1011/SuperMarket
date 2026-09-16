using Microsoft.EntityFrameworkCore;
using SupermarketSystem.Application.Common.Interfaces;
using SupermarketSystem.Application.Common.Results;

namespace SupermarketSystem.Application.Catalog.UpdatePromotion;

public sealed record UpdatePromotionCommand(
    Guid PromotionId,
    string Title,
    int BundleQuantity,
    decimal BundlePrice,
    decimal? MaxQuantityPerInvoice,
    DateTime StartAtUtc,
    DateTime EndAtUtc);

/// <summary>
/// تعديل تفاصيل عرض قائم - لا يأثر على الفواتير القديمة إطلاقًا
/// (Snapshot ثابت بـSaleInvoiceItem، راجع تعليق Promotion.cs) ولا على
/// ربط الفروع (فيه commands منفصلة لهذا - SetPromotionBranchActive).
/// </summary>
public sealed class UpdatePromotionHandler
{
    private readonly IApplicationDbContext _context;
    private readonly ICatalogVersionService _catalogVersionService;

    public UpdatePromotionHandler(IApplicationDbContext context, ICatalogVersionService catalogVersionService)
    {
        _context = context;
        _catalogVersionService = catalogVersionService;
    }

    public async Task<Result> HandleAsync(UpdatePromotionCommand command, CancellationToken cancellationToken)
    {
        var promotion = await _context.Promotions.FirstOrDefaultAsync(p => p.Id == command.PromotionId, cancellationToken);
        if (promotion is null)
        {
            return Result.Failure(Error.NotFound("Promotion.NotFound", $"Promotion '{command.PromotionId}' was not found."));
        }

        try
        {
            promotion.UpdateDetails(
                command.Title, command.BundleQuantity, command.BundlePrice,
                command.MaxQuantityPerInvoice, command.StartAtUtc, command.EndAtUtc);
        }
        catch (Domain.Common.DomainException ex)
        {
            return Result.Failure(Error.Validation("Promotion.InvalidDetails", ex.Message));
        }

        await _context.SaveChangesAsync(cancellationToken);
        await _catalogVersionService.IncrementVersionAsync(cancellationToken);
        return Result.Success();
    }
}
