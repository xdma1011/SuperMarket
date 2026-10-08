using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using SupermarketSystem.Application.Common.Interfaces;
using SupermarketSystem.Application.Common.Notifications;
using SupermarketSystem.Application.Common.Pagination;
using SupermarketSystem.Application.Common.Results;
using SupermarketSystem.Application.Sales.CompleteSale;
using SupermarketSystem.Domain.Identity;

namespace SupermarketSystem.Application.Sales.RejectedSales;

/// <summary>
/// تسجيل البيعات اللي السيرفر رفضها نهائيًا (بند 24، 8/10/2026). التنفيذ بالـInfrastructure لأنه لازم يكتب
/// بـDbContext منفصل: معاملة البيع الفاشلة ممكن تكون عملت rollback، وتسجيل الرفض ما لازم يتأثر فيها ولا يأثّر عليها.
/// كلا الميثودين "أفضل جهد" - ما بيرموا استثناء أبدًا (فشل التسجيل ما لازم يغيّر رد البيع الأصلي).
/// </summary>
public interface IRejectedSaleRecorder
{
    Task RecordRejectionAsync(CompleteSaleCommand command, Error error, CancellationToken cancellationToken);

    /// <summary>نفس الـClientRequestId انقبل (أو رجع WasReplay): لو كان له سجل رفض مفتوح بيتعلّم "انقبلت لاحقًا".</summary>
    Task MarkAcceptedAsync(Guid clientRequestId, CancellationToken cancellationToken);
}

public static class RejectedSaleRules
{
    /// <summary>
    /// أي رفض إلا تعارض التزامن (Concurrency): هاد مؤقت ونفس الطلب لما يتعاد بينجح، وتسجيله بيعمل ضجيج.
    /// 401 (جلسة منتهية) ما بيوصل للمعالج أصلًا، وانقطاع الاتصال ما بيوصل للسيرفر.
    /// </summary>
    public static bool ShouldRecord(Error error) => error.Type != ErrorType.Concurrency;
}

// ───────────────────────── القائمة ─────────────────────────

public sealed record GetRejectedSalesQuery(PagedRequest Paging, Guid? BranchId, bool OnlyOpen = true);

public sealed record RejectedSaleListItemDto(
    Guid Id,
    Guid ClientRequestId,
    Guid BranchId,
    string BranchName,
    string? CashierName,
    string ErrorCode,
    string ErrorMessage,
    decimal PaidAmountHint,
    int ItemCount,
    int AttemptCount,
    DateTime FirstAttemptAtUtc,
    DateTime LastAttemptAtUtc,
    bool IsResolved,
    DateTime? ResolvedAtUtc,
    string? ResolvedByName,
    string? ResolutionNote,
    bool ResolvedAutomatically);

public sealed class GetRejectedSalesHandler
{
    private readonly IApplicationDbContext _context;

    public GetRejectedSalesHandler(IApplicationDbContext context)
    {
        _context = context;
    }

    public async Task<PagedResult<RejectedSaleListItemDto>> HandleAsync(GetRejectedSalesQuery query, CancellationToken cancellationToken)
    {
        var paging = query.Paging.Normalized();

        var attempts = _context.RejectedSaleAttempts.AsNoTracking().AsQueryable();
        if (query.BranchId is { } branchId)
        {
            attempts = attempts.Where(a => a.BranchId == branchId);
        }

        if (query.OnlyOpen)
        {
            attempts = attempts.Where(a => a.ResolvedAtUtc == null);
        }

        if (!string.IsNullOrWhiteSpace(paging.Search))
        {
            var pattern = $"%{paging.Search.Trim()}%";
            attempts = attempts.Where(a => EF.Functions.Like(a.ErrorMessage, pattern) || EF.Functions.Like(a.ErrorCode, pattern));
        }

        var totalCount = await attempts.CountAsync(cancellationToken);
        var rows = await attempts
            .OrderByDescending(a => a.LastAttemptAtUtc).ThenByDescending(a => a.Id)
            .Skip(paging.Skip).Take(paging.PageSize)
            .ToListAsync(cancellationToken);

        var userIds = rows.Select(r => r.CashierUserId).Concat(rows.Select(r => r.ResolvedByUserId))
            .Where(id => id != null).Select(id => id!.Value).Distinct().ToList();
        var userNames = userIds.Count == 0
            ? new Dictionary<Guid, string>()
            : await _context.Users.IgnoreQueryFilters().AsNoTracking()
                .Where(u => userIds.Contains(u.Id))
                .ToDictionaryAsync(u => u.Id, u => u.FullName, cancellationToken);

        var branchIds = rows.Select(r => r.BranchId).Distinct().ToList();
        var branchNames = await _context.Branches.IgnoreQueryFilters().AsNoTracking()
            .Where(b => branchIds.Contains(b.Id))
            .ToDictionaryAsync(b => b.Id, b => b.Name, cancellationToken);

        string? NameOf(Guid? id) => id is { } value && userNames.TryGetValue(value, out var name) ? name : null;

        var items = rows.Select(r => new RejectedSaleListItemDto(
                r.Id, r.ClientRequestId, r.BranchId,
                branchNames.TryGetValue(r.BranchId, out var branchName) ? branchName : "فرع غير معروف",
                NameOf(r.CashierUserId), r.ErrorCode, r.ErrorMessage, r.PaidAmountHint, r.ItemCount, r.AttemptCount,
                r.FirstAttemptAtUtc, r.LastAttemptAtUtc, r.ResolvedAtUtc != null, r.ResolvedAtUtc, NameOf(r.ResolvedByUserId),
                r.ResolutionNote, r.ResolvedAutomatically))
            .ToList();

        return new PagedResult<RejectedSaleListItemDto>(items, totalCount, paging.PageNumber, paging.PageSize);
    }
}

// ───────────────────────── التفاصيل (محتوى البيعة بأسماء مقروءة) ─────────────────────────

public sealed record RejectedSaleLineDto(string ProductName, string? UnitName, decimal Quantity, decimal ManualDiscountAmount);

public sealed record RejectedSalePaymentDto(string PaymentMethodName, decimal Amount);

public sealed record RejectedSaleDetailsDto(
    RejectedSaleListItemDto Summary,
    decimal InvoiceLevelDiscountAmount,
    bool AllowCreditSale,
    string? CustomerPhone,
    IReadOnlyList<RejectedSaleLineDto> Lines,
    IReadOnlyList<RejectedSalePaymentDto> Payments,
    string PayloadJson);

public sealed class GetRejectedSaleByIdHandler
{
    private static readonly JsonSerializerOptions JsonOptions = new() { PropertyNameCaseInsensitive = true };

    private readonly IApplicationDbContext _context;
    private readonly GetRejectedSalesHandler _listHandler;

    public GetRejectedSaleByIdHandler(IApplicationDbContext context, GetRejectedSalesHandler listHandler)
    {
        _context = context;
        _listHandler = listHandler;
    }

    public async Task<Result<RejectedSaleDetailsDto>> HandleAsync(Guid id, CancellationToken cancellationToken)
    {
        var attempt = await _context.RejectedSaleAttempts.AsNoTracking().FirstOrDefaultAsync(a => a.Id == id, cancellationToken);
        if (attempt is null)
        {
            return Result.Failure<RejectedSaleDetailsDto>(Error.NotFound("RejectedSale.NotFound", "البيعة المرفوضة مش موجودة."));
        }

        // ملخّص السطر نفسه من القائمة (اسم الكاشير والفرع...) - بنفس المنطق بدل تكراره.
        var summaryPage = await _listHandler.HandleAsync(
            new GetRejectedSalesQuery(new PagedRequest { PageSize = 100 }, attempt.BranchId, OnlyOpen: false), cancellationToken);
        var summary = summaryPage.Items.FirstOrDefault(i => i.Id == id) ?? await LoadSummaryDirectAsync(attempt, cancellationToken);

        CompleteSaleCommand? command = null;
        try
        {
            command = JsonSerializer.Deserialize<CompleteSaleCommand>(attempt.PayloadJson, JsonOptions);
        }
        catch (JsonException)
        {
            // محتوى تالف (ما لازم يصير) - بنعرض الملخّص والـJSON الخام بس.
        }

        if (command is null)
        {
            return Result.Success(new RejectedSaleDetailsDto(summary, 0, false, null, [], [], attempt.PayloadJson));
        }

        var productIds = command.Items.Select(i => i.ProductId).Distinct().ToList();
        var productNames = await _context.Products.IgnoreQueryFilters().AsNoTracking()
            .Where(p => productIds.Contains(p.Id))
            .ToDictionaryAsync(p => p.Id, p => p.Name, cancellationToken);
        var unitIds = command.Items.Select(i => i.ProductUnitId).Distinct().ToList();
        var unitNames = await _context.ProductUnits.AsNoTracking()
            .Where(u => unitIds.Contains(u.Id))
            .ToDictionaryAsync(u => u.Id, u => u.UnitName, cancellationToken);
        var methodIds = command.Payments.Select(p => p.PaymentMethodId).Distinct().ToList();
        var methodNames = await _context.PaymentMethods.AsNoTracking()
            .Where(m => methodIds.Contains(m.Id))
            .ToDictionaryAsync(m => m.Id, m => m.Name, cancellationToken);

        var lines = command.Items.Select(i => new RejectedSaleLineDto(
                productNames.TryGetValue(i.ProductId, out var productName) ? productName : "منتج غير معروف",
                unitNames.TryGetValue(i.ProductUnitId, out var unitName) ? unitName : null,
                i.Quantity, i.ManualDiscountAmount))
            .ToList();
        var payments = command.Payments.Select(p => new RejectedSalePaymentDto(
                methodNames.TryGetValue(p.PaymentMethodId, out var methodName) ? methodName : "طريقة دفع غير معروفة", p.Amount))
            .ToList();

        return Result.Success(new RejectedSaleDetailsDto(
            summary, command.InvoiceLevelDiscountAmount, command.AllowCreditSale, command.CustomerPhone, lines, payments, attempt.PayloadJson));
    }

    private async Task<RejectedSaleListItemDto> LoadSummaryDirectAsync(Domain.Sales.RejectedSaleAttempt a, CancellationToken cancellationToken)
    {
        var branchName = await AlertText.BranchNameAsync(_context, a.BranchId, cancellationToken);
        var cashier = a.CashierUserId is null ? null : await AlertText.UserNameAsync(_context, a.CashierUserId, cancellationToken);
        return new RejectedSaleListItemDto(
            a.Id, a.ClientRequestId, a.BranchId, branchName, cashier, a.ErrorCode, a.ErrorMessage, a.PaidAmountHint, a.ItemCount,
            a.AttemptCount, a.FirstAttemptAtUtc, a.LastAttemptAtUtc, a.ResolvedAtUtc != null, a.ResolvedAtUtc, null,
            a.ResolutionNote, a.ResolvedAutomatically);
    }
}

// ───────────────────────── المعالجة ─────────────────────────

public sealed record ResolveRejectedSaleCommand(Guid Id, string? Note);

/// <summary>
/// "تمت المعالجة" علامة بشرية + ملاحظة (مثلًا "دخلتها يدويًا بفاتورة رقم X" أو "الزبون رجّع الأغراض"). بلا أي أثر مالي
/// أو مخزني، وبلا إعادة معالجة تلقائية للبيعة (الأسعار/الوقت/المخزون ممكن تكون تغيّرت وما بنخمّن).
/// </summary>
public sealed class ResolveRejectedSaleHandler
{
    private readonly IApplicationDbContext _context;
    private readonly ICurrentUserContext _currentUser;
    private readonly IDateTimeProvider _dateTimeProvider;

    public ResolveRejectedSaleHandler(IApplicationDbContext context, ICurrentUserContext currentUser, IDateTimeProvider dateTimeProvider)
    {
        _context = context;
        _currentUser = currentUser;
        _dateTimeProvider = dateTimeProvider;
    }

    public async Task<Result<Guid>> HandleAsync(ResolveRejectedSaleCommand command, CancellationToken cancellationToken)
    {
        var attempt = await _context.RejectedSaleAttempts.FirstOrDefaultAsync(a => a.Id == command.Id, cancellationToken);
        if (attempt is null)
        {
            return Result.Failure<Guid>(Error.NotFound("RejectedSale.NotFound", "البيعة المرفوضة مش موجودة."));
        }

        if (!attempt.IsOpen)
        {
            return Result.Failure<Guid>(Error.Conflict("RejectedSale.AlreadyResolved", "هاي البيعة معالَجة من قبل."));
        }

        attempt.MarkResolved(_currentUser.UserId ?? User.SystemUserId, command.Note, _dateTimeProvider.UtcNow);
        await _context.SaveChangesAsync(cancellationToken);
        return Result.Success(attempt.Id);
    }
}
