using Microsoft.EntityFrameworkCore;
using SupermarketSystem.Application.Common.Interfaces;
using SupermarketSystem.Application.Common.Results;

namespace SupermarketSystem.Application.Purchasing.UpdateSupplier;

public sealed record UpdateSupplierCommand(
    Guid SupplierId, string Name, string? ContactName, string? Phone, string? Email);

public sealed class UpdateSupplierHandler
{
    private readonly IApplicationDbContext _context;

    public UpdateSupplierHandler(IApplicationDbContext context)
    {
        _context = context;
    }

    public async Task<Result> HandleAsync(UpdateSupplierCommand command, CancellationToken cancellationToken)
    {
        if (string.IsNullOrWhiteSpace(command.Name))
        {
            return Result.Failure(Error.Validation("Supplier.NameRequired", "اسم المورد مطلوب."));
        }

        var supplier = await _context.Suppliers.FirstOrDefaultAsync(s => s.Id == command.SupplierId, cancellationToken);

        if (supplier is null)
        {
            return Result.Failure(Error.NotFound("Supplier.NotFound", $"المورد '{command.SupplierId}' غير موجود."));
        }

        supplier.UpdateDetails(command.Name.Trim(), command.ContactName, command.Phone, command.Email);
        await _context.SaveChangesAsync(cancellationToken);

        return Result.Success();
    }
}
