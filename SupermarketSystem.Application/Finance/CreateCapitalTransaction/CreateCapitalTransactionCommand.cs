using Microsoft.EntityFrameworkCore;
using SupermarketSystem.Application.Common.Interfaces;
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
    string? Notes);

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

    public CreateCapitalTransactionHandler(IApplicationDbContext context, ICurrentUserContext currentUser)
    {
        _context = context;
        _currentUser = currentUser;
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

        var actorUserId = _currentUser.UserId ?? User.SystemUserId;

        CapitalTransaction transaction;
        try
        {
            transaction = new CapitalTransaction(
                command.BranchId, command.Type, command.Amount, command.OccurredAtUtc, command.Notes, actorUserId);
        }
        catch (Domain.Common.DomainException ex)
        {
            return Result.Failure<CreateCapitalTransactionResponse>(Error.Validation("CapitalTransaction.InvalidDetails", ex.Message));
        }

        _context.CapitalTransactions.Add(transaction);
        await _context.SaveChangesAsync(cancellationToken);

        return Result.Success(new CreateCapitalTransactionResponse(transaction.Id));
    }
}
