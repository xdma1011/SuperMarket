using Microsoft.EntityFrameworkCore;
using SupermarketSystem.Application.Common.Interfaces;
using SupermarketSystem.Application.Common.Results;
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

    public UpdateTimeSettingsHandler(IApplicationDbContext context, ISettingsProvider settingsProvider, IDateTimeProvider dateTimeProvider)
    {
        _context = context;
        _settingsProvider = settingsProvider;
        _dateTimeProvider = dateTimeProvider;
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

        await UpsertAsync(BusinessTimeKeys.TimeZoneId, zoneId, cancellationToken);
        await UpsertAsync(BusinessTimeKeys.FixedUtcOffsetMinutes, command.FixedUtcOffsetMinutes?.ToString() ?? "", cancellationToken);
        await _context.SaveChangesAsync(cancellationToken);
        _settingsProvider.Invalidate(BusinessTimeKeys.TimeZoneId);
        _settingsProvider.Invalidate(BusinessTimeKeys.FixedUtcOffsetMinutes);

        return Result.Success(GetTimeSettingsHandler.ToDto(new BusinessTime(zoneId, command.FixedUtcOffsetMinutes), _dateTimeProvider.UtcNow));
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
