using System.Text.RegularExpressions;
using SupermarketSystem.Application.Common.Interfaces;
using SupermarketSystem.Application.Common.Results;
using SupermarketSystem.Domain.Common;
using SupermarketSystem.Domain.Purchasing;

namespace SupermarketSystem.Application.Purchasing.CreateSupplier;

public sealed record CreateSupplierCommand(
    string Name,
    string? ContactName,
    string? Phone,
    string? Email,
    string? Street,
    string? City,
    string? PostalCode,
    string? Country);

public sealed record CreateSupplierResponse(Guid SupplierId, string Name);

public static partial class CreateSupplierValidator
{
    private const int MaxNameLength = 200;
    private const int MaxContactNameLength = 200;
    private const int MaxPhoneLength = 30;
    private const int MaxEmailLength = 256;

    [GeneratedRegex(@"^[^@\s]+@[^@\s]+\.[^@\s]+$")]
    private static partial Regex EmailPattern();

    public static Error? Validate(CreateSupplierCommand command)
    {
        if (string.IsNullOrWhiteSpace(command.Name))
        {
            return Error.Validation("Supplier.NameRequired", "Supplier name is required.");
        }

        if (command.Name.Length > MaxNameLength)
        {
            return Error.Validation("Supplier.NameTooLong", $"Supplier name cannot exceed {MaxNameLength} characters.");
        }

        if (command.ContactName is { Length: > MaxContactNameLength })
        {
            return Error.Validation("Supplier.ContactNameTooLong", $"Contact name cannot exceed {MaxContactNameLength} characters.");
        }

        if (command.Phone is { Length: > MaxPhoneLength })
        {
            return Error.Validation("Supplier.PhoneTooLong", $"Phone number cannot exceed {MaxPhoneLength} characters.");
        }

        if (!string.IsNullOrWhiteSpace(command.Email))
        {
            if (command.Email.Length > MaxEmailLength)
            {
                return Error.Validation("Supplier.EmailTooLong", $"Email cannot exceed {MaxEmailLength} characters.");
            }

            if (!EmailPattern().IsMatch(command.Email))
            {
                return Error.Validation("Supplier.EmailInvalid", "Email is not a valid email address.");
            }
        }

        return null;
    }
}

public sealed class CreateSupplierHandler
{
    private readonly IApplicationDbContext _context;

    public CreateSupplierHandler(IApplicationDbContext context)
    {
        _context = context;
    }

    public async Task<Result<CreateSupplierResponse>> HandleAsync(CreateSupplierCommand command, CancellationToken cancellationToken)
    {
        var validationError = CreateSupplierValidator.Validate(command);
        if (validationError is not null)
        {
            return Result.Failure<CreateSupplierResponse>(validationError);
        }

        var hasAddress = !string.IsNullOrWhiteSpace(command.Street)
                          || !string.IsNullOrWhiteSpace(command.City)
                          || !string.IsNullOrWhiteSpace(command.PostalCode)
                          || !string.IsNullOrWhiteSpace(command.Country);

        var address = hasAddress ? new Address(command.Street, command.City, command.PostalCode, command.Country) : null;

        var supplier = new Supplier(
            command.Name.Trim(),
            command.ContactName?.Trim(),
            command.Phone?.Trim(),
            command.Email?.Trim(),
            address);

        _context.Suppliers.Add(supplier);

        await _context.SaveChangesAsync(cancellationToken);

        return Result.Success(new CreateSupplierResponse(supplier.Id, supplier.Name));
    }
}
