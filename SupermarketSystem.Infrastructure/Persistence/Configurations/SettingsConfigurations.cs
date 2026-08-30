using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using SupermarketSystem.Application.CashManagement.CompleteCashClosing;
using SupermarketSystem.Application.Common.Interfaces;
using SupermarketSystem.Domain.Identity;
using SupermarketSystem.Domain.Settings;
using SupermarketSystem.Infrastructure.Services;

namespace SupermarketSystem.Infrastructure.Persistence.Configurations;

public class SystemSettingConfiguration : IEntityTypeConfiguration<SystemSetting>
{
    public void Configure(EntityTypeBuilder<SystemSetting> builder)
    {
        builder.ToTable("SystemSettings");
        builder.HasKey(s => s.Id);

        builder.Property(s => s.Key).IsRequired().HasMaxLength(200);
        builder.Property(s => s.Value).IsRequired().HasMaxLength(2000);
        builder.Property(s => s.Description).HasMaxLength(500);
        builder.Property(s => s.RowVersion).IsRowVersion();
        builder.Property(s => s.CreatedAtUtc).HasColumnType("datetime2").IsRequired();
        builder.Property(s => s.UpdatedAtUtc).HasColumnType("datetime2");

        builder.HasIndex(s => s.Key).IsUnique();

        // POS policy settings, seeded with the same defaults PosPolicyService
        // falls back to. Seeding them matters for a practical reason: an
        // administrator cannot switch off a setting that has no row to edit.
        // Without these, the policy would still work (the service defaults
        // cover a missing key) but the admin screen would show an empty list.
        //
        // This is reference/configuration data, which the brief permits
        // seeding — no business or transactional data is seeded anywhere.
        builder.HasData(
            NewSetting("99133340-d81f-4202-bebe-294ed26f0c41", "Pos.AllowVoidSale", "true",
                "Allow cashiers to void a completed sale. Voids never wait for approval; they are recorded and flagged for review."),
            NewSetting("445269e2-e8f6-482b-95da-d8a829f9e14e", "Pos.AllowReturn", "true",
                "Allow cashiers to process customer returns."),
            NewSetting("abd250d3-ef22-415c-b9fd-81a5850fc3d7", "Pos.AllowCrossMethodRefund", "false",
                "Allow refunding to a payment method other than the original sale's. Off by default: a cash refund against a card sale removes cash that never entered the drawer."),
            NewSetting("c536de35-d0ed-42ac-b551-b3851118015b", "Pos.AllowManualDiscount", "true",
                "Allow ad-hoc discounts keyed in at checkout."),
            NewSetting("e26ab4e9-5c07-4ad6-96d8-0732494eb625", "Pos.AllowPaymentReversal", "true",
                "Allow reversing a completed payment. The original payment is preserved; a reversal record is added."),
            NewSetting("64b0776a-6271-4c87-a1ab-bdfeb3b50361", "Pos.MaxManualDiscountPercentage", "10",
                "Maximum ad-hoc discount as a percentage of the line/invoice total. Set to 0 to disable manual discounts."),
            NewSetting("032121de-b688-4ee9-ab20-e5a16af6fe4f", "Pos.HighValueReturnThreshold", "0",
                "Returns above this value are still completed immediately, but flagged for management review. 0 disables value-based flagging."),
            // إعداد المخزون السالب — افتراضيًا "true" بناءً على قرار صاحب
            // النظام: البيع ما يتوقف أبدًا بسبب نقص المخزون بالنظام،
            // لأنه المخزون الحقيقي (الفعلي بالمحل) ممكن يكون موجود حتى لو
            // النظام لسه ما استلم فاتورة الشراء. هذا يلغي الافتراض الأصلي
            // بالـ Architecture Review ("Negative stock disallowed by
            // default") بقرار صريح موثّق، مش بالغلط أو بالصدفة.
            NewSetting("67264658-2e77-4241-8778-d5c8d20df993", InventorySettingsKeys.AllowNegativeStock, "true",
                "Allow a sale to proceed even when system stock is insufficient. Stock goes negative and is flagged for review rather than blocking the sale."),
            // فاضية عمدًا — القناة معطّلة لحد ما المستخدم يدخل التوكن ومعرّف
            // المحادثة بنفسه من الإعدادات. فشل هادئ، لا استثناء (نفس مبدأ
            // مفاتيح مزوّدي الذكاء الاصطناعي).
            NewSetting("9e2fa6ca-2101-4c8f-bbf9-fe594f468e7f", NotificationSettingsKeys.TelegramBotToken, "",
                "Telegram bot token. Empty disables the Telegram channel (silent, no error)."),
            NewSetting("50a800fa-ae5d-4f4e-a50b-ff8adb30e7ff", NotificationSettingsKeys.TelegramChatId, "",
                "Telegram chat id notifications are sent to. Empty disables the Telegram channel."),
            NewSetting("5fddc559-554c-4f20-8923-bfb5c1bb7c6e", CashClosingSettingsKeys.VarianceAlertThreshold, "0",
                "Cash-closing variance (absolute value) above which a notification is sent. 0 disables the alert."),
            // إعدادات قراءة فاتورة الشراء بالذكاء الاصطناعي — مفاتيح
            // API فاضية عمدًا (المستخدم يدخلها بنفسه لاحقًا من لوحة
            // الإعدادات)، أسماء الموديلات بقيمها الافتراضية الحالية.
            NewSetting("c2142398-e389-4c50-8ccf-1773249029d8", InvoiceOcrSettingsKeys.GeminiApiKey, "",
                "Gemini API key. Empty disables both Gemini providers (silent, no error)."),
            NewSetting("ba427a27-4945-401b-954f-a090612ec2aa", InvoiceOcrSettingsKeys.GeminiProModelName, "gemini-pro-latest",
                "Gemini model name for the primary attempt. 'latest' aliases are maintained by Google to always point at the current recommended model."),
            NewSetting("a7bd1207-ab41-4a51-bdd6-a7aed05d0b5d", InvoiceOcrSettingsKeys.GeminiFlashModelName, "gemini-flash-latest",
                "Gemini Flash model name for the second attempt."),
            NewSetting("ba5a86fd-e696-4aef-ad52-25257d882014", InvoiceOcrSettingsKeys.ClaudeApiKey, "",
                "Claude API key. Empty disables the last-resort fallback provider (silent, no error)."),
            NewSetting("e5f99833-a059-4192-92c7-33ed0d662169", InvoiceOcrSettingsKeys.ClaudeModelName, "claude-sonnet-5",
                "Claude model name for the final fallback attempt."),
            NewSetting("1c3e6a52-8f3a-4c6a-9f6e-2a6f7e9b0c1d", ImageStorageSettingsKeys.PurchaseInvoiceImagesDirectory, "PurchaseInvoiceImages",
                "Local directory where uploaded purchase-invoice images are stored as WebP, relative to the API process's working directory unless an absolute path is given."),
            NewSetting("7a2f4e91-3b6c-4d8a-9e1f-5c7b8a9d0e2f", Application.Authentication.Login.AuthSettingsKeys.MaxFailedLoginAttempts, "5",
                "Consecutive failed login attempts before temporary lockout. 0 disables lockout entirely."),
            NewSetting("8b3f5e92-4c7d-4e9b-0f2a-6d8c9b0e1f3a", Application.Authentication.Login.AuthSettingsKeys.LockoutDurationMinutes, "15",
                "Lockout duration in minutes once the failed-attempt threshold is reached."),
            NewSetting("c15d8f3a-6e2b-4a91-b7d4-9f0e3c5a8b1d", Application.Inventory.RecordComplimentaryIssue.ComplimentarySettingsKeys.DailyReviewThresholdQuantity, "10",
                "Daily quantity threshold (per product, across all branches) before a complimentary issue is auto-flagged for review. Never blocks — allow-with-review only."),
            NewSetting("e2a7b4c9-1f5d-4e83-9a6c-7d0b3f8e5c12", Application.Reporting.GetProductConsumptionLevels.ConsumptionLevelSettingsKeys.HighThreshold, "50",
                "Quantity sold within the query period (base unit) at or above which a product is classified as 'High' consumption."),
            NewSetting("f3b8c5d0-2a6e-4f94-8b7d-8e1c4a9f6d23", Application.Reporting.GetProductConsumptionLevels.ConsumptionLevelSettingsKeys.MediumThreshold, "15",
                "Quantity sold within the query period at or above which a product is classified as 'Medium' consumption (below High)."),
            NewSetting("a4c9d6e1-3b7f-4a05-9c8e-9f2d5b0a7e34", Application.Reporting.GetProductConsumptionLevels.ConsumptionLevelSettingsKeys.LowThreshold, "1",
                "Quantity sold within the query period at or above which a product is classified as 'Low' consumption (below Medium). Zero sales is always 'NearZero'."),
            NewSetting("b5d0e7f2-4c8a-4b16-9d9f-0a3e6c1b8f45", "Catalog.Version", "1",
                "Global catalog version counter — incremented atomically on every product/category/unit/price change. The cashier app (offline-first) polls this cheaply to know when to pull a full catalog re-sync."),
            NewSetting("d6e1f8a3-5c9b-4d27-8e4a-1b6c9d2e5f78", PendingReviewSettingsKeys.EscalationThresholdDays, "3",
                "Days a pending review item (unreviewed return, or NeedsReview stock movement) can stay unreviewed before PendingReviewEscalationBackgroundService flags it in an escalation notification."));
    }

    private static readonly DateTime SeedTimestamp = new(2026, 1, 1, 0, 0, 0, DateTimeKind.Utc);

    private static object NewSetting(string id, string key, string value, string description) => new
    {
        Id = Guid.Parse(id),
        Key = key,
        Value = value,
        Description = description,
        CreatedAtUtc = SeedTimestamp
    };
}

public class UserSettingConfiguration : IEntityTypeConfiguration<UserSetting>
{
    public void Configure(EntityTypeBuilder<UserSetting> builder)
    {
        builder.ToTable("UserSettings");
        builder.HasKey(s => s.Id);

        builder.Property(s => s.Key).IsRequired().HasMaxLength(200);
        builder.Property(s => s.Value).IsRequired().HasMaxLength(2000);

        builder.HasIndex(s => new { s.UserId, s.Key }).IsUnique();

        builder.HasOne<User>().WithMany().HasForeignKey(s => s.UserId).OnDelete(DeleteBehavior.Cascade);
    }
}

public class NotificationSettingConfiguration : IEntityTypeConfiguration<NotificationSetting>
{
    public void Configure(EntityTypeBuilder<NotificationSetting> builder)
    {
        builder.ToTable("NotificationSettings");
        builder.HasKey(s => s.Id);

        builder.Property(s => s.NotificationCode).IsRequired().HasMaxLength(100);

        builder.HasIndex(s => new { s.UserId, s.NotificationCode }).IsUnique();

        builder.HasOne<User>().WithMany().HasForeignKey(s => s.UserId).OnDelete(DeleteBehavior.Cascade);
    }
}
