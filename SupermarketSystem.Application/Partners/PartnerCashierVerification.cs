using System.Security.Cryptography;
using System.Text;
using Microsoft.EntityFrameworkCore;
using SupermarketSystem.Application.Common.Interfaces;
using SupermarketSystem.Application.Common.Notifications;
using SupermarketSystem.Application.Common.Results;
using SupermarketSystem.Application.Sales.Common;
using SupermarketSystem.Domain.Common;
using SupermarketSystem.Domain.Partners;

namespace SupermarketSystem.Application.Partners;

// =====================================================================================
// تحقق الشريك بالكاشير بطرق إضافية (29/9/2026، "ضيفها وخليها اختيارية من الضبط"):
//   - كود تلغرام (OTP): الكاشير بيطلب كود **لمبلغ محدد**، بيوصل للشريك على تلغرام بالمبلغ، والكود ما بيمشي إلا
//     لنفس المبلغ - فالكاشير ما بيقدر يطلب كود لـ5 دنانير ويسحب 500. صالح 5 دقايق، 5 محاولات غلط بتحرقه.
//   - باركود شخصي: 20 رقم عشوائي بكرت مطبوع (أرقام بس - لوحة المفاتيح العربية ما بتخربط قراءة الماسح). بينحفظ
//     hash بس، وإصدار جديد بيلغي القديم (كرت ضايع = إصدار جديد).
// كل طريقة بتتفعّل لحالها من الإعدادات (PartnerVerificationSettingsKeys)؛ يوزر وكلمة سر الشريك دايمًا شغّالين.
// =====================================================================================

public sealed record PartnerVerificationOptionsDto(
    bool PasswordEnabled, bool TelegramOtpEnabled, bool BarcodeEnabled, IReadOnlyList<OtpPartnerDto> OtpPartners);

/// <summary>شريك بالفرع رقمه مربوط ببوت تلغرام - بيقدر يستلم كود.</summary>
public sealed record OtpPartnerDto(Guid PartnerId, string FullName);

public sealed record RequestPartnerOtpCommand(Guid BranchId, Guid PartnerId, decimal Amount);

public sealed record RequestPartnerOtpResponse(Guid ChallengeId, DateTime ExpiresAtUtc);

public sealed record SetPartnerTelegramPhoneCommand(Guid PartnerId, string? TelegramPhone);

public sealed record IssuePartnerBarcodeResponse(string Barcode, DateTime IssuedAtUtc);

internal static class PartnerVerificationSecrets
{
    public const int OtpLifetimeMinutes = 5;
    public const int BarcodeDigits = 20;

    public static string Hash(string value) => Convert.ToBase64String(SHA256.HashData(Encoding.UTF8.GetBytes(value)));

    public static bool HashEquals(string value, string expectedHash) =>
        CryptographicOperations.FixedTimeEquals(Encoding.UTF8.GetBytes(Hash(value)), Encoding.UTF8.GetBytes(expectedHash));

    /// <summary>أرقام بس (ماسح الباركود أو إدخال يدوي) - أي مسافة/شرطة بتنشال.</summary>
    public static string? NormalizeBarcode(string? raw)
    {
        if (string.IsNullOrWhiteSpace(raw))
        {
            return null;
        }

        var digits = new string(raw.Where(char.IsAsciiDigit).ToArray());
        return digits.Length == BarcodeDigits ? digits : null;
    }

    public static string NewBarcode()
    {
        var builder = new StringBuilder(BarcodeDigits);
        for (var i = 0; i < BarcodeDigits; i++)
        {
            builder.Append((char)('0' + RandomNumberGenerator.GetInt32(0, 10)));
        }

        return builder.ToString();
    }

    /// <summary>chat_id تبع رقم الشريك (آخر 9 أرقام، نفس مطابقة أرقام الزبائن) - آخر ربط هو الفعّال.</summary>
    public static async Task<string?> FindTelegramChatIdAsync(IApplicationDbContext context, string? phone, CancellationToken cancellationToken)
    {
        if (SaleCustomerPhone.Normalize(phone) is not { } normalized)
        {
            return null;
        }

        var key = SaleCustomerPhone.MatchKey(normalized);
        return await context.TelegramChatLinks.AsNoTracking()
            .Where(l => EF.Functions.Like(l.Phone, "%" + key))
            .OrderByDescending(l => l.LinkedAtUtc)
            .Select(l => l.ChatId)
            .FirstOrDefaultAsync(cancellationToken);
    }

    /// <summary>كل الأرقام المربوطة بالبوت اللي بتطابق أرقام شركاء (للقائمة بالكاشير والإدارة) - مفتاحها آخر 9 أرقام.</summary>
    public static async Task<HashSet<string>> LinkedPhoneKeysAsync(
        IApplicationDbContext context, IEnumerable<string?> phones, CancellationToken cancellationToken)
    {
        var keys = phones
            .Select(p => SaleCustomerPhone.Normalize(p))
            .Where(p => p is not null)
            .Select(p => SaleCustomerPhone.MatchKey(p!))
            .Distinct()
            .ToList();
        var linked = new HashSet<string>();
        foreach (var key in keys)
        {
            if (await context.TelegramChatLinks.AsNoTracking().AnyAsync(l => EF.Functions.Like(l.Phone, "%" + key), cancellationToken))
            {
                linked.Add(key);
            }
        }

        return linked;
    }

    public static string? PhoneKey(string? phone) =>
        SaleCustomerPhone.Normalize(phone) is { } normalized ? SaleCustomerPhone.MatchKey(normalized) : null;
}

/// <summary>شو طرق التحقق المفعّلة، ومين من شركاء الفرع بيقدر يستلم كود تلغرام (الكاشير بيعرض الخيارات حسبها).</summary>
public sealed class GetPartnerVerificationOptionsHandler
{
    private readonly IApplicationDbContext _context;
    private readonly ISettingsProvider _settingsProvider;

    public GetPartnerVerificationOptionsHandler(IApplicationDbContext context, ISettingsProvider settingsProvider)
    {
        _context = context;
        _settingsProvider = settingsProvider;
    }

    public async Task<PartnerVerificationOptionsDto> HandleAsync(Guid branchId, CancellationToken cancellationToken)
    {
        var otpEnabled = await _settingsProvider.GetBoolAsync(PartnerVerificationSettingsKeys.TelegramOtpEnabled, false, cancellationToken);
        var barcodeEnabled = await _settingsProvider.GetBoolAsync(PartnerVerificationSettingsKeys.BarcodeEnabled, false, cancellationToken);

        var otpPartners = new List<OtpPartnerDto>();
        if (otpEnabled)
        {
            var candidates = await _context.Partners.IgnoreQueryFilters().AsNoTracking()
                .Where(p => p.BranchId == branchId && p.IsActive && p.TelegramPhone != null)
                .OrderBy(p => p.FullName)
                .Select(p => new { p.Id, p.FullName, p.TelegramPhone })
                .ToListAsync(cancellationToken);
            var linked = await PartnerVerificationSecrets.LinkedPhoneKeysAsync(_context, candidates.Select(c => c.TelegramPhone), cancellationToken);
            otpPartners = candidates
                .Where(c => PartnerVerificationSecrets.PhoneKey(c.TelegramPhone) is { } key && linked.Contains(key))
                .Select(c => new OtpPartnerDto(c.Id, c.FullName))
                .ToList();
        }

        return new PartnerVerificationOptionsDto(true, otpEnabled, barcodeEnabled, otpPartners);
    }
}

/// <summary>الكاشير طلب كود تلغرام لسحب مبلغ محدد - بيوصل للشريك نفسه بالمبلغ واسم الكاشير.</summary>
public sealed class RequestPartnerOtpHandler
{
    private readonly IApplicationDbContext _context;
    private readonly ICurrentUserContext _currentUser;
    private readonly ISettingsProvider _settingsProvider;
    private readonly ITelegramBotClient _telegramBotClient;
    private readonly IDateTimeProvider _dateTimeProvider;

    public RequestPartnerOtpHandler(
        IApplicationDbContext context, ICurrentUserContext currentUser, ISettingsProvider settingsProvider,
        ITelegramBotClient telegramBotClient, IDateTimeProvider dateTimeProvider)
    {
        _context = context;
        _currentUser = currentUser;
        _settingsProvider = settingsProvider;
        _telegramBotClient = telegramBotClient;
        _dateTimeProvider = dateTimeProvider;
    }

    public async Task<Result<RequestPartnerOtpResponse>> HandleAsync(RequestPartnerOtpCommand command, CancellationToken cancellationToken)
    {
        if (!await _settingsProvider.GetBoolAsync(PartnerVerificationSettingsKeys.TelegramOtpEnabled, false, cancellationToken))
        {
            return Result.Failure<RequestPartnerOtpResponse>(Error.Forbidden(
                "PartnerOtp.Disabled", "التحقق بكود تلغرام مش مفعّل - بيتفعّل من صفحة الإعدادات."));
        }

        if (!_currentUser.IsCrossBranchAccessAllowed && _currentUser.BranchId != command.BranchId)
        {
            return Result.Failure<RequestPartnerOtpResponse>(Error.Forbidden("PartnerWithdrawal.BranchNotAllowed", "مش فرعك."));
        }

        if (command.Amount <= 0)
        {
            return Result.Failure<RequestPartnerOtpResponse>(Error.Validation("PartnerWithdrawal.AmountInvalid", "المبلغ لازم يكون أكبر من صفر."));
        }

        var partner = await _context.Partners.IgnoreQueryFilters().AsNoTracking()
            .FirstOrDefaultAsync(p => p.Id == command.PartnerId && p.BranchId == command.BranchId && p.IsActive, cancellationToken);
        if (partner is null)
        {
            return Result.Failure<RequestPartnerOtpResponse>(Error.NotFound("Partner.NotFound", "الشريك مش موجود بهالفرع."));
        }

        var chatId = await PartnerVerificationSecrets.FindTelegramChatIdAsync(_context, partner.TelegramPhone, cancellationToken);
        if (chatId is null)
        {
            return Result.Failure<RequestPartnerOtpResponse>(Error.BusinessRule(
                "PartnerOtp.TelegramNotLinked", "رقم تلغرام الشريك مش مربوط بالبوت - لازم يفتح البوت ويشارك رقمه أول مرة."));
        }

        var now = _dateTimeProvider.UtcNow;
        // كود واحد بالدقيقة لكل شريك - ما في إغراق لتلغرامه بكبسات متكررة.
        if (await _context.PartnerOtpChallenges.AsNoTracking()
                .AnyAsync(c => c.PartnerId == partner.Id && c.CreatedAtUtc > now.AddMinutes(-1), cancellationToken))
        {
            return Result.Failure<RequestPartnerOtpResponse>(Error.BusinessRule(
                "PartnerOtp.TooFrequent", "انبعت كود من أقل من دقيقة - استنى شوي أو استعمل الكود اللي وصل."));
        }

        var amount = Math.Round(command.Amount, 3, MidpointRounding.AwayFromZero);
        var code = RandomNumberGenerator.GetInt32(100000, 1000000).ToString();
        var actor = _currentUser.UserId ?? throw new InvalidOperationException("لا يمكن طلب كود بلا هوية مستخدم.");
        var challenge = new PartnerOtpChallenge(
            partner.BranchId, partner.Id, PartnerVerificationSecrets.Hash(code), amount, now,
            now.AddMinutes(PartnerVerificationSecrets.OtpLifetimeMinutes), actor);

        var who = await AlertText.UserNameAsync(_context, actor, cancellationToken);
        var branch = await AlertText.BranchNameAsync(_context, partner.BranchId, cancellationToken);
        var sent = await _telegramBotClient.SendMessageAsync(chatId,
            $"🔐 كود سحب {amount:0.000} د.أ من صندوق {branch}: {code}\n" +
            $"طلبه الكاشير {who}. صالح {PartnerVerificationSecrets.OtpLifetimeMinutes} دقايق ولنفس المبلغ بس.\n" +
            "إذا مش إنت اللي بتسحب، لا تعطي الكود لحدا.",
            cancellationToken);
        if (!sent)
        {
            return Result.Failure<RequestPartnerOtpResponse>(Error.BusinessRule(
                "PartnerOtp.SendFailed", "تعذّر إرسال الكود على تلغرام - تأكد من إعداد البوت، أو استعمل طريقة تحقق تانية."));
        }

        _context.PartnerOtpChallenges.Add(challenge);
        await _context.SaveChangesAsync(cancellationToken);
        return Result.Success(new RequestPartnerOtpResponse(challenge.Id, challenge.ExpiresAtUtc));
    }
}

/// <summary>رقم تلغرام الشريك (من صفحة الشركاء) - فاضي = بلا تحقق بتلغرام.</summary>
public sealed class SetPartnerTelegramPhoneHandler
{
    private readonly IApplicationDbContext _context;

    public SetPartnerTelegramPhoneHandler(IApplicationDbContext context)
    {
        _context = context;
    }

    public async Task<Result> HandleAsync(SetPartnerTelegramPhoneCommand command, CancellationToken cancellationToken)
    {
        var partner = await _context.Partners.FirstOrDefaultAsync(p => p.Id == command.PartnerId, cancellationToken);
        if (partner is null)
        {
            return Result.Failure(Error.NotFound("Partner.NotFound", "الشريك مش موجود."));
        }

        var phone = string.IsNullOrWhiteSpace(command.TelegramPhone) ? null : command.TelegramPhone.Trim();
        if (phone is not null && SaleCustomerPhone.Normalize(phone) is null)
        {
            return Result.Failure(Error.Validation("Partner.TelegramPhoneInvalid", "رقم تلغرام مش صحيح - اكتبه أرقام (مثلًا 0791234567)."));
        }

        try
        {
            partner.SetTelegramPhone(phone);
        }
        catch (DomainException ex)
        {
            return Result.Failure(Error.Validation("Partner.TelegramPhoneInvalid", ex.Message));
        }

        await _context.SaveChangesAsync(cancellationToken);
        return Result.Success();
    }
}

/// <summary>إصدار باركود شخصي جديد (بيلغي القديم) - الأرقام بترجع هون مرة وحدة بس للطباعة.</summary>
public sealed class IssuePartnerBarcodeHandler
{
    private readonly IApplicationDbContext _context;
    private readonly IDateTimeProvider _dateTimeProvider;

    public IssuePartnerBarcodeHandler(IApplicationDbContext context, IDateTimeProvider dateTimeProvider)
    {
        _context = context;
        _dateTimeProvider = dateTimeProvider;
    }

    public async Task<Result<IssuePartnerBarcodeResponse>> HandleAsync(Guid partnerId, CancellationToken cancellationToken)
    {
        var partner = await _context.Partners.FirstOrDefaultAsync(p => p.Id == partnerId, cancellationToken);
        if (partner is null)
        {
            return Result.Failure<IssuePartnerBarcodeResponse>(Error.NotFound("Partner.NotFound", "الشريك مش موجود."));
        }

        var barcode = PartnerVerificationSecrets.NewBarcode();
        var now = _dateTimeProvider.UtcNow;
        partner.IssueCashierBarcode(PartnerVerificationSecrets.Hash(barcode), now);
        await _context.SaveChangesAsync(cancellationToken);
        return Result.Success(new IssuePartnerBarcodeResponse(barcode, now));
    }
}

public sealed class RevokePartnerBarcodeHandler
{
    private readonly IApplicationDbContext _context;

    public RevokePartnerBarcodeHandler(IApplicationDbContext context)
    {
        _context = context;
    }

    public async Task<Result> HandleAsync(Guid partnerId, CancellationToken cancellationToken)
    {
        var partner = await _context.Partners.FirstOrDefaultAsync(p => p.Id == partnerId, cancellationToken);
        if (partner is null)
        {
            return Result.Failure(Error.NotFound("Partner.NotFound", "الشريك مش موجود."));
        }

        partner.RevokeCashierBarcode();
        await _context.SaveChangesAsync(cancellationToken);
        return Result.Success();
    }
}
