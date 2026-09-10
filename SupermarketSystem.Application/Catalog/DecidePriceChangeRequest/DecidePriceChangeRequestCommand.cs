using Microsoft.EntityFrameworkCore;
using SupermarketSystem.Application.Common.Interfaces;
using SupermarketSystem.Application.Common.Results;
using SupermarketSystem.Domain.Catalog;
using SupermarketSystem.Domain.Identity;

namespace SupermarketSystem.Application.Catalog.DecidePriceChangeRequest;

public sealed record ApprovePriceChangeRequestCommand(Guid RequestId, string? Note);
public sealed record RejectPriceChangeRequestCommand(Guid RequestId, string? Note);

/// <summary>يوافق على طلب معلَّق - السعر يتغيّر فعليًا الآن فقط، لا وقت الطلب.</summary>
public sealed class ApprovePriceChangeRequestHandler
{
    private readonly IApplicationDbContext _context;
    private readonly ICurrentUserContext _currentUser;
    private readonly IDateTimeProvider _dateTimeProvider;
    private readonly ICatalogVersionService _catalogVersionService;

    public ApprovePriceChangeRequestHandler(
        IApplicationDbContext context, ICurrentUserContext currentUser,
        IDateTimeProvider dateTimeProvider, ICatalogVersionService catalogVersionService)
    {
        _context = context;
        _currentUser = currentUser;
        _dateTimeProvider = dateTimeProvider;
        _catalogVersionService = catalogVersionService;
    }

    public async Task<Result> HandleAsync(ApprovePriceChangeRequestCommand command, CancellationToken cancellationToken)
    {
        var request = await _context.PriceChangeRequests.FirstOrDefaultAsync(r => r.Id == command.RequestId, cancellationToken);
        if (request is null)
        {
            return Result.Failure(Error.NotFound("PriceChangeRequest.NotFound", $"الطلب '{command.RequestId}' غير موجود."));
        }

        if (request.Status != PriceChangeRequestStatus.Pending)
        {
            return Result.Failure(Error.Conflict("PriceChangeRequest.NotPending", "هذا الطلب تم البت فيه أصلًا."));
        }

        var productBranch = await _context.ProductBranches.FirstOrDefaultAsync(pb => pb.Id == request.ProductBranchId, cancellationToken);
        if (productBranch is null)
        {
            return Result.Failure(Error.NotFound("PriceChangeRequest.ProductBranchNotFound", "الربط المرتبط بهذا الطلب غير موجود."));
        }

        var actorUserId = _currentUser.UserId ?? User.SystemUserId;
        var occurredAtUtc = _dateTimeProvider.UtcNow;

        productBranch.ChangePrice(request.RequestedPrice);
        request.Approve(actorUserId, occurredAtUtc, command.Note);

        await _context.SaveChangesAsync(cancellationToken);
        await _catalogVersionService.IncrementVersionAsync(cancellationToken);

        return Result.Success();
    }
}

/// <summary>يرفض طلب معلَّق - السعر يضل كما هو، بلا أي تغيير.</summary>
public sealed class RejectPriceChangeRequestHandler
{
    private readonly IApplicationDbContext _context;
    private readonly ICurrentUserContext _currentUser;
    private readonly IDateTimeProvider _dateTimeProvider;

    public RejectPriceChangeRequestHandler(IApplicationDbContext context, ICurrentUserContext currentUser, IDateTimeProvider dateTimeProvider)
    {
        _context = context;
        _currentUser = currentUser;
        _dateTimeProvider = dateTimeProvider;
    }

    public async Task<Result> HandleAsync(RejectPriceChangeRequestCommand command, CancellationToken cancellationToken)
    {
        var request = await _context.PriceChangeRequests.FirstOrDefaultAsync(r => r.Id == command.RequestId, cancellationToken);
        if (request is null)
        {
            return Result.Failure(Error.NotFound("PriceChangeRequest.NotFound", $"الطلب '{command.RequestId}' غير موجود."));
        }

        if (request.Status != PriceChangeRequestStatus.Pending)
        {
            return Result.Failure(Error.Conflict("PriceChangeRequest.NotPending", "هذا الطلب تم البت فيه أصلًا."));
        }

        var actorUserId = _currentUser.UserId ?? User.SystemUserId;
        request.Reject(actorUserId, _dateTimeProvider.UtcNow, command.Note);

        await _context.SaveChangesAsync(cancellationToken);
        return Result.Success();
    }
}
