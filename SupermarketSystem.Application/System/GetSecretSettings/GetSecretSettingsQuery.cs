using Microsoft.EntityFrameworkCore;
using SupermarketSystem.Application.Common.Interfaces;

namespace SupermarketSystem.Application.System.GetSecretSettings;

public sealed record SecretSettingDto(string Key, string Label, bool IsSet);

public sealed record GetSecretSettingsResponse(IReadOnlyList<SecretSettingDto> Secrets);

/// <summary>
/// نفس فكرة GetAdminSettingsHandler بالضبط (whitelist صريحة)، بس **القيمة
/// نفسها ما ترجع أبدًا** - IsSet بس (هل فيه قيمة محفوظة ولا لأ). هذا هو
/// الفرق الجوهري عن صفحة الإعدادات العادية اللي استبعدت هالمفاتيح
/// أصلًا لهالسبب بالضبط (راجع تعليق GetAdminSettingsHandler).
/// </summary>
public sealed class GetSecretSettingsHandler
{
    internal static readonly (string Key, string Label)[] ManagedSecrets =
    {
        (InvoiceOcrSettingsKeys.GeminiApiKey, "مفتاح Gemini API (قراءة فواتير الشراء بالذكاء الاصطناعي)"),
        (InvoiceOcrSettingsKeys.ClaudeApiKey, "مفتاح Claude API (قراءة فواتير الشراء - احتياطي)"),
        (TelegramSettingsKeys.BotToken, "توكن بوت تلغرام (تسجيل دخول الزبائن برقم الهاتف)"),
        (TelegramSettingsKeys.WebhookSecret, "سرّ التحقق من Webhook تلغرام (secret_token)"),
        (FirebaseSettingsKeys.ServiceAccountJson, "ملف Firebase Service Account (إشعارات تطبيق الزبائن)")
    };

    private readonly IApplicationDbContext _context;

    public GetSecretSettingsHandler(IApplicationDbContext context)
    {
        _context = context;
    }

    public async Task<GetSecretSettingsResponse> HandleAsync(CancellationToken cancellationToken)
    {
        var keys = ManagedSecrets.Select(m => m.Key).ToList();

        var setKeys = await _context.SystemSettings.AsNoTracking()
            .Where(s => keys.Contains(s.Key) && s.Value != "")
            .Select(s => s.Key)
            .ToListAsync(cancellationToken);

        var setKeysLookup = setKeys.ToHashSet();

        var secrets = ManagedSecrets
            .Select(m => new SecretSettingDto(m.Key, m.Label, setKeysLookup.Contains(m.Key)))
            .ToList();

        return new GetSecretSettingsResponse(secrets);
    }
}
