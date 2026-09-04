using Microsoft.EntityFrameworkCore;
using SupermarketSystem.Application.Common.Interfaces;
using SupermarketSystem.Application.Common.Results;
using SupermarketSystem.Domain.Customers;

namespace SupermarketSystem.Application.Customers.FileComplaint;

/// <summary>⚠️ نفس تحذير PlaceOrderCommand - CustomerId هون لازم يجي من طبقة مصادقة حقيقية لاحقًا، مؤقتًا بلا تحقق.</summary>
public sealed record FileComplaintCommand(Guid CustomerId, Guid? OrderId, string Text);

public sealed record FileComplaintResponse(Guid ComplaintId);

public sealed class FileComplaintHandler
{
    private readonly IApplicationDbContext _context;

    public FileComplaintHandler(IApplicationDbContext context)
    {
        _context = context;
    }

    public async Task<Result<FileComplaintResponse>> HandleAsync(FileComplaintCommand command, CancellationToken cancellationToken)
    {
        if (string.IsNullOrWhiteSpace(command.Text))
        {
            return Result.Failure<FileComplaintResponse>(Error.Validation("Complaint.TextRequired", "نص الشكوى مطلوب."));
        }

        var customerExists = await _context.Customers.AsNoTracking().AnyAsync(c => c.Id == command.CustomerId, cancellationToken);
        if (!customerExists)
        {
            return Result.Failure<FileComplaintResponse>(Error.NotFound("Complaint.CustomerNotFound", $"الزبون '{command.CustomerId}' غير موجود."));
        }

        if (command.OrderId is { } orderId)
        {
            var orderExists = await _context.Orders.AsNoTracking().AnyAsync(o => o.Id == orderId, cancellationToken);
            if (!orderExists)
            {
                return Result.Failure<FileComplaintResponse>(Error.NotFound("Complaint.OrderNotFound", $"الطلب '{orderId}' غير موجود."));
            }
        }

        var complaint = new Complaint(command.CustomerId, command.OrderId, command.Text.Trim());
        _context.Complaints.Add(complaint);
        await _context.SaveChangesAsync(cancellationToken);

        return Result.Success(new FileComplaintResponse(complaint.Id));
    }
}
