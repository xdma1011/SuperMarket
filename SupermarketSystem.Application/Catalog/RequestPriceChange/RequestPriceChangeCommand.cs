using Microsoft.EntityFrameworkCore;
using SupermarketSystem.Application.Common.Interfaces;
using SupermarketSystem.Application.Common.Results;
using SupermarketSystem.Domain.Catalog;
using SupermarketSystem.Domain.Identity;

namespace SupermarketSystem.Application.Catalog.RequestPriceChange;

public sealed record RequestPriceChangeCommand(Guid ProductBranchId, decimal RequestedPrice);

public sealed record RequestPriceChangeResponse(
    Guid RequestId,
    // applied=true يعني السعر اتغيّر فعليًا فورًا (المستخدم عنده Catalog.ChangePriceDirect)؛
    // applied=false يعني الطلب بانتظار موافقة صريحة (Catalog.RequestPriceChange بس).
    bool Applied);

/// <summary>
/// كانت مفقودة كليًا - القرار "مباشر أو بحاجة موافقة" يُحسم هون فعليًا
/// Runtime، مش endpoint منفصل لكل مستوى (راجع تعليق PriceChangeRequest.cs
/// بالـDomain للتصميم الكامل الثلاثي المستويات).
/// </summary>
public sealed class RequestPriceChangeHandler
{
    private readonly IApplicationDbContext _context;
    private readonly ICurrentUserContext _currentUser;
    private readonly IDateTimeProvider _dateTimeProvider;
    private readonly IPermissionChecker _permissionChecker;
    private readonly ICatalogVersionService _catalogVersionService;

    public RequestPriceChangeHandler(
        IApplicationDbContext context,
        ICurrentUserContext currentUser,
        IDateTimeProvider dateTimeProvider,
        IPermissionChecker permissionChecker,
        ICatalogVersionService catalogVersionService)
    {
        _context = context;
        _currentUser = currentUser;
        _dateTimeProvider = dateTimeProvider;
        _permissionChecker = permissionChecker;
        _catalogVersionService = catalogVersionService;
    }

    public async Task<Result<RequestPriceChangeResponse>> HandleAsync(RequestPriceChangeCommand command, CancellationToken cancellationToken)
    {
        if (command.RequestedPrice < 0)
        {
            return Result.Failure<RequestPriceChangeResponse>(
                Error.Validation("PriceChangeRequest.PriceNegative", "السعر المطلوب لا يمكن أن يكون سالبًا."));
        }

        var productBranch = await _context.ProductBranches.FirstOrDefaultAsync(pb => pb.Id == command.ProductBranchId, cancellationToken);
        if (productBranch is null)
        {
            return Result.Failure<RequestPriceChangeResponse>(
                Error.NotFound("PriceChangeRequest.ProductBranchNotFound", $"الربط '{command.ProductBranchId}' غير موجود."));
        }

        var actorUserId = _currentUser.UserId ?? User.SystemUserId;
        var occurredAtUtc = _dateTimeProvider.UtcNow;

        var request = new PriceChangeRequest(
            productBranch.Id, productBranch.SellingPrice, command.RequestedPrice, actorUserId, occurredAtUtc);

        var hasDirectPermission = _currentUser.UserId is { } userId
            && await _permissionChecker.HasPermissionAsync(userId, PermissionCodes.ChangeSellingPriceDirect, cancellationToken);

        if (hasDirectPermission)
        {
            productBranch.ChangePrice(command.RequestedPrice);
            request.AutoApprove(actorUserId, occurredAtUtc);
        }

        _context.PriceChangeRequests.Add(request);
        await _context.SaveChangesAsync(cancellationToken);

        if (hasDirectPermission)
        {
            await _catalogVersionService.IncrementVersionAsync(cancellationToken);
        }

        return Result.Success(new RequestPriceChangeResponse(request.Id, hasDirectPermission));
    }
}
