using Microsoft.EntityFrameworkCore;
using SupermarketSystem.Application.Common.Interfaces;
using SupermarketSystem.Application.Common.Results;
using SupermarketSystem.Application.Partners;
using SupermarketSystem.Application.Common.Time;
using SupermarketSystem.Domain.Settings;

namespace SupermarketSystem.Application.System.TimeSettings;

// =====================================================================================
// توقيت المحل (29/9/2026) - قراءة (للكل، حتى قبل الدخول: لوحة الإدارة والكاشير بيحتاجوه لعرض الأوقات) وتعديل
// (إعدادات حسّاسة). راجع BusinessTime.
// =====================================================================================

/// <summary>CurrentOffsetMinutes = الفرق المطبَّق هلق فعليًا (من الفرق اليدوي لو محدَّد، وإلا من المنطقة).</summary>
public sealed record TimeSettingsDto(
    string TimeZoneId, int? FixedUtcOffsetMinutes, int CurrentOffsetMinutes, bool IsTimeZoneKnown, DateTime ServerUtcNow, DateTime LocalNow);

public sealed record UpdateTimeSettingsCommand(string TimeZoneId, int? FixedUtcOffsetMinutes);

public sealed class GetTimeSettingsHandler
{
    private readonly IApplicationDbContext _context;
    private readonly IDateTimeProvider _dateTimeProvider;

    public GetTimeSettingsHandler(IApplicationDbContext context, IDateTimeProvider dateTimeProvider)
    {
        _context = context;
        _dateTimeProvider = dateTimeProvider;
    }

    public async Task<TimeSettingsDto> HandleAsync(CancellationToken cancellationToken)
    {
        var time = await BusinessTime.LoadAsync(_context, cancellationToken);
        return ToDto(time, _dateTimeProvider.UtcNow);
    }

    internal static TimeSettingsDto ToDto(BusinessTime time, DateTime nowUtc) => new(
        time.TimeZoneId, time.FixedUtcOffsetMinutes, (int)time.OffsetAt(nowUtc).TotalMinutes,
        BusinessTime.TryFindZone(time.TimeZoneId) is not null, nowUtc, time.ToLocal(nowUtc));
}

public sealed class UpdateTimeSettingsHandler
{
    private readonly IApplicationDbContext _context;
    private readonly ISettingsProvider _settingsProvider;
    private readonly IDateTimeProvider _dateTimeProvider;
    private readonly INotificationDispatcher _notificationDispatcher;

    public UpdateTimeSettingsHandler(
        IApplicationDbContext context, ISettingsProvider settingsProvider, IDateTimeProvider dateTimeProvider, INotificationDispatcher notificationDispatcher)
    {
        _context = context;
        _settingsProvider = settingsProvider;
        _dateTimeProvider = dateTimeProvider;
        _notificationDispatcher = notificationDispatcher;
    }

    public async Task<Result<TimeSettingsDto>> HandleAsync(UpdateTimeSettingsCommand command, CancellationToken cancellationToken)
    {
        var zoneId = command.TimeZoneId?.Trim() ?? "";
        if (BusinessTime.TryFindZone(zoneId) is null)
        {
            return Result.Failure<TimeSettingsDto>(Error.Validation("TimeSettings.UnknownZone",
                $"المنطقة \"{zoneId}\" مش معروفة على السيرفر - اختار منطقة من القائمة (مثلًا Asia/Amman)."));
        }

        if (command.FixedUtcOffsetMinutes is { } minutes && !BusinessTime.IsValidFixedOffset(minutes))
        {
            return Result.Failure<TimeSettingsDto>(Error.Validation("TimeSettings.InvalidOffset",
                "الفرق اليدوي لازم يكون بين -12 و+14 ساعة، ومضاعفات ربع ساعة."));
        }

        var previous = await BusinessTime.LoadAsync(_context, cancellationToken);
        await UpsertAsync(BusinessTimeKeys.TimeZoneId, zoneId, cancellationToken);
        await UpsertAsync(BusinessTimeKeys.FixedUtcOffsetMinutes, command.FixedUtcOffsetMinutes?.ToString() ?? "", cancellationToken);
        await _context.SaveChangesAsync(cancellationToken);
        _settingsProvider.Invalidate(BusinessTimeKeys.TimeZoneId);
        _settingsProvider.Invalidate(BusinessTimeKeys.FixedUtcOffsetMinutes);

        await MarkShiftedStatementsStaleAsync(previous, new BusinessTime(zoneId, command.FixedUtcOffsetMinutes), cancellationToken);

        return Result.Success(GetTimeSettingsHandler.ToDto(new BusinessTime(zoneId, command.FixedUtcOffsetMinutes), _dateTimeProvider.UtcNow));
    }

    /// <summary>
    /// تغيير المنطقة/الفرق بيحرّك حدود الشهور (بيعة الساعة 11 بالليل آخر يوم ممكن تصير بالشهر الجاي) - كشوف الشركاء النازلة انحسبت بالحدود
    /// القديمة، فأي كشف حدود شهره تغيّرت فعليًا بيتعلّم "قديم" + تنبيه (28/10/2026، ثغرة #11). ما بنمنع التغيير (§1.6).
    /// </summary>
    private async Task MarkShiftedStatementsStaleAsync(BusinessTime previous, BusinessTime current, CancellationToken cancellationToken)
    {
        var statements = await _context.PartnerMonthlyStatements.IgnoreQueryFilters().AsNoTracking()
            .Select(s => new { s.BranchId, s.Year, s.Month })
            .ToListAsync(cancellationToken);
        var shifted = statements.Where(s => previous.MonthRangeUtc(s.Year, s.Month) != current.MonthRangeUtc(s.Year, s.Month)).ToList();
        if (shifted.Count == 0)
        {
            return;
        }

        var describe = (BusinessTime t) => t.FixedUtcOffsetMinutes is { } m ? $"فرق يدوي {m / 60.0:+0.##;-0.##} ساعة" : t.TimeZoneId;
        var reason = $"تغيّر توقيت المحل ({describe(previous)} ← {describe(current)}) فحدود الشهر تحرّكت - الكشف انحسب بالحدود القديمة.";
        foreach (var branch in shifted.GroupBy(s => s.BranchId))
        {
            await PartnerStatementStaleness.MarkAndNotifyAsync(
                _context, _notificationDispatcher, _dateTimeProvider.UtcNow, branch.Key,
                branch.Select(s => (s.Year, s.Month)).ToList(), reason, cancellationToken);
        }
    }

    private async Task UpsertAsync(string key, string value, CancellationToken cancellationToken)
    {
        var setting = await _context.SystemSettings.FirstOrDefaultAsync(s => s.Key == key, cancellationToken);
        if (setting is null)
        {
            _context.SystemSettings.Add(new SystemSetting(key, value, description: null));
        }
        else
        {
            setting.UpdateValue(value);
        }
    }
}
