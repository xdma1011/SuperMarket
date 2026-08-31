using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace SupermarketSystem.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    /// <summary>
    /// يسدّ فجوة موجودة من قبل: SettingsConfigurations.cs عنده HasData لـ25
    /// إعداد إجمالًا، بس الـMigrations الموجودة (PhaseCFixes +
    /// CatalogAndPolicySettings) كانت بترحّل 7 بس (إعدادات Pos.* فقط).
    /// الـ17 إعداد الباقي (AllowNegativeStock، Telegram، OCR، Auth lockout،
    /// Complimentary threshold، Consumption levels، Catalog.Version) كانوا
    /// موجودين بالموديل (Domain code) بس بدون Migration توازيهم إطلاقًا —
    /// هذا بالضبط سبب "PendingModelChangesWarning" اللي بيظهر عند
    /// dotnet ef database update. لا حل كودي بديل غير سدّ الفجوة فعليًا هون.
    /// </summary>
    public partial class SeedMissingSystemSettings : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.InsertData(
                table: "SystemSettings",
                columns: new[] { "Id", "CreatedAtUtc", "CreatedByUserId", "Description", "Key", "UpdatedAtUtc", "UpdatedByUserId", "Value" },
                values: new object[,]
                {
                    { new Guid("67264658-2e77-4241-8778-d5c8d20df993"), new DateTime(2026, 1, 1, 0, 0, 0, 0, DateTimeKind.Utc), null, "Allow a sale to proceed even when system stock is insufficient. Stock goes negative and is flagged for review rather than blocking the sale.", "Inventory.AllowNegativeStock", null, null, "true" },
                    { new Guid("9e2fa6ca-2101-4c8f-bbf9-fe594f468e7f"), new DateTime(2026, 1, 1, 0, 0, 0, 0, DateTimeKind.Utc), null, "Telegram bot token. Empty disables the Telegram channel (silent, no error).", "Notifications.TelegramBotToken", null, null, "" },
                    { new Guid("50a800fa-ae5d-4f4e-a50b-ff8adb30e7ff"), new DateTime(2026, 1, 1, 0, 0, 0, 0, DateTimeKind.Utc), null, "Telegram chat id notifications are sent to. Empty disables the Telegram channel.", "Notifications.TelegramChatId", null, null, "" },
                    { new Guid("5fddc559-554c-4f20-8923-bfb5c1bb7c6e"), new DateTime(2026, 1, 1, 0, 0, 0, 0, DateTimeKind.Utc), null, "Cash-closing variance (absolute value) above which a notification is sent. 0 disables the alert.", "CashClosing.VarianceAlertThreshold", null, null, "0" },
                    { new Guid("c2142398-e389-4c50-8ccf-1773249029d8"), new DateTime(2026, 1, 1, 0, 0, 0, 0, DateTimeKind.Utc), null, "Gemini API key. Empty disables both Gemini providers (silent, no error).", "Ai.GeminiApiKey", null, null, "" },
                    { new Guid("ba427a27-4945-401b-954f-a090612ec2aa"), new DateTime(2026, 1, 1, 0, 0, 0, 0, DateTimeKind.Utc), null, "Gemini model name for the primary attempt. 'latest' aliases are maintained by Google to always point at the current recommended model.", "Ai.GeminiProModelName", null, null, "gemini-pro-latest" },
                    { new Guid("a7bd1207-ab41-4a51-bdd6-a7aed05d0b5d"), new DateTime(2026, 1, 1, 0, 0, 0, 0, DateTimeKind.Utc), null, "Gemini Flash model name for the second attempt.", "Ai.GeminiFlashModelName", null, null, "gemini-flash-latest" },
                    { new Guid("ba5a86fd-e696-4aef-ad52-25257d882014"), new DateTime(2026, 1, 1, 0, 0, 0, 0, DateTimeKind.Utc), null, "Claude API key. Empty disables the last-resort fallback provider (silent, no error).", "Ai.ClaudeApiKey", null, null, "" },
                    { new Guid("e5f99833-a059-4192-92c7-33ed0d662169"), new DateTime(2026, 1, 1, 0, 0, 0, 0, DateTimeKind.Utc), null, "Claude model name for the final fallback attempt.", "Ai.ClaudeModelName", null, null, "claude-sonnet-5" },
                    { new Guid("1c3e6a52-8f3a-4c6a-9f6e-2a6f7e9b0c1d"), new DateTime(2026, 1, 1, 0, 0, 0, 0, DateTimeKind.Utc), null, "Local directory where uploaded purchase-invoice images are stored as WebP, relative to the API process's working directory unless an absolute path is given.", "Storage.PurchaseInvoiceImagesDirectory", null, null, "PurchaseInvoiceImages" },
                    { new Guid("7a2f4e91-3b6c-4d8a-9e1f-5c7b8a9d0e2f"), new DateTime(2026, 1, 1, 0, 0, 0, 0, DateTimeKind.Utc), null, "Consecutive failed login attempts before temporary lockout. 0 disables lockout entirely.", "Auth.MaxFailedLoginAttempts", null, null, "5" },
                    { new Guid("8b3f5e92-4c7d-4e9b-0f2a-6d8c9b0e1f3a"), new DateTime(2026, 1, 1, 0, 0, 0, 0, DateTimeKind.Utc), null, "Lockout duration in minutes once the failed-attempt threshold is reached.", "Auth.LockoutDurationMinutes", null, null, "15" },
                    { new Guid("c15d8f3a-6e2b-4a91-b7d4-9f0e3c5a8b1d"), new DateTime(2026, 1, 1, 0, 0, 0, 0, DateTimeKind.Utc), null, "Daily quantity threshold (per product, across all branches) before a complimentary issue is auto-flagged for review. Never blocks - allow-with-review only.", "Complimentary.DailyReviewThresholdQuantity", null, null, "10" },
                    { new Guid("e2a7b4c9-1f5d-4e83-9a6c-7d0b3f8e5c12"), new DateTime(2026, 1, 1, 0, 0, 0, 0, DateTimeKind.Utc), null, "Quantity sold within the query period (base unit) at or above which a product is classified as 'High' consumption.", "ConsumptionLevel.HighThreshold", null, null, "50" },
                    { new Guid("f3b8c5d0-2a6e-4f94-8b7d-8e1c4a9f6d23"), new DateTime(2026, 1, 1, 0, 0, 0, 0, DateTimeKind.Utc), null, "Quantity sold within the query period at or above which a product is classified as 'Medium' consumption (below High).", "ConsumptionLevel.MediumThreshold", null, null, "15" },
                    { new Guid("a4c9d6e1-3b7f-4a05-9c8e-9f2d5b0a7e34"), new DateTime(2026, 1, 1, 0, 0, 0, 0, DateTimeKind.Utc), null, "Quantity sold within the query period at or above which a product is classified as 'Low' consumption (below Medium). Zero sales is always 'NearZero'.", "ConsumptionLevel.LowThreshold", null, null, "1" },
                    { new Guid("b5d0e7f2-4c8a-4b16-9d9f-0a3e6c1b8f45"), new DateTime(2026, 1, 1, 0, 0, 0, 0, DateTimeKind.Utc), null, "Global catalog version counter - incremented atomically on every product/category/unit/price change. The cashier app (offline-first) polls this cheaply to know when to pull a full catalog re-sync.", "Catalog.Version", null, null, "1" }
                });
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DeleteData(table: "SystemSettings", keyColumn: "Id", keyValue: new Guid("67264658-2e77-4241-8778-d5c8d20df993"));
            migrationBuilder.DeleteData(table: "SystemSettings", keyColumn: "Id", keyValue: new Guid("9e2fa6ca-2101-4c8f-bbf9-fe594f468e7f"));
            migrationBuilder.DeleteData(table: "SystemSettings", keyColumn: "Id", keyValue: new Guid("50a800fa-ae5d-4f4e-a50b-ff8adb30e7ff"));
            migrationBuilder.DeleteData(table: "SystemSettings", keyColumn: "Id", keyValue: new Guid("5fddc559-554c-4f20-8923-bfb5c1bb7c6e"));
            migrationBuilder.DeleteData(table: "SystemSettings", keyColumn: "Id", keyValue: new Guid("c2142398-e389-4c50-8ccf-1773249029d8"));
            migrationBuilder.DeleteData(table: "SystemSettings", keyColumn: "Id", keyValue: new Guid("ba427a27-4945-401b-954f-a090612ec2aa"));
            migrationBuilder.DeleteData(table: "SystemSettings", keyColumn: "Id", keyValue: new Guid("a7bd1207-ab41-4a51-bdd6-a7aed05d0b5d"));
            migrationBuilder.DeleteData(table: "SystemSettings", keyColumn: "Id", keyValue: new Guid("ba5a86fd-e696-4aef-ad52-25257d882014"));
            migrationBuilder.DeleteData(table: "SystemSettings", keyColumn: "Id", keyValue: new Guid("e5f99833-a059-4192-92c7-33ed0d662169"));
            migrationBuilder.DeleteData(table: "SystemSettings", keyColumn: "Id", keyValue: new Guid("1c3e6a52-8f3a-4c6a-9f6e-2a6f7e9b0c1d"));
            migrationBuilder.DeleteData(table: "SystemSettings", keyColumn: "Id", keyValue: new Guid("7a2f4e91-3b6c-4d8a-9e1f-5c7b8a9d0e2f"));
            migrationBuilder.DeleteData(table: "SystemSettings", keyColumn: "Id", keyValue: new Guid("8b3f5e92-4c7d-4e9b-0f2a-6d8c9b0e1f3a"));
            migrationBuilder.DeleteData(table: "SystemSettings", keyColumn: "Id", keyValue: new Guid("c15d8f3a-6e2b-4a91-b7d4-9f0e3c5a8b1d"));
            migrationBuilder.DeleteData(table: "SystemSettings", keyColumn: "Id", keyValue: new Guid("e2a7b4c9-1f5d-4e83-9a6c-7d0b3f8e5c12"));
            migrationBuilder.DeleteData(table: "SystemSettings", keyColumn: "Id", keyValue: new Guid("f3b8c5d0-2a6e-4f94-8b7d-8e1c4a9f6d23"));
            migrationBuilder.DeleteData(table: "SystemSettings", keyColumn: "Id", keyValue: new Guid("a4c9d6e1-3b7f-4a05-9c8e-9f2d5b0a7e34"));
            migrationBuilder.DeleteData(table: "SystemSettings", keyColumn: "Id", keyValue: new Guid("b5d0e7f2-4c8a-4b16-9d9f-0a3e6c1b8f45"));
        }
    }
}
