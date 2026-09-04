using Microsoft.EntityFrameworkCore;
using SupermarketSystem.Application.Common.Interfaces;
using SupermarketSystem.Domain.Customers;

namespace SupermarketSystem.Application.CustomerAuth.LinkTelegramContact;

public sealed record LinkTelegramContactCommand(string ChatId, string Phone);

/// <summary>
/// يُستدعى من TelegramWebhookEndpoint لما Update وارد يحمل contact (الزبون
/// ضغط زر "مشاركة رقم الهاتف" بعد /start). طبيعي وغير عرضي إعادة الاستدعاء
/// بنفس الرقم من chat_id مختلف (تغيير جهاز) - Relink بيحدّث الربط القديم،
/// لا يضيف سطر مكرَّر (راجع الفهرس الفريد على Phone بـTelegramChatLinkConfiguration).
/// </summary>
public sealed class LinkTelegramContactHandler
{
    private readonly IApplicationDbContext _context;
    private readonly IDateTimeProvider _dateTimeProvider;

    public LinkTelegramContactHandler(IApplicationDbContext context, IDateTimeProvider dateTimeProvider)
    {
        _context = context;
        _dateTimeProvider = dateTimeProvider;
    }

    public async Task HandleAsync(LinkTelegramContactCommand command, CancellationToken cancellationToken)
    {
        var phone = NormalizePhone(command.Phone);
        var now = _dateTimeProvider.UtcNow;

        var existing = await _context.TelegramChatLinks.FirstOrDefaultAsync(l => l.Phone == phone, cancellationToken);
        if (existing is null)
        {
            _context.TelegramChatLinks.Add(new TelegramChatLink(phone, command.ChatId, now));
        }
        else
        {
            existing.Relink(command.ChatId, now);
        }

        await _context.SaveChangesAsync(cancellationToken);
    }

    /// <summary>تلغرام يرسل الرقم بصيغة دولية (+9627...) - نفس الصيغة المتوقَّعة من تطبيق الزبائن عند الطلب/التحقق.</summary>
    private static string NormalizePhone(string phone) => phone.Trim().TrimStart('+');
}
