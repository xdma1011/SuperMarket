using Microsoft.EntityFrameworkCore;
using SupermarketSystem.Application.Common.Interfaces;
using SupermarketSystem.Application.Common.Notifications;
using SupermarketSystem.Application.Partners;
using SupermarketSystem.Application.Common.Results;
using SupermarketSystem.Domain.Finance;
using SupermarketSystem.Domain.Identity;

namespace SupermarketSystem.Application.Finance.CreateCapitalTransaction;

/// <summary>
/// حركة رأس مال يدوية صريحة - راجع تعليق CapitalTransaction.cs بالـDomain:
/// لا ربط تلقائي بالربح الشهري، فعل إداري بحت (طلب صاحب المشروع 15-17/9/2026).
/// </summary>
public sealed record CreateCapitalTransactionCommand(
    Guid BranchId,
    CapitalTransactionType Type,
    decimal Amount,
    DateTime OccurredAtUtc,
    string? Notes,
    // رأس مال شريك (اختياري، 28/9/2026) - أساس نسبته من الربح بالكشف الشهري. لازم شريك رأس مال بنفس الفرع.
    Guid? PartnerId = null);

public sealed record CreateCapitalTransactionResponse(Guid CapitalTransactionId);

public static class CreateCapitalTransactionValidator
{
    public static Error? Validate(CreateCapitalTransactionCommand command)
    {
        if (command.BranchId == Guid.Empty)
        {
            return Error.Validation("CapitalTransaction.BranchRequired", "A branch is required.");
        }

        if (command.Amount <= 0)
        {
            return Error.Validation("CapitalTransaction.AmountInvalid", "Capital transaction amount must be positive.");
        }

        return null;
    }
}

public sealed class CreateCapitalTransactionHandler
{
    private readonly IApplicationDbContext _context;
    private readonly ICurrentUserContext _currentUser;
    private readonly IDateTimeProvider _dateTimeProvider;
    private readonly INotificationDispatcher _notificationDispatcher;

    public CreateCapitalTransactionHandler(
        IApplicationDbContext context, ICurrentUserContext currentUser, IDateTimeProvider dateTimeProvider,
        INotificationDispatcher notificationDispatcher)
    {
        _context = context;
        _currentUser = currentUser;
        _dateTimeProvider = dateTimeProvider;
        _notificationDispatcher = notificationDispatcher;
    }

    public async Task<Result<CreateCapitalTransactionResponse>> HandleAsync(
        CreateCapitalTransactionCommand command, CancellationToken cancellationToken)
    {
        var validationError = CreateCapitalTransactionValidator.Validate(command);
        if (validationError is not null)
        {
            return Result.Failure<CreateCapitalTransactionResponse>(validationError);
        }

        var branchExists = await _context.Branches.AsNoTracking().AnyAsync(b => b.Id == command.BranchId, cancellationToken);
        if (!branchExists)
        {
            return Result.Failure<CreateCapitalTransactionResponse>(
                Error.NotFound("CapitalTransaction.BranchNotFound", $"Branch '{command.BranchId}' was not found."));
        }

        if (command.PartnerId is { } partnerId)
        {
            var partner = await _context.Partners.AsNoTracking()
                .Where(p => p.Id == partnerId)
                .Select(p => new { p.BranchId, p.Type })
                .FirstOrDefaultAsync(cancellationToken);
            if (partner is null || partner.BranchId != command.BranchId)
            {
                return Result.Failure<CreateCapitalTransactionResponse>(
                    Error.Validation("CapitalTransaction.PartnerInvalid", "الشريك مش موجود بهالفرع."));
            }

            if (partner.Type != Domain.Partners.PartnerType.Capital)
            {
                return Result.Failure<CreateCapitalTransactionResponse>(
                    Error.Validation("CapitalTransaction.PartnerNotCapital", "الشريك المضارب ما إله رأس مال - نصيبه نسبة ثابتة."));
            }
        }

        var actorUserId = _currentUser.UserId ?? User.SystemUserId;

        CapitalTransaction transaction;
        try
        {
            transaction = new CapitalTransaction(
                command.BranchId, command.Type, command.Amount, command.OccurredAtUtc, command.Notes, actorUserId, command.PartnerId);
        }
        catch (Domain.Common.DomainException ex)
        {
            return Result.Failure<CreateCapitalTransactionResponse>(Error.Validation("CapitalTransaction.InvalidDetails", ex.Message));
        }

        _context.CapitalTransactions.Add(transaction);
        await _context.SaveChangesAsync(cancellationToken);

        // حركة رأس مال بتاريخ رجعي بتغيّر نسب الشركاء لكشوف انتهت بعد هالتاريخ (رأس المال بيتحسب آخر الشهر) - بند 2.
        var affectedMonths = await PartnerStatementStaleness.MonthsAffectedByCapitalAsync(
            _context, command.BranchId, command.OccurredAtUtc, cancellationToken);
        await PartnerStatementStaleness.MarkAndNotifyAsync(
            _context, _notificationDispatcher, _dateTimeProvider.UtcNow, command.BranchId, affectedMonths,
            $"انسجّلت حركة رأس مال ({command.Amount:0.000} د.أ) بتاريخ {command.OccurredAtUtc:yyyy-MM-dd} قبل نهاية هالشهر.", cancellationToken);

        return Result.Success(new CreateCapitalTransactionResponse(transaction.Id));
    }
}
