using SupermarketSystem.Domain.Common;

namespace SupermarketSystem.Domain.Customers;

/// <summary>
/// ربط رقم هاتف بمعرّف محادثة تلغرام (chat_id) - يُنشأ لما الزبون يفتح
/// البوت ويشارك رقم هاتفه (زر "مشاركة رقم الهاتف"، Update.message.contact
/// من Webhook). بدون هذا الربط ما فيه طريقة نرسل فيها OTP للزبون عبر
/// تلغرام - رقم الهاتف وحده مش كافي، البوت يحتاج chat_id ليرسل رسالة.
/// </summary>
public class TelegramChatLink : Entity
{
    public string Phone { get; private set; } = null!;
    public string ChatId { get; private set; } = null!;
    public DateTime LinkedAtUtc { get; private set; }

    private TelegramChatLink() { } // EF Core

    public TelegramChatLink(string phone, string chatId, DateTime linkedAtUtc)
    {
        Phone = phone;
        ChatId = chatId;
        LinkedAtUtc = linkedAtUtc;
    }

    /// <summary>نفس الرقم ممكن يعيد الربط من جهاز/محادثة تلغرام جديدة - آخر ربط هو الفعّال.</summary>
    public void Relink(string chatId, DateTime linkedAtUtc)
    {
        ChatId = chatId;
        LinkedAtUtc = linkedAtUtc;
    }
}
