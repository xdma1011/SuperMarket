using Microsoft.EntityFrameworkCore;
using SupermarketSystem.Application.Common.Interfaces;
using SupermarketSystem.Application.Common.Results;
using SupermarketSystem.Application.Common.Time;
using SupermarketSystem.Application.Sales.Common;
using SupermarketSystem.Domain.Common;
using SupermarketSystem.Domain.Ordering;

namespace SupermarketSystem.Application.Ordering.Coupons;

// =====================================================================================
// كوبونات خصم تطبيق الزبائن (29/9/2026، "ضيفها عادي وانا اللي ببعثها يدوي او للكل"):
//   - صاحب المحل بيعمل كود (مبلغ ثابت أو نسبة بسقف)، لزبون معيّن أو للكل، بفترة وحدود استعمال.
//   - الإرسال يدوي: إشعار على التطبيق (push) + تلغرام لو رقم الزبون مربوط بالبوت.
//   - الزبون بيكتب الكود وقت الطلب → بينحجز (CouponRedemption.Reserved)؛ رفض الطلب بيرجّعه؛ التسليم بيثبّته
//     بخصم محسوب على أسعار لحظة التسليم (CompleteSaleHandler.HandleOrderWithCouponAsync).
// =====================================================================================

public sealed record CouponDto(
    Guid Id, string Code, string Title, int DiscountTypeCode, string DiscountTypeTitle, decimal Value, decimal? MaxDiscountAmount,
    decimal MinOrderAmount, DateTime StartAtUtc, DateTime EndAtUtc, Guid? CustomerId, string? CustomerName, string? CustomerPhone,
    int MaxUsesPerCustomer, int? MaxTotalUses, bool IsActive, DateTime? LastSentAtUtc,
    int ReservedCount, int RedeemedCount, decimal RedeemedDiscountTotal, string StatusTitle);

public sealed record CreateCouponCommand(
    string Code, string Title, CouponDiscountType DiscountType, decimal Value, decimal? MaxDiscountAmount, decimal MinOrderAmount,
    DateTime StartAtUtc, DateTime EndAtUtc, Guid? CustomerId, int MaxUsesPerCustomer, int? MaxTotalUses);

public sealed record UpdateCouponCommand(
    Guid CouponId, string Title, CouponDiscountType DiscountType, decimal Value, decimal? MaxDiscountAmount, decimal MinOrderAmount,
    DateTime StartAtUtc, DateTime EndAtUtc, int MaxUsesPerCustomer, int? MaxTotalUses);

public sealed record SetCouponActiveCommand(Guid CouponId, bool IsActive);

/// <summary>CustomerId للكوبون العام = إرسال لزبون واحد؛ بلاه = لكل الزبائن (أو لصاحب الكوبون لو مخصص).</summary>
public sealed record SendCouponCommand(Guid CouponId, Guid? CustomerId);

public sealed record SendCouponResponse(int CustomersTargeted, int TelegramSent, int PushTargeted);

/// <summary>كوبون ظاهر بتطبيق الزبون - RemainingUses لهالزبون.</summary>
public sealed record CustomerCouponDto(
    string Code, string Title, string DiscountDescription, decimal MinOrderAmount, DateTime EndAtUtc, int RemainingUses, bool IsPersonal);

/// <summary>فحص الكود قبل الطلب (من التطبيق) - بيرجّع الخصم التقديري على مجموع السلة.</summary>
public sealed record PreviewCouponQuery(Guid CustomerId, string Code, decimal EstimatedTotal);

public sealed record PreviewCouponResponse(string Code, string Title, decimal EstimatedDiscount, decimal EstimatedTotalAfterDiscount);

internal static class CouponRules
{
    public static string TypeTitle(CouponDiscountType type) => type switch
    {
        CouponDiscountType.FixedAmount => "مبلغ ثابت",
        CouponDiscountType.Percent => "نسبة",
        _ => type.ToString()
    };

    public static string Describe(Coupon coupon) => coupon.DiscountType == CouponDiscountType.FixedAmount
        ? $"خصم {coupon.Value:0.000} د.أ"
        : $"خصم {coupon.Value:0.##}%" + (coupon.MaxDiscountAmount is { } cap ? $" (لحد {cap:0.000} د.أ)" : "");

    /// <summary>عدد الاستعمالات المحسوبة (محجوز لطلب لسا مفتوح + مستعمل) - المرفوض ما بينحسب.</summary>
    public static IQueryable<CouponRedemption> Counted(IApplicationDbContext context, Guid couponId) =>
        context.CouponRedemptions.AsNoTracking()
            .Where(r => r.CouponId == couponId && r.Status != CouponRedemptionStatus.Released);

    /// <summary>كل شروط استعمال الكود لزبون وطلب بمبلغ تقديري - وبيرجّع الكوبون والخصم التقديري.</summary>
    public static async Task<Result<(Coupon Coupon, decimal Discount)>> ValidateForOrderAsync(
        IApplicationDbContext context, string? rawCode, Guid customerId, decimal estimatedTotal, DateTime utcNow, CancellationToken cancellationToken)
    {
        var code = Coupon.NormalizeCode(rawCode);
        var coupon = code is null ? null : await context.Coupons.AsNoTracking().FirstOrDefaultAsync(c => c.Code == code, cancellationToken);
        if (coupon is null || !coupon.IsActive || (coupon.CustomerId is { } owner && owner != customerId))
        {
            return Result.Failure<(Coupon, decimal)>(Error.Validation("Coupon.Invalid", "كود الخصم مش صحيح أو مش إلك."));
        }

        if (!coupon.IsWithinPeriod(utcNow))
        {
            return Result.Failure<(Coupon, decimal)>(Error.Validation(
                "Coupon.OutOfPeriod", utcNow < coupon.StartAtUtc ? "كود الخصم لسا ما بلّش." : "كود الخصم خلصت مدته."));
        }

        if (coupon.MinOrderAmount > 0 && estimatedTotal < coupon.MinOrderAmount)
        {
            return Result.Failure<(Coupon, decimal)>(Error.Validation(
                "Coupon.BelowMinimum", $"كود الخصم بيشتغل على طلب {coupon.MinOrderAmount:0.000} د.أ وأكتر - طلبك {estimatedTotal:0.000}."));
        }

        var usedByCustomer = await Counted(context, coupon.Id).CountAsync(r => r.CustomerId == customerId, cancellationToken);
        if (usedByCustomer >= coupon.MaxUsesPerCustomer)
        {
            return Result.Failure<(Coupon, decimal)>(Error.Validation("Coupon.AlreadyUsed", "استعملت كود الخصم هاد قبل."));
        }

        if (coupon.MaxTotalUses is { } maxTotal && await Counted(context, coupon.Id).CountAsync(cancellationToken) >= maxTotal)
        {
            return Result.Failure<(Coupon, decimal)>(Error.Validation("Coupon.Exhausted", "كود الخصم خلص (انستعمل لأقصى عدد)."));
        }

        return Result.Success((coupon, coupon.DiscountFor(estimatedTotal)));
    }
}

public sealed class GetCouponsHandler
{
    private readonly IApplicationDbContext _context;
    private readonly IDateTimeProvider _dateTimeProvider;

    public GetCouponsHandler(IApplicationDbContext context, IDateTimeProvider dateTimeProvider)
    {
        _context = context;
        _dateTimeProvider = dateTimeProvider;
    }

    public async Task<IReadOnlyList<CouponDto>> HandleAsync(CancellationToken cancellationToken)
    {
        var coupons = await _context.Coupons.AsNoTracking().OrderByDescending(c => c.CreatedAtUtc).Take(500).ToListAsync(cancellationToken);
        var ids = coupons.Select(c => c.Id).ToList();
        var usage = await _context.CouponRedemptions.AsNoTracking()
            .Where(r => ids.Contains(r.CouponId) && r.Status != CouponRedemptionStatus.Released)
            .GroupBy(r => new { r.CouponId, r.Status })
            .Select(g => new { g.Key.CouponId, g.Key.Status, Count = g.Count(), Total = g.Sum(r => r.DiscountAmount ?? 0m) })
            .ToListAsync(cancellationToken);
        var customerIds = coupons.Where(c => c.CustomerId != null).Select(c => c.CustomerId!.Value).Distinct().ToList();
        var customers = await _context.Customers.AsNoTracking()
            .Where(c => customerIds.Contains(c.Id))
            .ToDictionaryAsync(c => c.Id, c => new { c.FullName, c.Phone }, cancellationToken);
        var now = _dateTimeProvider.UtcNow;

        return coupons.Select(c =>
        {
            var reserved = usage.Where(u => u.CouponId == c.Id && u.Status == CouponRedemptionStatus.Reserved).Sum(u => u.Count);
            var redeemed = usage.Where(u => u.CouponId == c.Id && u.Status == CouponRedemptionStatus.Redeemed).ToList();
            var customer = c.CustomerId is { } cid ? customers.GetValueOrDefault(cid) : null;
            var status = !c.IsActive ? "موقوف"
                : now < c.StartAtUtc ? "مجدول"
                : now >= c.EndAtUtc ? "منتهي"
                : c.MaxTotalUses is { } max && reserved + redeemed.Sum(r => r.Count) >= max ? "خلص"
                : "شغّال";
            return new CouponDto(
                c.Id, c.Code, c.Title, (int)c.DiscountType, CouponRules.TypeTitle(c.DiscountType), c.Value, c.MaxDiscountAmount,
                c.MinOrderAmount, c.StartAtUtc, c.EndAtUtc, c.CustomerId, customer?.FullName, customer?.Phone,
                c.MaxUsesPerCustomer, c.MaxTotalUses, c.IsActive, c.LastSentAtUtc,
                reserved, redeemed.Sum(r => r.Count), redeemed.Sum(r => r.Total), status);
        }).ToList();
    }
}

public sealed class CreateCouponHandler
{
    private readonly IApplicationDbContext _context;

    public CreateCouponHandler(IApplicationDbContext context)
    {
        _context = context;
    }

    public async Task<Result<Guid>> HandleAsync(CreateCouponCommand command, CancellationToken cancellationToken)
    {
        var code = Coupon.NormalizeCode(command.Code);
        if (code is null)
        {
            return Result.Failure<Guid>(Error.Validation(
                "Coupon.CodeInvalid", "الكود من 4 لـ20 حرف: حروف إنجليزية وأرقام وشرطة بس (مثلًا EID-2026)."));
        }

        if (await _context.Coupons.AsNoTracking().AnyAsync(c => c.Code == code, cancellationToken))
        {
            return Result.Failure<Guid>(Error.Conflict("Coupon.CodeTaken", "في كوبون بنفس الكود."));
        }

        if (command.CustomerId is { } customerId
            && !await _context.Customers.AsNoTracking().AnyAsync(c => c.Id == customerId && !c.IsDeleted, cancellationToken))
        {
            return Result.Failure<Guid>(Error.NotFound("Coupon.CustomerNotFound", "الزبون مش موجود."));
        }

        Coupon coupon;
        try
        {
            coupon = new Coupon(code, command.Title, command.DiscountType, command.Value, command.MaxDiscountAmount, command.MinOrderAmount,
                AsUtc(command.StartAtUtc), AsUtc(command.EndAtUtc), command.CustomerId, command.MaxUsesPerCustomer, command.MaxTotalUses);
        }
        catch (DomainException)
        {
            return Result.Failure<Guid>(InvalidDetails());
        }

        _context.Coupons.Add(coupon);
        await _context.SaveChangesAsync(cancellationToken);
        return Result.Success(coupon.Id);
    }

    internal static DateTime AsUtc(DateTime value) =>
        value.Kind == DateTimeKind.Local ? value.ToUniversalTime() : DateTime.SpecifyKind(value, DateTimeKind.Utc);

    internal static Error InvalidDetails() => Error.Validation(
        "Coupon.Invalid",
        "تفاصيل الكوبون مش صحيحة: العنوان مطلوب، القيمة أكبر من صفر (النسبة لحد 100)، النهاية بعد البداية، والاستعمال لكل زبون 1 أو أكتر.");
}

public sealed class UpdateCouponHandler
{
    private readonly IApplicationDbContext _context;

    public UpdateCouponHandler(IApplicationDbContext context)
    {
        _context = context;
    }

    public async Task<Result> HandleAsync(UpdateCouponCommand command, CancellationToken cancellationToken)
    {
        var coupon = await _context.Coupons.FirstOrDefaultAsync(c => c.Id == command.CouponId, cancellationToken);
        if (coupon is null)
        {
            return Result.Failure(Error.NotFound("Coupon.NotFound", "الكوبون مش موجود."));
        }

        try
        {
            coupon.Update(command.Title, command.DiscountType, command.Value, command.MaxDiscountAmount, command.MinOrderAmount,
                CreateCouponHandler.AsUtc(command.StartAtUtc), CreateCouponHandler.AsUtc(command.EndAtUtc),
                command.MaxUsesPerCustomer, command.MaxTotalUses);
        }
        catch (DomainException)
        {
            return Result.Failure(CreateCouponHandler.InvalidDetails());
        }

        await _context.SaveChangesAsync(cancellationToken);
        return Result.Success();
    }
}

public sealed class SetCouponActiveHandler
{
    private readonly IApplicationDbContext _context;

    public SetCouponActiveHandler(IApplicationDbContext context)
    {
        _context = context;
    }

    public async Task<Result> HandleAsync(SetCouponActiveCommand command, CancellationToken cancellationToken)
    {
        var coupon = await _context.Coupons.FirstOrDefaultAsync(c => c.Id == command.CouponId, cancellationToken);
        if (coupon is null)
        {
            return Result.Failure(Error.NotFound("Coupon.NotFound", "الكوبون مش موجود."));
        }

        coupon.SetActive(command.IsActive);
        await _context.SaveChangesAsync(cancellationToken);
        return Result.Success();
    }
}

/// <summary>
/// إرسال يدوي: إشعار التطبيق (كل أجهزة الزبون) + تلغرام لو رقمه مربوط بالبوت. فشل إرسال لزبون ما بيوقف الباقي.
/// </summary>
public sealed class SendCouponHandler
{
    private readonly IApplicationDbContext _context;
    private readonly ICustomerPushNotifier _pushNotifier;
    private readonly ITelegramBotClient _telegramBotClient;
    private readonly IDateTimeProvider _dateTimeProvider;

    public SendCouponHandler(
        IApplicationDbContext context, ICustomerPushNotifier pushNotifier, ITelegramBotClient telegramBotClient, IDateTimeProvider dateTimeProvider)
    {
        _context = context;
        _pushNotifier = pushNotifier;
        _telegramBotClient = telegramBotClient;
        _dateTimeProvider = dateTimeProvider;
    }

    public async Task<Result<SendCouponResponse>> HandleAsync(SendCouponCommand command, CancellationToken cancellationToken)
    {
        var coupon = await _context.Coupons.FirstOrDefaultAsync(c => c.Id == command.CouponId, cancellationToken);
        if (coupon is null)
        {
            return Result.Failure<SendCouponResponse>(Error.NotFound("Coupon.NotFound", "الكوبون مش موجود."));
        }

        var now = _dateTimeProvider.UtcNow;
        if (!coupon.IsActive || now >= coupon.EndAtUtc)
        {
            return Result.Failure<SendCouponResponse>(Error.BusinessRule("Coupon.NotSendable", "الكوبون موقوف أو خلصت مدته - ما في داعي تبعته."));
        }

        if (coupon.CustomerId is { } owner && command.CustomerId is { } other && other != owner)
        {
            return Result.Failure<SendCouponResponse>(Error.Validation("Coupon.PersonalOnly", "هالكوبون مخصص لزبون تاني."));
        }

        var targetId = coupon.CustomerId ?? command.CustomerId;
        var customersQuery = _context.Customers.AsNoTracking().Where(c => !c.IsDeleted && !c.IsBlocked);
        if (targetId is { } id)
        {
            customersQuery = customersQuery.Where(c => c.Id == id);
        }

        var customers = await customersQuery.Select(c => new { c.Id, c.Phone }).ToListAsync(cancellationToken);
        if (targetId is not null && customers.Count == 0)
        {
            return Result.Failure<SendCouponResponse>(Error.NotFound("Coupon.CustomerNotFound", "الزبون مش موجود أو محظور."));
        }

        // تلغرام: الأرقام المربوطة بالبوت بمفتاح آخر 9 أرقام (نفس مطابقة أرقام الزبائن).
        var links = await _context.TelegramChatLinks.AsNoTracking()
            .OrderBy(l => l.LinkedAtUtc)
            .Select(l => new { l.Phone, l.ChatId })
            .ToListAsync(cancellationToken);
        var chatByKey = new Dictionary<string, string>();
        foreach (var link in links)
        {
            if (SaleCustomerPhone.Normalize(link.Phone) is { } normalized)
            {
                chatByKey[SaleCustomerPhone.MatchKey(normalized)] = link.ChatId; // آخر ربط بيغلب
            }
        }

        var businessTime = await BusinessTime.LoadAsync(_context, cancellationToken);
        var title = $"🎁 {coupon.Title}";
        var body = $"{CouponRules.Describe(coupon)} بكود {coupon.Code}" +
                   (coupon.MinOrderAmount > 0 ? $" على طلب {coupon.MinOrderAmount:0.000} د.أ وأكتر" : "") +
                   $" - لحد {businessTime.LocalDate(coupon.EndAtUtc.AddSeconds(-1)):d/M/yyyy}.";

        var telegramSent = 0;
        foreach (var customer in customers)
        {
            await _pushNotifier.NotifyOrderStatusChangedAsync(customer.Id, title, body, cancellationToken);
            if (SaleCustomerPhone.Normalize(customer.Phone) is { } phone
                && chatByKey.TryGetValue(SaleCustomerPhone.MatchKey(phone), out var chatId)
                && await _telegramBotClient.SendMessageAsync(chatId, $"{title}\n{body}\nاكتب الكود وقت الطلب بالتطبيق.", cancellationToken))
            {
                telegramSent++;
            }
        }

        coupon.MarkSent(now);
        await _context.SaveChangesAsync(cancellationToken);
        return Result.Success(new SendCouponResponse(customers.Count, telegramSent, customers.Count));
    }
}

/// <summary>كوبونات الزبون بالتطبيق: المخصصة إله + العامة، الشغّالة هلق وإله فيها استعمال باقي.</summary>
public sealed class GetCustomerCouponsHandler
{
    private readonly IApplicationDbContext _context;
    private readonly IDateTimeProvider _dateTimeProvider;

    public GetCustomerCouponsHandler(IApplicationDbContext context, IDateTimeProvider dateTimeProvider)
    {
        _context = context;
        _dateTimeProvider = dateTimeProvider;
    }

    public async Task<IReadOnlyList<CustomerCouponDto>> HandleAsync(Guid customerId, CancellationToken cancellationToken)
    {
        var now = _dateTimeProvider.UtcNow;
        var coupons = await _context.Coupons.AsNoTracking()
            .Where(c => c.IsActive && c.StartAtUtc <= now && c.EndAtUtc > now && (c.CustomerId == null || c.CustomerId == customerId))
            .OrderBy(c => c.EndAtUtc)
            .ToListAsync(cancellationToken);
        var ids = coupons.Select(c => c.Id).ToList();
        var counted = await _context.CouponRedemptions.AsNoTracking()
            .Where(r => ids.Contains(r.CouponId) && r.Status != CouponRedemptionStatus.Released)
            .GroupBy(r => r.CouponId)
            .Select(g => new { CouponId = g.Key, Total = g.Count(), Mine = g.Count(r => r.CustomerId == customerId) })
            .ToDictionaryAsync(g => g.CouponId, cancellationToken);

        return coupons
            .Select(c =>
            {
                var usage = counted.GetValueOrDefault(c.Id);
                var remaining = c.MaxUsesPerCustomer - (usage?.Mine ?? 0);
                var exhausted = c.MaxTotalUses is { } max && (usage?.Total ?? 0) >= max;
                return (Coupon: c, Remaining: exhausted ? 0 : remaining);
            })
            .Where(x => x.Remaining > 0)
            .Select(x => new CustomerCouponDto(
                x.Coupon.Code, x.Coupon.Title, CouponRules.Describe(x.Coupon), x.Coupon.MinOrderAmount, x.Coupon.EndAtUtc, x.Remaining,
                x.Coupon.CustomerId is not null))
            .ToList();
    }
}

public sealed class PreviewCouponHandler
{
    private readonly IApplicationDbContext _context;
    private readonly IDateTimeProvider _dateTimeProvider;

    public PreviewCouponHandler(IApplicationDbContext context, IDateTimeProvider dateTimeProvider)
    {
        _context = context;
        _dateTimeProvider = dateTimeProvider;
    }

    public async Task<Result<PreviewCouponResponse>> HandleAsync(PreviewCouponQuery query, CancellationToken cancellationToken)
    {
        var check = await CouponRules.ValidateForOrderAsync(
            _context, query.Code, query.CustomerId, query.EstimatedTotal, _dateTimeProvider.UtcNow, cancellationToken);
        if (check.IsFailure)
        {
            return Result.Failure<PreviewCouponResponse>(check.Error!);
        }

        var (coupon, discount) = check.Value;
        return Result.Success(new PreviewCouponResponse(coupon.Code, coupon.Title, discount, query.EstimatedTotal - discount));
    }
}
