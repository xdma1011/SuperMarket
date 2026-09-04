using System.Globalization;
using Microsoft.EntityFrameworkCore;
using SupermarketSystem.Application.Common.Interfaces;
using SupermarketSystem.Application.Common.Results;
using SupermarketSystem.Application.System.GetAdminSettings;
using SupermarketSystem.Domain.Settings;

namespace SupermarketSystem.Application.System.UpdateAdminSetting;

public sealed record UpdateAdminSettingCommand(string Key, string Value);

public sealed record UpdateAdminSettingResponse(string Key, string Value);

/// <summary>
/// نقطة الكتابة الوحيدة لصفحة الإعدادات الحسّاسة. الأمان هون كله بسطر
/// التحقق من whitelist (GetAdminSettingsHandler.ManagedSettings) - بدونه
/// هالـendpoint كان رح يسمح بالكتابة على أي SystemSettings.Key بما فيها
/// أسرار زي مفاتيح API، فالتحقق إلزامي قبل أي شيء ثاني.
/// </summary>
public sealed class UpdateAdminSettingHandler
{
    private readonly IApplicationDbContext _context;
    private readonly ISettingsProvider _settingsProvider;

    public UpdateAdminSettingHandler(IApplicationDbContext context, ISettingsProvider settingsProvider)
    {
        _context = context;
        _settingsProvider = settingsProvider;
    }

    public async Task<Result<UpdateAdminSettingResponse>> HandleAsync(
        UpdateAdminSettingCommand command, CancellationToken cancellationToken)
    {
        var managedSetting = GetAdminSettingsHandler.ManagedSettings
            .FirstOrDefault(m => m.Key == command.Key);

        if (managedSetting.Key is null)
        {
            return Result.Failure<UpdateAdminSettingResponse>(
                Error.Validation("AdminSettings.UnknownKey", $"الإعداد '{command.Key}' غير مسموح بتعديله من هذه الصفحة."));
        }

        var normalizedValue = NormalizeValue(command.Value, managedSetting.DataType);
        if (normalizedValue is null)
        {
            return Result.Failure<UpdateAdminSettingResponse>(
                Error.Validation("AdminSettings.InvalidValue", $"القيمة المدخلة لإعداد '{managedSetting.Label}' غير صحيحة."));
        }

        var setting = await _context.SystemSettings
            .FirstOrDefaultAsync(s => s.Key == command.Key, cancellationToken);

        if (setting is null)
        {
            setting = new SystemSetting(command.Key, normalizedValue, description: null);
            _context.SystemSettings.Add(setting);
        }
        else
        {
            setting.UpdateValue(normalizedValue);
        }

        await _context.SaveChangesAsync(cancellationToken);
        _settingsProvider.Invalidate(command.Key);

        return Result.Success(new UpdateAdminSettingResponse(command.Key, normalizedValue));
    }

    private static string? NormalizeValue(string rawValue, AdminSettingDataType dataType)
    {
        switch (dataType)
        {
            case AdminSettingDataType.Boolean:
                return bool.TryParse(rawValue, out var boolValue) ? boolValue.ToString() : null;

            case AdminSettingDataType.Decimal:
                return decimal.TryParse(rawValue, NumberStyles.Number, CultureInfo.InvariantCulture, out var decimalValue)
                    ? decimalValue.ToString(CultureInfo.InvariantCulture)
                    : null;

            case AdminSettingDataType.String:
                return rawValue;

            default:
                return null;
        }
    }
}
