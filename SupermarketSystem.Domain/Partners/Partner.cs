using SupermarketSystem.Domain.Common;

namespace SupermarketSystem.Domain.Partners;

// =====================================================================================
// وحدة الشركاء (قرارات صاحب المشروع 15-21/9/2026، "اشتغلهم تماما" 28/9/2026 - راجع CLAUDE.md):
// - الشراكة لكل فرع لحاله.
// - شريك رأس مال: نصيبه من الربح حسب رأس ماله المضخوخ فعليًا (CapitalTransaction.PartnerId) - متغيّر.
// - شريك مضارب: برّا رأس المال، إله نسبة ثابتة متفَق عليها من ربح الشهر.
// - كشف شهري "جاهز ومسجَّل" ببداية كل شهر (مش تحويل فلوس) - الرصيد بيتراكم، والسحب فعل يدوي منفصل
//   ممكن يسبق الرصيد (سلفة، الرصيد بيصير سالب وبيترحّل).
// - السحب من الصندوق (بينقص المتوقع بالتقفيل) أو "من جيبي" (صاحب المحل دفع من ماله - بينسجّل إله
//   "مستحق لصاحب المحل" منفصل، ما بيضيع لو نسي).
// كل السجلات تاريخية (بلا تعديل/حذف للمبالغ) - تصحيح غلط = حركة معاكسة، نفس فلسفة CashDrawerLog.
// =====================================================================================

public enum PartnerType
{
    /// <summary>شريك رأس مال - نصيبه بنسبة رأس ماله من مجموع رأس مال الشركاء بالفرع.</summary>
    Capital = 1,

    /// <summary>شريك مضارب - نسبة ثابتة من ربح الشهر، بلا رأس مال.</summary>
    Speculative = 2
}

public class Partner : AuditableEntity, IBranchOwned
{
    public const int MaxNameLength = 200;
    public const int MaxNotesLength = 500;

    public Guid BranchId { get; private set; }
    public string FullName { get; private set; } = null!;
    public PartnerType Type { get; private set; }

    /// <summary>
    /// حساب الشريك بالنظام (اختياري) - لازم لسحبه من الكاشير بتحقق هوية، ولربط "اخصمها مني" (السحب بسعر
    /// التكلفة) برصيده. شريك بلا حساب بينسحبله من لوحة الإدارة بس.
    /// </summary>
    public Guid? UserId { get; private set; }

    /// <summary>للمضارب بس: نسبته من صافي ربح الشهر (0-100).</summary>
    public decimal? SpeculativeProfitPercent { get; private set; }

    public bool IsActive { get; private set; }
    public string? Notes { get; private set; }

    public const int MaxTelegramPhoneLength = 20;

    /// <summary>
    /// رقم تلغرام الشريك (29/9/2026) - لكود التحقق (OTP) بالكاشير. الشريك لازم يكون فاتح البوت وشارك رقمه
    /// (TelegramChatLink) - الرقم لحاله ما بيكفي، البوت بدّه chat_id.
    /// </summary>
    public string? TelegramPhone { get; private set; }

    /// <summary>
    /// باركود التحقق الشخصي (29/9/2026) - hash بس (SHA-256)، الكود نفسه بيطلع مرة وحدة وقت الإصدار (بيتطبع
    /// كرت). إصدار جديد بيلغي القديم؛ null = ما في باركود.
    /// </summary>
    public string? CashierBarcodeHash { get; private set; }

    public DateTime? CashierBarcodeIssuedAtUtc { get; private set; }

    private Partner() { } // EF Core

    public Partner(Guid branchId, string fullName, PartnerType type, Guid? userId, decimal? speculativeProfitPercent, string? notes)
    {
        BranchId = branchId;
        Type = type;
        IsActive = true;
        Update(fullName, userId, speculativeProfitPercent, notes);
    }

    public void Update(string fullName, Guid? userId, decimal? speculativeProfitPercent, string? notes)
    {
        if (string.IsNullOrWhiteSpace(fullName))
        {
            throw new DomainException("Partner name is required.");
        }

        if (Type == PartnerType.Speculative)
        {
            if (speculativeProfitPercent is not { } percent || percent <= 0 || percent > 100)
            {
                throw new DomainException("A speculative partner needs a profit percent between 0 and 100.");
            }
        }
        else if (speculativeProfitPercent is not null)
        {
            throw new DomainException("A capital partner's share comes from capital, not a fixed percent.");
        }

        FullName = fullName.Trim();
        UserId = userId;
        SpeculativeProfitPercent = speculativeProfitPercent;
        Notes = string.IsNullOrWhiteSpace(notes) ? null : notes.Trim();
    }

    public void SetActive(bool isActive) => IsActive = isActive;

    public void SetTelegramPhone(string? phone)
    {
        if (phone is { Length: > MaxTelegramPhoneLength })
        {
            throw new DomainException("Telegram phone is too long.");
        }

        TelegramPhone = string.IsNullOrWhiteSpace(phone) ? null : phone.Trim();
    }

    public void IssueCashierBarcode(string barcodeHash, DateTime issuedAtUtc)
    {
        if (string.IsNullOrWhiteSpace(barcodeHash))
        {
            throw new DomainException("Barcode hash is required.");
        }

        CashierBarcodeHash = barcodeHash;
        CashierBarcodeIssuedAtUtc = issuedAtUtc;
    }

    public void RevokeCashierBarcode()
    {
        CashierBarcodeHash = null;
        CashierBarcodeIssuedAtUtc = null;
    }
}

/// <summary>
/// كود تلغرام لسحب شريك من الكاشير (29/9/2026): الكاشير بيطلبه، بيوصل للشريك على تلغرام، الشريك بيكتبه بالكاشير.
/// مربوط بمبلغ السحب، صالح 5 دقايق، لسحب واحد (ConsumedByClientRequestId - إعادة إرسال نفس السحب بتنقبل)، و5 محاولات غلط بتحرقه.
/// </summary>
public class PartnerOtpChallenge : Entity
{
    public const int MaxFailedAttempts = 5;

    public Guid BranchId { get; private set; }
    public Guid PartnerId { get; private set; }
    public string CodeHash { get; private set; } = null!;

    /// <summary>المبلغ اللي انطلب الكود عشانه - الكود ما بيمشي لمبلغ تاني.</summary>
    public decimal Amount { get; private set; }

    public DateTime CreatedAtUtc { get; private set; }
    public DateTime ExpiresAtUtc { get; private set; }
    public int FailedAttempts { get; private set; }
    public DateTime? ConsumedAtUtc { get; private set; }
    public Guid? ConsumedByClientRequestId { get; private set; }
    public Guid RequestedByUserId { get; private set; }

    private PartnerOtpChallenge() { } // EF Core

    public PartnerOtpChallenge(
        Guid branchId, Guid partnerId, string codeHash, decimal amount, DateTime createdAtUtc, DateTime expiresAtUtc, Guid requestedByUserId)
    {
        BranchId = branchId;
        PartnerId = partnerId;
        CodeHash = codeHash;
        Amount = amount;
        CreatedAtUtc = createdAtUtc;
        ExpiresAtUtc = expiresAtUtc;
        RequestedByUserId = requestedByUserId;
    }

    public bool IsUsable(DateTime utcNow) => ConsumedAtUtc is null && utcNow < ExpiresAtUtc && FailedAttempts < MaxFailedAttempts;

    public void RegisterFailedAttempt() => FailedAttempts++;

    public void Consume(DateTime utcNow, Guid clientRequestId)
    {
        ConsumedAtUtc = utcNow;
        ConsumedByClientRequestId = clientRequestId;
    }
}

public enum PartnerWithdrawalSource
{
    /// <summary>من كاش الصندوق - بينقص المتوقع بتقفيل الصندوق (CashDrawerLog PayOut).</summary>
    Drawer = 1,

    /// <summary>صاحب المحل دفع من جيبه - بلا أثر على الصندوق، وبينسجّل إله "مستحق لصاحب المحل".</summary>
    OwnerPocket = 2
}

/// <summary>سحب من رصيد شريك (فعل يدوي). مسموح أكتر من الرصيد - الفرق بيترحّل للشهر الجاي.</summary>
public class PartnerWithdrawal : Entity, IBranchOwned
{
    public const int MaxNotesLength = 500;

    public Guid BranchId { get; private set; }
    public Guid PartnerId { get; private set; }
    public decimal Amount { get; private set; }
    public PartnerWithdrawalSource Source { get; private set; }
    public DateTime OccurredAtUtc { get; private set; }
    public string? Notes { get; private set; }
    public Guid RecordedByUserId { get; private set; }

    /// <summary>لـOwnerPocket: صاحب المحل اللي دفع من جيبه (إله المستحق).</summary>
    public Guid? PaidByUserId { get; private set; }

    /// <summary>انسجّل من الكاشير بتحقق هوية الشريك (مش من لوحة الإدارة).</summary>
    public bool RecordedAtCashier { get; private set; }

    public Guid ClientRequestId { get; private set; }

    private PartnerWithdrawal() { } // EF Core

    public PartnerWithdrawal(
        Guid branchId, Guid partnerId, decimal amount, PartnerWithdrawalSource source, DateTime occurredAtUtc,
        string? notes, Guid recordedByUserId, Guid? paidByUserId, bool recordedAtCashier, Guid clientRequestId)
    {
        if (amount <= 0)
        {
            throw new DomainException("Withdrawal amount must be positive.");
        }

        if (clientRequestId == Guid.Empty)
        {
            throw new DomainException("A client request id is required.");
        }

        if (source == PartnerWithdrawalSource.OwnerPocket && paidByUserId is null)
        {
            throw new DomainException("A pocket withdrawal needs the owner who paid.");
        }

        BranchId = branchId;
        PartnerId = partnerId;
        Amount = amount;
        Source = source;
        OccurredAtUtc = occurredAtUtc;
        Notes = string.IsNullOrWhiteSpace(notes) ? null : notes.Trim();
        RecordedByUserId = recordedByUserId;
        PaidByUserId = source == PartnerWithdrawalSource.OwnerPocket ? paidByUserId : null;
        RecordedAtCashier = recordedAtCashier;
        ClientRequestId = clientRequestId;
    }
}

/// <summary>
/// الكشف الشهري لفرع: ربح الشهر (Snapshot مجمَّد) موزَّع على الشركاء. واحد بس لكل (فرع، سنة، شهر) - بينزل
/// تلقائيًا ببداية الشهر، والإدارة بتقدر تعيد إصداره (فاتورة شراء متأخرة غيّرت الربح مثلًا).
/// </summary>
public class PartnerMonthlyStatement : AuditableEntity, IBranchOwned
{
    public Guid BranchId { get; private set; }
    public int Year { get; private set; }
    public int Month { get; private set; }
    public decimal NetProfit { get; private set; }

    /// <summary>ربح ما انوزّع على حدا (ما في شركاء رأس مال بالفرع) - بيبين بالكشف كتنبيه.</summary>
    public decimal UnallocatedAmount { get; private set; }

    public DateTime GeneratedAtUtc { get; private set; }
    public bool IsAutomatic { get; private set; }

    private readonly List<PartnerStatementLine> _lines = new();
    public IReadOnlyCollection<PartnerStatementLine> Lines => _lines.AsReadOnly();

    private PartnerMonthlyStatement() { } // EF Core

    public PartnerMonthlyStatement(Guid branchId, int year, int month)
    {
        if (month is < 1 or > 12)
        {
            throw new DomainException("Month must be between 1 and 12.");
        }

        BranchId = branchId;
        Year = year;
        Month = month;
    }

    /// <summary>بيستبدل محتوى الكشف كامل (أول إصدار أو إعادة إصدار).</summary>
    public void SetContent(decimal netProfit, decimal unallocatedAmount, DateTime generatedAtUtc, bool isAutomatic,
        IEnumerable<(Guid PartnerId, PartnerType Type, decimal? CapitalBalance, decimal SharePercent, decimal ShareAmount)> lines)
    {
        NetProfit = netProfit;
        UnallocatedAmount = unallocatedAmount;
        GeneratedAtUtc = generatedAtUtc;
        IsAutomatic = isAutomatic;
        _lines.Clear();
        foreach (var line in lines)
        {
            _lines.Add(new PartnerStatementLine(Id, line.PartnerId, line.Type, line.CapitalBalance, line.SharePercent, line.ShareAmount));
        }
    }
}

public class PartnerStatementLine : Entity
{
    public Guid StatementId { get; private set; }
    public Guid PartnerId { get; private set; }
    public PartnerType PartnerType { get; private set; }

    /// <summary>رأس مال الشريك آخر الشهر (لشريك رأس المال) - أساس نسبته.</summary>
    public decimal? CapitalBalance { get; private set; }

    /// <summary>نسبته الفعلية من صافي ربح الشهر.</summary>
    public decimal SharePercent { get; private set; }

    /// <summary>نصيبه بالدينار (سالب بشهر خسارة لشريك رأس المال).</summary>
    public decimal ShareAmount { get; private set; }

    private PartnerStatementLine() { } // EF Core

    internal PartnerStatementLine(Guid statementId, Guid partnerId, PartnerType partnerType, decimal? capitalBalance, decimal sharePercent, decimal shareAmount)
    {
        StatementId = statementId;
        PartnerId = partnerId;
        PartnerType = partnerType;
        CapitalBalance = capitalBalance;
        SharePercent = sharePercent;
        ShareAmount = shareAmount;
    }
}

public enum OwnerReceivableEntryType
{
    /// <summary>صاحب المحل دفع لشريك من جيبه - المحل صار مديون إله.</summary>
    PaidPartnerFromPocket = 1,

    /// <summary>صاحب المحل استرجع حقه (فعل يدوي صريح).</summary>
    Repayment = 2
}

public enum OwnerRepaymentSource
{
    /// <summary>أخدها من كاش الصندوق - بتنقص المتوقع بالتقفيل.</summary>
    Drawer = 1,

    /// <summary>برّا الصندوق (تحويل بنكي مثلًا) - بلا أثر على الصندوق.</summary>
    Outside = 2
}

/// <summary>
/// "مستحق لصاحب المحل" (قرار 21/9/2026 - الخيار الأدق): كل سحب شريك "من جيبي" بيزيده تلقائيًا بنفس
/// المبلغ، والاسترجاع بيصير بفعل يدوي صريح بس. مستقل كليًا عن أرصدة الشركاء وعن CapitalTransaction.
/// </summary>
public class OwnerReceivableEntry : Entity, IBranchOwned
{
    public const int MaxNotesLength = 500;

    public Guid BranchId { get; private set; }
    public Guid OwnerUserId { get; private set; }
    public OwnerReceivableEntryType Type { get; private set; }
    public decimal Amount { get; private set; }
    public DateTime OccurredAtUtc { get; private set; }
    public Guid? PartnerWithdrawalId { get; private set; }
    public OwnerRepaymentSource? RepaymentSource { get; private set; }
    public string? Notes { get; private set; }
    public Guid RecordedByUserId { get; private set; }
    public Guid? ClientRequestId { get; private set; }

    private OwnerReceivableEntry() { } // EF Core

    private OwnerReceivableEntry(Guid branchId, Guid ownerUserId, OwnerReceivableEntryType type, decimal amount, DateTime occurredAtUtc,
        Guid? partnerWithdrawalId, OwnerRepaymentSource? repaymentSource, string? notes, Guid recordedByUserId, Guid? clientRequestId)
    {
        if (amount <= 0)
        {
            throw new DomainException("Owner receivable amount must be positive; direction is expressed by Type.");
        }

        BranchId = branchId;
        OwnerUserId = ownerUserId;
        Type = type;
        Amount = amount;
        OccurredAtUtc = occurredAtUtc;
        PartnerWithdrawalId = partnerWithdrawalId;
        RepaymentSource = repaymentSource;
        Notes = string.IsNullOrWhiteSpace(notes) ? null : notes.Trim();
        RecordedByUserId = recordedByUserId;
        ClientRequestId = clientRequestId;
    }

    public static OwnerReceivableEntry ForPocketWithdrawal(PartnerWithdrawal withdrawal) => new(
        withdrawal.BranchId, withdrawal.PaidByUserId ?? throw new DomainException("Pocket withdrawal without owner."),
        OwnerReceivableEntryType.PaidPartnerFromPocket, withdrawal.Amount, withdrawal.OccurredAtUtc,
        withdrawal.Id, null, withdrawal.Notes, withdrawal.RecordedByUserId, null);

    public static OwnerReceivableEntry Repayment(Guid branchId, Guid ownerUserId, decimal amount, DateTime occurredAtUtc,
        OwnerRepaymentSource source, string? notes, Guid recordedByUserId, Guid clientRequestId) => new(
        branchId, ownerUserId, OwnerReceivableEntryType.Repayment, amount, occurredAtUtc,
        null, source, notes, recordedByUserId, clientRequestId);
}
