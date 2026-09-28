using Microsoft.EntityFrameworkCore;
using SupermarketSystem.Application.Common.Interfaces;
using SupermarketSystem.Application.Common.Results;
using SupermarketSystem.Domain.Sales;

namespace SupermarketSystem.Application.Sales.PreparedOrders;

// =====================================================================================
// مساعد الكاشير (28/9/2026، فكرة صاحب المشروع): وقت الزحمة، المساعد/الشريك بيحط الأغراض بالكيس
// وبيضربها من تلفونه (صفحة "تجهيز طلب" بلوحة الإدارة) - بيطلعله رقم طلب قصير. الكاشير بينزّل
// الطلب بالسلة (زر "طلبات جاهزة") وبيحاسب هو على المصاري الحقيقية. الطلب نفسه ما إله أي أثر مالي
// ولا مخزني - كله بيصير لحظة البيع الفعلي (CompleteSaleCommand.PreparedOrderId). مبني على
// SuspendedSale الموجود من التصميم الأصلي (كان جدول بلا استخدام).
// =====================================================================================

public sealed record PreparedOrderItemRequest(Guid ProductId, Guid ProductUnitId, decimal Quantity);

public sealed record CreatePreparedOrderCommand(Guid BranchId, string? Note, IReadOnlyList<PreparedOrderItemRequest> Items);

public sealed record CreatePreparedOrderResponse(Guid PreparedOrderId, int TicketNumber, int ItemCount, decimal EstimatedTotal);

public sealed record PreparedOrderItemDto(
    Guid ProductId, Guid ProductUnitId, string ProductName, string UnitName, decimal Quantity, decimal UnitPriceSnapshot);

public sealed record PreparedOrderDto(
    Guid Id, int TicketNumber, string? Note, string PreparedByName, DateTime CreatedAtUtc,
    decimal EstimatedTotal, IReadOnlyList<PreparedOrderItemDto> Items);

public sealed record GetOpenPreparedOrdersQuery(Guid? BranchId);

public sealed record CancelPreparedOrderCommand(Guid PreparedOrderId);

public sealed record ScanLookupQuery(Guid BranchId, string Term);

/// <summary>منتج بسعر فرعه - نتيجة ضرب باركود أو بحث بالاسم بصفحة التلفون.</summary>
public sealed record ScanLookupItemDto(
    Guid ProductId, Guid ProductUnitId, string ProductName, string UnitName, decimal UnitPrice, bool IsBatchTracked, string? Barcode);

public sealed class CreatePreparedOrderHandler
{
    public const int MaxItems = 200;
    private const int MaxTicketAttempts = 3;

    private readonly IApplicationDbContext _context;
    private readonly ICurrentUserContext _currentUser;
    private readonly IDateTimeProvider _dateTimeProvider;

    public CreatePreparedOrderHandler(IApplicationDbContext context, ICurrentUserContext currentUser, IDateTimeProvider dateTimeProvider)
    {
        _context = context;
        _currentUser = currentUser;
        _dateTimeProvider = dateTimeProvider;
    }

    public async Task<Result<CreatePreparedOrderResponse>> HandleAsync(CreatePreparedOrderCommand command, CancellationToken cancellationToken)
    {
        if (command.BranchId == Guid.Empty)
        {
            return Result.Failure<CreatePreparedOrderResponse>(Error.Validation("PreparedOrder.BranchRequired", "الفرع مطلوب."));
        }

        if (command.Items.Count == 0)
        {
            return Result.Failure<CreatePreparedOrderResponse>(Error.Validation("PreparedOrder.ItemsRequired", "الطلب فاضي - اضرب صنف واحد على الأقل."));
        }

        if (command.Items.Count > MaxItems)
        {
            return Result.Failure<CreatePreparedOrderResponse>(
                Error.Validation("PreparedOrder.TooManyItems", $"الطلب فيه أكتر من {MaxItems} سطر."));
        }

        if (command.Items.Any(i => i.ProductId == Guid.Empty || i.ProductUnitId == Guid.Empty || i.Quantity <= 0))
        {
            return Result.Failure<CreatePreparedOrderResponse>(
                Error.Validation("PreparedOrder.ItemInvalid", "كل سطر لازم يكون إله منتج ووحدة وكمية أكبر من صفر."));
        }

        if (command.Note is { Length: > SuspendedSale.MaxNoteLength })
        {
            return Result.Failure<CreatePreparedOrderResponse>(
                Error.Validation("PreparedOrder.NoteTooLong", $"الملاحظة أطول من {SuspendedSale.MaxNoteLength} حرف."));
        }

        if (!_currentUser.IsCrossBranchAccessAllowed && _currentUser.BranchId != command.BranchId)
        {
            return Result.Failure<CreatePreparedOrderResponse>(
                Error.Forbidden("PreparedOrder.BranchNotAllowed", "ما بتقدر تجهّز طلب لفرع غير فرعك."));
        }

        var userId = _currentUser.UserId
            ?? throw new InvalidOperationException("لا يمكن تجهيز طلب بلا هوية مستخدم مصادَق عليها.");

        var productIds = command.Items.Select(i => i.ProductId).Distinct().ToList();
        var unitIds = command.Items.Select(i => i.ProductUnitId).Distinct().ToList();

        var units = await _context.ProductUnits.AsNoTracking()
            .Where(u => unitIds.Contains(u.Id))
            .Select(u => new { u.Id, u.ProductId, u.ConversionFactorToBase })
            .ToDictionaryAsync(u => u.Id, cancellationToken);

        var prices = await _context.ProductBranches.AsNoTracking()
            .Where(pb => pb.BranchId == command.BranchId && productIds.Contains(pb.ProductId))
            .Select(pb => new { pb.ProductId, pb.SellingPrice, pb.IsAvailableForSale })
            .ToDictionaryAsync(pb => pb.ProductId, cancellationToken);

        var productNames = await _context.Products.AsNoTracking()
            .Where(p => productIds.Contains(p.Id))
            .ToDictionaryAsync(p => p.Id, p => p.Name, cancellationToken);

        var lines = new List<(PreparedOrderItemRequest Item, decimal UnitPrice)>();
        foreach (var item in command.Items)
        {
            if (!productNames.TryGetValue(item.ProductId, out var productName)
                || !units.TryGetValue(item.ProductUnitId, out var unit) || unit.ProductId != item.ProductId)
            {
                return Result.Failure<CreatePreparedOrderResponse>(
                    Error.NotFound("PreparedOrder.ProductNotFound", "في صنف بالطلب مش موجود (أو وحدته غلط)."));
            }

            if (!prices.TryGetValue(item.ProductId, out var price) || !price.IsAvailableForSale)
            {
                return Result.Failure<CreatePreparedOrderResponse>(
                    Error.BusinessRule("PreparedOrder.ProductNotAtBranch", $"'{productName}' مش متاح للبيع بهالفرع."));
            }

            lines.Add((item, price.SellingPrice * unit.ConversionFactorToBase));
        }

        var nowUtc = _dateTimeProvider.UtcNow;
        var ticketDate = DateOnly.FromDateTime(nowUtc);

        // رقم الطلب = أكبر رقم اليوم + 1، والفهرس الفريد (فرع، يوم، رقم) هو الضمان الحقيقي - طلبين بنفس
        // اللحظة: التاني بيفشل بالحفظ وبيعيد برقم جديد.
        for (var attempt = 1; ; attempt++)
        {
            var lastTicket = await _context.SuspendedSales.IgnoreQueryFilters().AsNoTracking()
                .Where(s => s.BranchId == command.BranchId && s.TicketDateUtc == ticketDate)
                .MaxAsync(s => (int?)s.TicketNumber, cancellationToken) ?? 0;

            var order = new SuspendedSale(command.BranchId, userId, lastTicket + 1, ticketDate, command.Note);
            foreach (var (item, unitPrice) in lines)
            {
                order.AddItem(item.ProductId, item.ProductUnitId, item.Quantity, unitPrice);
            }

            _context.SuspendedSales.Add(order);
            try
            {
                await _context.SaveChangesAsync(cancellationToken);
                return Result.Success(new CreatePreparedOrderResponse(
                    order.Id, order.TicketNumber, lines.Count, lines.Sum(l => l.UnitPrice * l.Item.Quantity)));
            }
            catch (DbUpdateException) when (attempt < MaxTicketAttempts)
            {
                // Remove على كيان Added بيفصله عن الـcontext بلا ما يحذف شي من القاعدة.
                _context.SuspendedSales.Remove(order);
            }
        }
    }
}

/// <summary>
/// الطلبات الجاهزة المفتوحة (للكاشير) - آخر 24 ساعة بس: طلب ما انحاسب من امبارح (الزبون فلّ) ما
/// بيضل يعبّي القائمة. الأقدم أول (اللي استنى أكتر). فلتر الفرع التلقائي بيقصر الكاشير على فرعه.
/// </summary>
public sealed class GetOpenPreparedOrdersHandler
{
    public static readonly TimeSpan OpenWindow = TimeSpan.FromHours(24);

    private readonly IApplicationDbContext _context;
    private readonly IDateTimeProvider _dateTimeProvider;

    public GetOpenPreparedOrdersHandler(IApplicationDbContext context, IDateTimeProvider dateTimeProvider)
    {
        _context = context;
        _dateTimeProvider = dateTimeProvider;
    }

    public async Task<IReadOnlyList<PreparedOrderDto>> HandleAsync(GetOpenPreparedOrdersQuery query, CancellationToken cancellationToken)
    {
        var sinceUtc = _dateTimeProvider.UtcNow - OpenWindow;

        var ordersQuery = _context.SuspendedSales.AsNoTracking()
            .Where(s => s.Status == SuspendedSaleStatus.Open && s.CreatedAtUtc >= sinceUtc);

        if (query.BranchId is { } branchId)
        {
            ordersQuery = ordersQuery.Where(s => s.BranchId == branchId);
        }

        var orders = await ordersQuery
            .OrderBy(s => s.CreatedAtUtc)
            .Select(s => new
            {
                s.Id, s.TicketNumber, s.Note, s.UserId, s.CreatedAtUtc,
                Items = s.Items.Select(i => new { i.ProductId, i.ProductUnitId, i.Quantity, i.UnitPriceSnapshot }).ToList()
            })
            .ToListAsync(cancellationToken);

        var productIds = orders.SelectMany(o => o.Items).Select(i => i.ProductId).Distinct().ToList();
        var unitIds = orders.SelectMany(o => o.Items).Select(i => i.ProductUnitId).Distinct().ToList();
        var userIds = orders.Select(o => o.UserId).Distinct().ToList();

        var productNames = await _context.Products.IgnoreQueryFilters().AsNoTracking()
            .Where(p => productIds.Contains(p.Id))
            .ToDictionaryAsync(p => p.Id, p => p.Name, cancellationToken);
        var unitNames = await _context.ProductUnits.AsNoTracking()
            .Where(u => unitIds.Contains(u.Id))
            .ToDictionaryAsync(u => u.Id, u => u.UnitName, cancellationToken);
        var userNames = await _context.Users.IgnoreQueryFilters().AsNoTracking()
            .Where(u => userIds.Contains(u.Id))
            .ToDictionaryAsync(u => u.Id, u => u.FullName, cancellationToken);

        return orders.Select(o => new PreparedOrderDto(
                o.Id, o.TicketNumber, o.Note,
                userNames.GetValueOrDefault(o.UserId, "(غير معروف)"),
                o.CreatedAtUtc,
                o.Items.Sum(i => i.Quantity * i.UnitPriceSnapshot),
                o.Items.Select(i => new PreparedOrderItemDto(
                    i.ProductId, i.ProductUnitId,
                    productNames.GetValueOrDefault(i.ProductId, "(منتج محذوف)"),
                    unitNames.GetValueOrDefault(i.ProductUnitId, ""),
                    i.Quantity, i.UnitPriceSnapshot)).ToList()))
            .ToList();
    }
}

/// <summary>إلغاء طلب جاهز ما انحاسب (الزبون فلّ، أو انضرب بالغلط) - بيختفي من قائمة الكاشير.</summary>
public sealed class CancelPreparedOrderHandler
{
    private readonly IApplicationDbContext _context;
    private readonly IDateTimeProvider _dateTimeProvider;

    public CancelPreparedOrderHandler(IApplicationDbContext context, IDateTimeProvider dateTimeProvider)
    {
        _context = context;
        _dateTimeProvider = dateTimeProvider;
    }

    public async Task<Result> HandleAsync(CancelPreparedOrderCommand command, CancellationToken cancellationToken)
    {
        var order = await _context.SuspendedSales.FirstOrDefaultAsync(s => s.Id == command.PreparedOrderId, cancellationToken);
        if (order is null)
        {
            return Result.Failure(Error.NotFound("PreparedOrder.NotFound", "الطلب مش موجود."));
        }

        if (order.Status != SuspendedSaleStatus.Open)
        {
            return Result.Failure(Error.Conflict(
                "PreparedOrder.NotOpen",
                order.Status == SuspendedSaleStatus.Completed ? "الطلب انحاسب أصلًا." : "الطلب ملغى أصلًا."));
        }

        order.Cancel(_dateTimeProvider.UtcNow);
        await _context.SaveChangesAsync(cancellationToken);
        return Result.Success();
    }
}

/// <summary>
/// ضرب باركود أو بحث بالاسم من صفحة التلفون: باركود مطابق حرفيًا أول (وحدته بالضبط - كرتونة/حبة)،
/// وإذا ما في، أول 10 منتجات اسمها فيه الكلمة (بالوحدة الأساسية). بس المنتجات المتاحة للبيع بالفرع.
/// </summary>
public sealed class ScanLookupHandler
{
    private const int MaxNameResults = 10;

    private readonly IApplicationDbContext _context;

    public ScanLookupHandler(IApplicationDbContext context)
    {
        _context = context;
    }

    public async Task<IReadOnlyList<ScanLookupItemDto>> HandleAsync(ScanLookupQuery query, CancellationToken cancellationToken)
    {
        var term = query.Term?.Trim();
        if (string.IsNullOrEmpty(term) || query.BranchId == Guid.Empty)
        {
            return Array.Empty<ScanLookupItemDto>();
        }

        var pricedAtBranch = _context.ProductBranches.AsNoTracking()
            .Where(pb => pb.BranchId == query.BranchId && pb.IsAvailableForSale);

        var byBarcode = await (
                from b in _context.ProductBarcodes.AsNoTracking()
                where b.BarcodeValue == term
                join u in _context.ProductUnits.AsNoTracking() on b.ProductUnitId equals u.Id
                join p in _context.Products.AsNoTracking() on b.ProductId equals p.Id
                join pb in pricedAtBranch on p.Id equals pb.ProductId
                where p.Status == Domain.Catalog.ProductStatus.Active
                select new ScanLookupItemDto(
                    p.Id, u.Id, p.Name, u.UnitName, pb.SellingPrice * u.ConversionFactorToBase, p.IsBatchTracked, b.BarcodeValue))
            .Take(MaxNameResults)
            .ToListAsync(cancellationToken);

        if (byBarcode.Count > 0)
        {
            return byBarcode;
        }

        var pattern = $"%{term}%";
        return await (
                from p in _context.Products.AsNoTracking()
                where p.Status == Domain.Catalog.ProductStatus.Active && EF.Functions.Like(p.Name, pattern)
                join pb in pricedAtBranch on p.Id equals pb.ProductId
                join u in _context.ProductUnits.AsNoTracking().Where(u => u.IsBaseUnit) on p.Id equals u.ProductId
                orderby p.Name
                select new ScanLookupItemDto(
                    p.Id, u.Id, p.Name, u.UnitName, pb.SellingPrice * u.ConversionFactorToBase, p.IsBatchTracked,
                    _context.ProductBarcodes.Where(b => b.ProductUnitId == u.Id).Select(b => b.BarcodeValue).FirstOrDefault()))
            .Take(MaxNameResults)
            .ToListAsync(cancellationToken);
    }
}
