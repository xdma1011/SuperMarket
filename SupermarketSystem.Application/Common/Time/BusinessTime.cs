using Microsoft.EntityFrameworkCore;
using SupermarketSystem.Application.Common.Interfaces;

namespace SupermarketSystem.Application.Common.Time;

/// <summary>مفاتيح إعدادات التوقيت (SystemSettings).</summary>
public static class BusinessTimeKeys
{
    /// <summary>منطقة IANA (مثلًا Asia/Amman) - الافتراضي لو فاضي.</summary>
    public const string TimeZoneId = "System.TimeZoneId";

    /// <summary>فرق ثابت يدوي عن UTC بالدقائق (مثلًا 180 = +3). فاضي = حسب المنطقة. لو محدَّد بيغلب المنطقة.</summary>
    public const string FixedUtcOffsetMinutes = "System.FixedUtcOffsetMinutes";

    public const string DefaultTimeZoneId = "Asia/Amman";
}

/// <summary>
/// توقيت المحل (29/9/2026، طلب صاحب المشروع): التخزين كله UTC (الموصى فيه، ما تغيّر)، والتحويل للتوقيت المحلي
/// بمكان واحد هون - أيام التقارير، أشهر كشف الربح، رقم طلب المساعد اليومي. مصدر واحد عشان ما يصير "اليوم" أو
/// "الشهر" بمعنيين بمكانين.
///
/// المنطقة من الإعدادات (افتراضي Asia/Amman). وفي فرق يدوي ثابت (+2/+3...) بيغلب المنطقة - بالأردن التوقيت الشتوي
/// مرات بيتطبّق ومرات لأ، فصاحب المحل بيحدده بإيده لو المنطقة ما طابقت الواقع. منطقة مش معروفة على الجهاز = +3.
/// </summary>
public sealed record BusinessTime(string TimeZoneId, int? FixedUtcOffsetMinutes)
{
    public static readonly BusinessTime Default = new(BusinessTimeKeys.DefaultTimeZoneId, null);

    private static readonly TimeSpan FallbackOffset = TimeSpan.FromHours(3);

    public static async Task<BusinessTime> LoadAsync(IApplicationDbContext context, CancellationToken cancellationToken)
    {
        var keys = new[] { BusinessTimeKeys.TimeZoneId, BusinessTimeKeys.FixedUtcOffsetMinutes };
        var values = await context.SystemSettings.AsNoTracking()
            .Where(s => keys.Contains(s.Key))
            .ToDictionaryAsync(s => s.Key, s => s.Value, cancellationToken);

        var zone = values.GetValueOrDefault(BusinessTimeKeys.TimeZoneId);
        var fixedText = values.GetValueOrDefault(BusinessTimeKeys.FixedUtcOffsetMinutes);
        int? fixedMinutes = int.TryParse(fixedText, out var parsed) ? parsed : null;
        return new BusinessTime(string.IsNullOrWhiteSpace(zone) ? BusinessTimeKeys.DefaultTimeZoneId : zone.Trim(), fixedMinutes);
    }

    public static TimeZoneInfo? TryFindZone(string? timeZoneId)
    {
        if (string.IsNullOrWhiteSpace(timeZoneId))
        {
            return null;
        }

        try
        {
            return TimeZoneInfo.FindSystemTimeZoneById(timeZoneId.Trim());
        }
        catch (Exception ex) when (ex is TimeZoneNotFoundException or InvalidTimeZoneException)
        {
            return null;
        }
    }

    public static bool IsValidFixedOffset(int minutes) => minutes is >= -12 * 60 and <= 14 * 60 && minutes % 15 == 0;

    /// <summary>الفرق عن UTC عند لحظة معيّنة (بيتغيّر مع التوقيت الصيفي لو المنطقة إلها صيفي).</summary>
    public TimeSpan OffsetAt(DateTime utc)
    {
        if (FixedUtcOffsetMinutes is { } minutes)
        {
            return TimeSpan.FromMinutes(minutes);
        }

        var zone = TryFindZone(TimeZoneId);
        return zone?.GetUtcOffset(DateTime.SpecifyKind(utc, DateTimeKind.Utc)) ?? FallbackOffset;
    }

    public DateTime ToLocal(DateTime utc)
    {
        var u = DateTime.SpecifyKind(utc, DateTimeKind.Utc);
        return DateTime.SpecifyKind(u + OffsetAt(u), DateTimeKind.Unspecified);
    }

    /// <summary>وقت محلي (جدار الساعة) ← UTC. بلحظة تغيير التوقيت الصيفي بياخد فرق اللحظة التقريبية - كافي لحدود أيام/أشهر.</summary>
    public DateTime ToUtc(DateTime local)
    {
        var l = DateTime.SpecifyKind(local, DateTimeKind.Unspecified);
        var guess = DateTime.SpecifyKind(l - OffsetAt(DateTime.SpecifyKind(l, DateTimeKind.Utc)), DateTimeKind.Utc);
        return DateTime.SpecifyKind(l - OffsetAt(guess), DateTimeKind.Utc);
    }

    public DateOnly LocalDate(DateTime utc) => DateOnly.FromDateTime(ToLocal(utc));

    public (DateTime StartUtc, DateTime EndUtc) DayRangeUtc(DateOnly localDate)
    {
        var start = localDate.ToDateTime(TimeOnly.MinValue);
        return (ToUtc(start), ToUtc(start.AddDays(1)));
    }

    /// <summary>حدود شهر محلي بـUTC - [بداية، نهاية).</summary>
    public (DateTime StartUtc, DateTime EndUtc) MonthRangeUtc(int year, int month)
    {
        var start = new DateTime(year, month, 1, 0, 0, 0, DateTimeKind.Unspecified);
        return (ToUtc(start), ToUtc(start.AddMonths(1)));
    }

    public (int Year, int Month) LocalMonth(DateTime utc)
    {
        var local = ToLocal(utc);
        return (local.Year, local.Month);
    }
}
