using Microsoft.EntityFrameworkCore;
using SupermarketSystem.Application.Common.Interfaces;

namespace SupermarketSystem.Application.Common.Services;

public sealed class CustomerPushNotifier : ICustomerPushNotifier
{
    private readonly IApplicationDbContext _context;
    private readonly IPushNotificationSender _sender;

    public CustomerPushNotifier(IApplicationDbContext context, IPushNotificationSender sender)
    {
        _context = context;
        _sender = sender;
    }

    public async Task NotifyOrderStatusChangedAsync(Guid customerId, string title, string body, CancellationToken cancellationToken)
    {
        var deviceTokens = await _context.CustomerDeviceTokens.AsNoTracking()
            .Where(t => t.CustomerId == customerId)
            .Select(t => t.Token)
            .ToListAsync(cancellationToken);

        foreach (var token in deviceTokens)
        {
            // فشل جهاز واحد ما يوقف الباقي - راجع تعليق ICustomerPushNotifier.
            await _sender.SendAsync(token, title, body, cancellationToken);
        }
    }
}
