using Microsoft.EntityFrameworkCore;
using SupermarketSystem.Application.Common.Interfaces;
using SupermarketSystem.Application.Common.Results;
using SupermarketSystem.Domain.Branches;
using SupermarketSystem.Domain.Common;

namespace SupermarketSystem.Application.Branches.CreateBranch;

public sealed record CreateBranchCommand(
    string Name,
    string Code,
    string? PhoneNumber,
    string? Street,
    string? City,
    string? PostalCode,
    string? Country);

public sealed record CreateBranchResponse(Guid BranchId, string Code);

/// <summary>
/// Hand-rolled validator rather than FluentValidation. The brief asks for
/// validators, not for a specific library, and this keeps the dependency
/// surface minimal (brief §36: no premature abstractions). If validation
/// rules grow past simple shape checks, swapping in FluentValidation is a
/// contained change — nothing outside this class depends on how it works.
/// </summary>
public static class CreateBranchValidator
{
    private const int MaxNameLength = 200;
    private const int MaxCodeLength = 20;
    private const int MaxPhoneLength = 30;

    public static Error? Validate(CreateBranchCommand command)
    {
        if (string.IsNullOrWhiteSpace(command.Name))
        {
            return Error.Validation("Branch.NameRequired", "Branch name is required.");
        }

        if (command.Name.Length > MaxNameLength)
        {
            return Error.Validation("Branch.NameTooLong", $"Branch name cannot exceed {MaxNameLength} characters.");
        }

        if (string.IsNullOrWhiteSpace(command.Code))
        {
            return Error.Validation("Branch.CodeRequired", "Branch code is required.");
        }

        if (command.Code.Length > MaxCodeLength)
        {
            return Error.Validation("Branch.CodeTooLong", $"Branch code cannot exceed {MaxCodeLength} characters.");
        }

        if (command.PhoneNumber is { Length: > MaxPhoneLength })
        {
            return Error.Validation("Branch.PhoneTooLong", $"Phone number cannot exceed {MaxPhoneLength} characters.");
        }

        return null;
    }
}

/// <summary>
/// Creates a branch AND its document number sequences in a single atomic
/// transaction.
///
/// Why both together, and why this is the first use case built: DocumentNumberGenerator
/// deliberately throws rather than lazily creating a missing sequence row
/// (lazy creation would reintroduce the race between two concurrent first
/// sales that the whole numbering design exists to prevent). That makes
/// sequence provisioning a hard precondition for a branch ever being able to
/// sell anything — so it belongs here, in the same transaction, not in a
/// separate step an operator might forget.
///
/// A branch created without sequences would look completely fine until the
/// first sale at that branch failed. Binding them atomically makes that state
/// unreachable.
/// </summary>
public sealed class CreateBranchHandler
{
    private readonly IApplicationDbContext _context;

    public CreateBranchHandler(IApplicationDbContext context)
    {
        _context = context;
    }

    public async Task<Result<CreateBranchResponse>> HandleAsync(
        CreateBranchCommand command,
        CancellationToken cancellationToken)
    {
        var validationError = CreateBranchValidator.Validate(command);
        if (validationError is not null)
        {
            return Result.Failure<CreateBranchResponse>(validationError);
        }

        var normalizedCode = command.Code.Trim().ToUpperInvariant();

        // Pre-check for a friendly error. The DB unique index on Branch.Code
        // remains the real guarantee — this check can lose a race with a
        // concurrent request, which is exactly why the constraint exists and
        // why the API layer translates the resulting unique-violation into a
        // conflict response rather than a 500.
        var codeExists = await _context.Branches
            .AsNoTracking()
            .AnyAsync(b => b.Code == normalizedCode, cancellationToken);

        if (codeExists)
        {
            return Result.Failure<CreateBranchResponse>(
                Error.Conflict("Branch.CodeAlreadyExists", $"A branch with code '{normalizedCode}' already exists."));
        }

        var address = HasAnyAddressComponent(command)
            ? new Address(command.Street, command.City, command.PostalCode, command.Country)
            : null;

        var branch = new Branch(command.Name.Trim(), normalizedCode, address, command.PhoneNumber?.Trim());

        _context.Branches.Add(branch);

        // One sequence per document type. Enum.GetValues rather than a hard-
        // coded list, so adding a new numbered document type in the future
        // cannot silently leave existing branches unprovisioned.
        foreach (var documentType in Enum.GetValues<DocumentType>())
        {
            _context.BranchDocumentSequences.Add(new BranchDocumentSequence(branch.Id, documentType));
        }

        // Single SaveChangesAsync = single implicit transaction. Branch and
        // sequences commit together or not at all.
        await _context.SaveChangesAsync(cancellationToken);

        return Result.Success(new CreateBranchResponse(branch.Id, branch.Code));
    }

    private static bool HasAnyAddressComponent(CreateBranchCommand command)
        => !string.IsNullOrWhiteSpace(command.Street)
           || !string.IsNullOrWhiteSpace(command.City)
           || !string.IsNullOrWhiteSpace(command.PostalCode)
           || !string.IsNullOrWhiteSpace(command.Country);
}
