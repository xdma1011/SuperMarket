using Microsoft.EntityFrameworkCore;
using SupermarketSystem.Application.Common.Interfaces;
using SupermarketSystem.Application.Common.Results;
using SupermarketSystem.Application.System.GetSecretSettings;
using SupermarketSystem.Domain.Settings;

namespace SupermarketSystem.Application.System.UpdateSecretSetting;

public sealed record UpdateSecretSettingCommand(string Key, string Value);

/// <summary>
/// نفس أمان UpdateAdminSettingHandler بالضبط - سطر التحقق من whitelist
/// هو الحماية الفعلية. هون بلا Result يحمل القيمة بالرد (خلافًا لصفحة
/// الإعدادات العادية) - نجاح/فشل بس، القيمة نفسها ما ترجع أبدًا حتى
/// بعد التحديث.
/// </summary>
public sealed class UpdateSecretSettingHandler
{
    private readonly IApplicationDbContext _context;
    private readonly ISettingsProvider _settingsProvider;

    public UpdateSecretSettingHandler(IApplicationDbContext context, ISettingsProvider settingsProvider)
    {
        _context = context;
        _settingsProvider = settingsProvider;
    }

    public async Task<Result> HandleAsync(UpdateSecretSettingCommand command, CancellationToken cancellationToken)
    {
        var isManaged = GetSecretSettingsHandler.ManagedSecrets.Any(m => m.Key == command.Key);
        if (!isManaged)
        {
            return Result.Failure(Error.Validation("SecretSetting.UnknownKey", $"المفتاح '{command.Key}' غير مسموح بتعديله من هذه الصفحة."));
        }

        var setting = await _context.SystemSettings.FirstOrDefaultAsync(s => s.Key == command.Key, cancellationToken);
        if (setting is null)
        {
            setting = new SystemSetting(command.Key, command.Value, description: null);
            _context.SystemSettings.Add(setting);
        }
        else
        {
            setting.UpdateValue(command.Value);
        }

        await _context.SaveChangesAsync(cancellationToken);
        _settingsProvider.Invalidate(command.Key);

        return Result.Success();
    }
}
