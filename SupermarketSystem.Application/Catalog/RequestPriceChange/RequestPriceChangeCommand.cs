using Microsoft.EntityFrameworkCore;
using SupermarketSystem.Domain.Notifications;
using SupermarketSystem.Application.Common.Notifications;
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
    private readonly ITransactionalExecutor _transactionalExecutor;
    private readonly INotificationDispatcher _notificationDispatcher;

    public RequestPriceChangeHandler(
        IApplicationDbContext context,
        ICurrentUserContext currentUser,
        IDateTimeProvider dateTimeProvider,
        IPermissionChecker permissionChecker,
        ICatalogVersionService catalogVersionService,
        ITransactionalExecutor transactionalExecutor,
        INotificationDispatcher notificationDispatcher)
    {
        _transactionalExecutor = transactionalExecutor;
        _notificationDispatcher = notificationDispatcher;
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

        if (!hasDirectPermission)
        {
            _context.PriceChangeRequests.Add(request);
            await _context.SaveChangesAsync(cancellationToken);
            return Result.Success(new RequestPriceChangeResponse(request.Id, Applied: false));
        }

        // السعر ورقم النسخة بنفس المعاملة - راجع PriceChangeRequest.AppliedAtCatalogVersion.
        var applied = await _transactionalExecutor.ExecuteAsync<bool>(async ct =>
        {
            var catalogVersion = await _catalogVersionService.IncrementVersionAndGetAsync(ct);
            productBranch.ChangePrice(command.RequestedPrice);
            request.AutoApprove(actorUserId, occurredAtUtc);
            request.RecordAppliedCatalogVersion(catalogVersion);
            _context.PriceChangeRequests.Add(request);
            await _context.SaveChangesAsync(ct);
            return Result.Success(true);
        }, cancellationToken);

        if (applied.IsFailure)
        {
            return Result.Failure<RequestPriceChangeResponse>(applied.Error);
        }

        await NotifyIfDecreasedAsync(productBranch.Id, request.PreviousPrice, command.RequestedPrice, cancellationToken);

        return Result.Success(new RequestPriceChangeResponse(request.Id, hasDirectPermission));
    }

    /// <summary>تنزيل سعر بيع = الاتجاه الخطِر (بيع لصاحب بسعر أقل) - تنبيه بكل تنزيل، مش بالرفع.</summary>
    private async Task NotifyIfDecreasedAsync(Guid productBranchId, decimal oldPrice, decimal newPrice, CancellationToken cancellationToken)
    {
        if (newPrice >= oldPrice)
        {
            return;
        }

        var link = await _context.ProductBranches.AsNoTracking()
            .Where(pb => pb.Id == productBranchId)
            .Select(pb => new { pb.ProductId, pb.BranchId })
            .FirstAsync(cancellationToken);
        var productName = await AlertText.ProductNameAsync(_context, link.ProductId, cancellationToken);
        var branchName = await AlertText.BranchNameAsync(_context, link.BranchId, cancellationToken);
        var actor = await AlertText.UserNameAsync(_context, _currentUser.UserId, cancellationToken);
        var percent = oldPrice == 0 ? 0 : (oldPrice - newPrice) / oldPrice * 100m;

        await _notificationDispatcher.NotifyAsync(
            $"تنزيل سعر — {productName}",
            $"الفرع: {branchName}\nمن {oldPrice:0.000} إلى {newPrice:0.000} (-{percent:0.#}%)\nغيّره: {actor}",
            cancellationToken,
            NotificationSeverity.Warning);
    }
}
