using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace SupermarketSystem.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class AddPendingReviewEscalationSetting : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.InsertData(
                table: "SystemSettings",
                columns: new[] { "Id", "CreatedAtUtc", "CreatedByUserId", "Description", "Key", "UpdatedAtUtc", "UpdatedByUserId", "Value" },
                values: new object[,]
                {
                    { new Guid("d6e1f8a3-5c9b-4d27-8e4a-1b6c9d2e5f78"), new DateTime(2026, 1, 1, 0, 0, 0, 0, DateTimeKind.Utc), null, "Days a pending review item (unreviewed return, or NeedsReview stock movement) can stay unreviewed before PendingReviewEscalationBackgroundService flags it in an escalation notification.", "PendingReview.EscalationThresholdDays", null, null, "3" }
                });
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DeleteData(
                table: "SystemSettings",
                keyColumn: "Id",
                keyValue: new Guid("d6e1f8a3-5c9b-4d27-8e4a-1b6c9d2e5f78"));
        }
    }
}
