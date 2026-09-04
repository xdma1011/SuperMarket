using Microsoft.EntityFrameworkCore;
using SupermarketSystem.Application.Common.Interfaces;
using SupermarketSystem.Application.Common.Policies;
using SupermarketSystem.Application.Common.Results;
using SupermarketSystem.Domain.Customers;
using SupermarketSystem.Domain.Ordering;

namespace SupermarketSystem.Application.Ordering.PlaceOrder;

public sealed record PlaceOrderItemDto(Guid ProductId, Guid ProductUnitId, decimal Quantity);

/// <summary>
/// CustomerPhone هون *مؤقت وغير موثَّق* - أساس لتطبيق الزبائن قبل ما
/// يُبنى تسجيل دخول حقيقي (تحقق OTP عبر تلغرام، نقاش منفصل لسا ما
/// انبنى). لحد ما ينبني، أي استدعاء لهاي الـcommand لازم يكون محمي
/// بطبقة تحقق هوية خارجية (أو محصور باختبار داخلي) - ممنوع اعتماده
/// كمصدر ثقة وحيد بالإنتاج الفعلي.
/// </summary>
public sealed record PlaceOrderCommand(
    string CustomerPhone,
    string? CustomerName,
    Guid BranchId,
    string? DeliveryNote,
    decimal? DeliveryLatitude,
    decimal? DeliveryLongitude,
    IReadOnlyList<PlaceOrderItemDto> Items);

public sealed record PlaceOrderResponse(Guid OrderId, decimal EstimatedTotal);

public static class PlaceOrderValidator
{
    public static Error? Validate(PlaceOrderCommand command)
    {
        if (string.IsNullOrWhiteSpace(command.CustomerPhone))
        {
            return Error.Validation("Order.CustomerPhoneRequired", "رقم هاتف الزبون مطلوب.");
        }

        if (command.BranchId == Guid.Empty)
        {
            return Error.Validation("Order.BranchRequired", "الفرع مطلوب.");
        }

        if (command.Items.Count == 0)
        {
            return Error.Validation("Order.ItemsRequired", "لازم صنف واحد على الأقل.");
        }

        foreach (var item in command.Items)
        {
            if (item.ProductId == Guid.Empty || item.ProductUnitId == Guid.Empty)
            {
                return Error.Validation("Order.ItemProductRequired", "كل سطر يحتاج منتج ووحدة.");
            }

            if (item.Quantity <= 0)
            {
                return Error.Validation("Order.ItemQuantityInvalid", "الكمية يجب أن تكون موجبة.");
            }
        }

        return null;
    }
}

/// <summary>
/// يحل هوية الزبون بالرقم (ينشئ سجل Customer جديد لو أول مرة، وإلا
/// يعيد استخدام الموجود - راجع CustomerConfigurations.cs: الفهرس على
/// Phone غير Unique تصميميًا لأسباب POS قديمة، فالتفرّد هون يُفرَض على
/// مستوى هالـhandler بس، لا بقيد قاعدة بيانات). يرفض لو الرقم محظور.
///
/// السعر بكل سطر تقديري فقط (EstimatedUnitPrice = ProductBranch.SellingPrice
/// لحظة الطلب) - نفس مبدأ CompleteSaleCommand: السعر الملزِم الحقيقي
/// يُحسَم لاحقًا لحظة CompleteOrder، لا هون.
/// </summary>
public sealed class PlaceOrderHandler
{
    private readonly IApplicationDbContext _context;
    private readonly ISettingsProvider _settingsProvider;
    private readonly INotificationDispatcher _notificationDispatcher;
    private readonly IDateTimeProvider _dateTimeProvider;

    public PlaceOrderHandler(
        IApplicationDbContext context, ISettingsProvider settingsProvider,
        INotificationDispatcher notificationDispatcher, IDateTimeProvider dateTimeProvider)
    {
        _context = context;
        _settingsProvider = settingsProvider;
        _notificationDispatcher = notificationDispatcher;
        _dateTimeProvider = dateTimeProvider;
    }

    public async Task<Result<PlaceOrderResponse>> HandleAsync(PlaceOrderCommand command, CancellationToken cancellationToken)
    {
        var validationError = PlaceOrderValidator.Validate(command);
        if (validationError is not null)
        {
            return Result.Failure<PlaceOrderResponse>(validationError);
        }

        var orderingEnabled = await _settingsProvider.GetBoolAsync(OrderingPolicyKeys.Enabled, defaultValue: true, cancellationToken);
        if (!orderingEnabled)
        {
            return Result.Failure<PlaceOrderResponse>(
                Error.BusinessRule("Order.OrderingDisabled", "استقبال الطلبات متوقّف مؤقتًا - جرّب لاحقًا."));
        }

        var branchExists = await _context.Branches.AsNoTracking().AnyAsync(b => b.Id == command.BranchId, cancellationToken);
        if (!branchExists)
        {
            return Result.Failure<PlaceOrderResponse>(Error.NotFound("Order.BranchNotFound", $"الفرع '{command.BranchId}' غير موجود."));
        }

        var customer = await _context.Customers
            .FirstOrDefaultAsync(c => c.Phone == command.CustomerPhone && !c.IsDeleted, cancellationToken);

        if (customer is null)
        {
            customer = new Customer(
                string.IsNullOrWhiteSpace(command.CustomerName) ? command.CustomerPhone : command.CustomerName,
                command.CustomerPhone, email: null);
            _context.Customers.Add(customer);
        }
        else if (customer.IsBlocked)
        {
            return Result.Failure<PlaceOrderResponse>(
                Error.Forbidden("Order.CustomerBlocked", "لا يمكن تقديم طلب - هذا الرقم محظور."));
        }

        var productBranches = await _context.ProductBranches.AsNoTracking()
            .Where(pb => pb.BranchId == command.BranchId && command.Items.Select(i => i.ProductId).Contains(pb.ProductId))
            .Select(pb => new { pb.ProductId, pb.SellingPrice, pb.IsAvailableForSale })
            .ToDictionaryAsync(pb => pb.ProductId, cancellationToken);

        var unitIds = command.Items.Select(i => i.ProductUnitId).Distinct().ToList();
        var validUnitIds = await _context.ProductUnits.AsNoTracking()
            .Where(u => unitIds.Contains(u.Id))
            .Select(u => new { u.Id, u.ProductId })
            .ToListAsync(cancellationToken);

        var order = new Order(customer.Id, command.BranchId, command.DeliveryNote, command.DeliveryLatitude, command.DeliveryLongitude);
        var estimatedTotal = 0m;

        foreach (var itemDto in command.Items)
        {
            if (!productBranches.TryGetValue(itemDto.ProductId, out var productBranch) || !productBranch.IsAvailableForSale)
            {
                return Result.Failure<PlaceOrderResponse>(Error.Validation(
                    "Order.ProductNotAvailable", $"المنتج '{itemDto.ProductId}' غير متوفر بهذا الفرع."));
            }

            var unitBelongsToProduct = validUnitIds.Any(u => u.Id == itemDto.ProductUnitId && u.ProductId == itemDto.ProductId);
            if (!unitBelongsToProduct)
            {
                return Result.Failure<PlaceOrderResponse>(Error.Validation(
                    "Order.UnitProductMismatch", $"الوحدة '{itemDto.ProductUnitId}' لا تخص المنتج '{itemDto.ProductId}'."));
            }

            order.AddItem(itemDto.ProductId, itemDto.ProductUnitId, itemDto.Quantity, productBranch.SellingPrice);
            estimatedTotal += itemDto.Quantity * productBranch.SellingPrice;
        }

        var minimumOrderAmount = await _settingsProvider.GetDecimalAsync(OrderingPolicyKeys.MinimumOrderAmount, defaultValue: 0m, cancellationToken);
        if (minimumOrderAmount > 0 && estimatedTotal < minimumOrderAmount)
        {
            return Result.Failure<PlaceOrderResponse>(Error.Validation(
                "Order.BelowMinimumAmount", $"أقل مبلغ مسموح للطلب {minimumOrderAmount:F2} - إجمالي طلبك الحالي {estimatedTotal:F2}."));
        }

        _context.Orders.Add(order);
        await _context.SaveChangesAsync(cancellationToken);

        // مؤشر إساءة استخدام محتملة - تنبيه لصاحب المشروع بس، ما بيمنع
        // الطلب إطلاقًا (نفس فلسفة النظام "سماح مع مراجعة").
        var alertThreshold = await _settingsProvider.GetDecimalAsync(OrderingPolicyKeys.DailyOrderCountAlertThreshold, defaultValue: 5m, cancellationToken);
        if (alertThreshold > 0)
        {
            var todayUtc = DateOnly.FromDateTime(_dateTimeProvider.UtcNow);
            var todaysOrderCount = await _context.Orders.AsNoTracking()
                .CountAsync(o => o.CustomerId == customer.Id && o.CreatedAtUtc.Date == todayUtc.ToDateTime(TimeOnly.MinValue), cancellationToken);

            if (todaysOrderCount >= alertThreshold)
            {
                await _notificationDispatcher.NotifyAsync(
                    "تكرار طلبات مرتفع",
                    $"الزبون '{customer.FullName}' ({command.CustomerPhone}) قدّم {todaysOrderCount} طلب اليوم - راجع نشاطه.",
                    cancellationToken);
            }
        }

        return Result.Success(new PlaceOrderResponse(order.Id, estimatedTotal));
    }
}
