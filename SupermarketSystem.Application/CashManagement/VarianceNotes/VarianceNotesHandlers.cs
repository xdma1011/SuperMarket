using Microsoft.EntityFrameworkCore;
using SupermarketSystem.Application.Common.Interfaces;
using SupermarketSystem.Application.Common.Results;
using SupermarketSystem.Domain.CashManagement;
using SupermarketSystem.Domain.Identity;

namespace SupermarketSystem.Application.CashManagement.VarianceNotes;

public static class VarianceNoteMath
{
    /// <summary>الفرق غير المفسَّر بنفس إشارة فرق التقفيل (سالب = عجز لسه بلا تفسير كامل)، صفر لو مفسَّر بالكامل.</summary>
    public static decimal Unexplained(decimal variance, decimal explainedTotal)
    {
        var remaining = Math.Max(0m, Math.Abs(variance) - explainedTotal);
        return variance < 0 ? -remaining : remaining;
    }
}

public sealed record RecordVarianceNoteCommand(
    Guid CashClosingId,
    VarianceExplanationReason Reason,
    // مقدار موجب من |الفرق| اللي هالسبب بيفسّره (صفر مقبول لـ"ما في تفسير").
    decimal ExplainedAmount,
    string? Note,
    // مصروف/دفعة منسيّة اتسجّلت كمصروف عادي (بلا PayOut) - ربط للمرجع فقط.
    Guid? RelatedExpenseId = null);

public sealed record VarianceNoteDto(
    Guid Id,
    int ReasonCode,
    string ReasonTitle,
    decimal ExplainedAmount,
    string? Note,
    Guid? RelatedExpenseId,
    string RecordedByName,
    DateTime RecordedAtUtc);

public sealed record VarianceNotesResponse(
    Guid CashClosingId,
    decimal Variance,
    decimal ExplainedTotal,
    decimal UnexplainedVariance,
    int PendingSalesCount,
    decimal PendingSalesAmount,
    IReadOnlyList<VarianceNoteDto> Notes);

public static class VarianceReasonText
{
    public static string Title(VarianceExplanationReason reason) => reason switch
    {
        VarianceExplanationReason.LateSales => "بيعات متأخرة (بطابور الكاشير)",
        VarianceExplanationReason.ForgottenDrawerPayment => "دفعة/مصروف من الدرج ما انسجّل",
        VarianceExplanationReason.CountError => "خطأ بعدّ الكاش",
        VarianceExplanationReason.Unknown => "غير معروف",
        _ => "سبب آخر"
    };
}

/// <summary>
/// بند 23: صاحب المحل بيسجّل تفسير فرق تقفيل الصندوق بعد ما الكاشير يحكيله. سجل تاريخي بحت - بلا أي تعديل على التقفيل
/// ولا حركة صندوق (راجع تعليق CashClosingVarianceNote). مسموح أكتر من تفسير للتقفيل الواحد (سببين مختلفين)، بس مجموع
/// المفسَّر ما بيتجاوز |الفرق| - هاد فحص اتساق بيانات، مش منع لعملية مشبوهة (§1.6 ما بينطبق).
/// </summary>
public sealed class RecordVarianceNoteHandler
{
    private readonly IApplicationDbContext _context;
    private readonly ICurrentUserContext _currentUser;
    private readonly IDateTimeProvider _dateTimeProvider;

    public RecordVarianceNoteHandler(IApplicationDbContext context, ICurrentUserContext currentUser, IDateTimeProvider dateTimeProvider)
    {
        _context = context;
        _currentUser = currentUser;
        _dateTimeProvider = dateTimeProvider;
    }

    public async Task<Result<VarianceNotesResponse>> HandleAsync(RecordVarianceNoteCommand command, CancellationToken cancellationToken)
    {
        if (!Enum.IsDefined(command.Reason))
        {
            return Result.Failure<VarianceNotesResponse>(Error.Validation("VarianceNote.ReasonInvalid", "سبب الفرق مش صحيح."));
        }

        if (command.ExplainedAmount < 0)
        {
            return Result.Failure<VarianceNotesResponse>(Error.Validation("VarianceNote.AmountNegative", "المبلغ المفسَّر لا يمكن أن يكون سالبًا."));
        }

        if (command.Reason == VarianceExplanationReason.Other && string.IsNullOrWhiteSpace(command.Note))
        {
            return Result.Failure<VarianceNotesResponse>(Error.Validation("VarianceNote.NoteRequired", "اكتب ملاحظة توضّح السبب."));
        }

        if (command.Note is { } note && note.Length > CashClosingVarianceNote.MaxNoteLength)
        {
            return Result.Failure<VarianceNotesResponse>(Error.Validation("VarianceNote.NoteTooLong", "الملاحظة طويلة (أقصاها 500 حرف)."));
        }

        var closing = await _context.CashClosings.AsNoTracking().FirstOrDefaultAsync(c => c.Id == command.CashClosingId, cancellationToken);
        if (closing is null)
        {
            return Result.Failure<VarianceNotesResponse>(Error.NotFound("VarianceNote.ClosingNotFound", "التقفيل مش موجود."));
        }

        if (closing.Variance == 0)
        {
            return Result.Failure<VarianceNotesResponse>(Error.BusinessRule("VarianceNote.NoVariance", "هذا التقفيل ما فيه فرق عشان نفسّره."));
        }

        var alreadyExplained = await _context.CashClosingVarianceNotes.AsNoTracking()
            .Where(n => n.CashClosingId == closing.Id)
            .SumAsync(n => (decimal?)n.ExplainedAmount, cancellationToken) ?? 0m;
        if (alreadyExplained + command.ExplainedAmount > Math.Abs(closing.Variance) + 0.0005m)
        {
            return Result.Failure<VarianceNotesResponse>(Error.BusinessRule(
                "VarianceNote.ExceedsVariance",
                $"المبلغ المفسَّر ({alreadyExplained + command.ExplainedAmount:0.000}) أكبر من الفرق ({Math.Abs(closing.Variance):0.000})."));
        }

        _context.CashClosingVarianceNotes.Add(new CashClosingVarianceNote(
            closing.Id, closing.BranchId, command.Reason, Math.Round(command.ExplainedAmount, 3, MidpointRounding.AwayFromZero),
            command.Note, command.RelatedExpenseId, _currentUser.UserId ?? User.SystemUserId, _dateTimeProvider.UtcNow));
        await _context.SaveChangesAsync(cancellationToken);

        return await new GetVarianceNotesHandler(_context).HandleAsync(closing.Id, cancellationToken);
    }
}

public sealed class GetVarianceNotesHandler
{
    private readonly IApplicationDbContext _context;

    public GetVarianceNotesHandler(IApplicationDbContext context)
    {
        _context = context;
    }

    public async Task<Result<VarianceNotesResponse>> HandleAsync(Guid cashClosingId, CancellationToken cancellationToken)
    {
        var closing = await _context.CashClosings.AsNoTracking().FirstOrDefaultAsync(c => c.Id == cashClosingId, cancellationToken);
        if (closing is null)
        {
            return Result.Failure<VarianceNotesResponse>(Error.NotFound("VarianceNote.ClosingNotFound", "التقفيل مش موجود."));
        }

        var rows = await _context.CashClosingVarianceNotes.AsNoTracking()
            .Where(n => n.CashClosingId == cashClosingId)
            .OrderBy(n => n.RecordedAtUtc)
            .ToListAsync(cancellationToken);

        var userIds = rows.Select(r => r.RecordedByUserId).Distinct().ToList();
        var names = userIds.Count == 0
            ? new Dictionary<Guid, string>()
            : await _context.Users.IgnoreQueryFilters().AsNoTracking()
                .Where(u => userIds.Contains(u.Id))
                .ToDictionaryAsync(u => u.Id, u => u.FullName, cancellationToken);

        var notes = rows.Select(r => new VarianceNoteDto(
                r.Id, (int)r.Reason, VarianceReasonText.Title(r.Reason), r.ExplainedAmount, r.Note, r.RelatedExpenseId,
                names.GetValueOrDefault(r.RecordedByUserId, "غير معروف"), r.RecordedAtUtc))
            .ToList();

        var explainedTotal = notes.Sum(n => n.ExplainedAmount);
        return Result.Success(new VarianceNotesResponse(
            closing.Id, closing.Variance, explainedTotal, VarianceNoteMath.Unexplained(closing.Variance, explainedTotal),
            closing.PendingSalesCount, closing.PendingSalesAmount, notes));
    }
}
