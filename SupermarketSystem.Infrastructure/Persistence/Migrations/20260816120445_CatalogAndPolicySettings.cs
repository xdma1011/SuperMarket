using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

#pragma warning disable CA1814 // Prefer jagged arrays over multidimensional

namespace SupermarketSystem.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class CatalogAndPolicySettings : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.InsertData(
                table: "SystemSettings",
                columns: new[] { "Id", "CreatedAtUtc", "CreatedByUserId", "Description", "Key", "UpdatedAtUtc", "UpdatedByUserId", "Value" },
                values: new object[,]
                {
                    { new Guid("032121de-b688-4ee9-ab20-e5a16af6fe4f"), new DateTime(2026, 1, 1, 0, 0, 0, 0, DateTimeKind.Utc), null, "Returns above this value are still completed immediately, but flagged for management review. 0 disables value-based flagging.", "Pos.HighValueReturnThreshold", null, null, "0" },
                    { new Guid("445269e2-e8f6-482b-95da-d8a829f9e14e"), new DateTime(2026, 1, 1, 0, 0, 0, 0, DateTimeKind.Utc), null, "Allow cashiers to process customer returns.", "Pos.AllowReturn", null, null, "true" },
                    { new Guid("64b0776a-6271-4c87-a1ab-bdfeb3b50361"), new DateTime(2026, 1, 1, 0, 0, 0, 0, DateTimeKind.Utc), null, "Maximum ad-hoc discount as a percentage of the line/invoice total. Set to 0 to disable manual discounts.", "Pos.MaxManualDiscountPercentage", null, null, "10" },
                    { new Guid("99133340-d81f-4202-bebe-294ed26f0c41"), new DateTime(2026, 1, 1, 0, 0, 0, 0, DateTimeKind.Utc), null, "Allow cashiers to void a completed sale. Voids never wait for approval; they are recorded and flagged for review.", "Pos.AllowVoidSale", null, null, "true" },
                    { new Guid("abd250d3-ef22-415c-b9fd-81a5850fc3d7"), new DateTime(2026, 1, 1, 0, 0, 0, 0, DateTimeKind.Utc), null, "Allow refunding to a payment method other than the original sale's. Off by default: a cash refund against a card sale removes cash that never entered the drawer.", "Pos.AllowCrossMethodRefund", null, null, "false" },
                    { new Guid("c536de35-d0ed-42ac-b551-b3851118015b"), new DateTime(2026, 1, 1, 0, 0, 0, 0, DateTimeKind.Utc), null, "Allow ad-hoc discounts keyed in at checkout.", "Pos.AllowManualDiscount", null, null, "true" },
                    { new Guid("e26ab4e9-5c07-4ad6-96d8-0732494eb625"), new DateTime(2026, 1, 1, 0, 0, 0, 0, DateTimeKind.Utc), null, "Allow reversing a completed payment. The original payment is preserved; a reversal record is added.", "Pos.AllowPaymentReversal", null, null, "true" }
                });
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DeleteData(
                table: "SystemSettings",
                keyColumn: "Id",
                keyValue: new Guid("032121de-b688-4ee9-ab20-e5a16af6fe4f"));

            migrationBuilder.DeleteData(
                table: "SystemSettings",
                keyColumn: "Id",
                keyValue: new Guid("445269e2-e8f6-482b-95da-d8a829f9e14e"));

            migrationBuilder.DeleteData(
                table: "SystemSettings",
                keyColumn: "Id",
                keyValue: new Guid("64b0776a-6271-4c87-a1ab-bdfeb3b50361"));

            migrationBuilder.DeleteData(
                table: "SystemSettings",
                keyColumn: "Id",
                keyValue: new Guid("99133340-d81f-4202-bebe-294ed26f0c41"));

            migrationBuilder.DeleteData(
                table: "SystemSettings",
                keyColumn: "Id",
                keyValue: new Guid("abd250d3-ef22-415c-b9fd-81a5850fc3d7"));

            migrationBuilder.DeleteData(
                table: "SystemSettings",
                keyColumn: "Id",
                keyValue: new Guid("c536de35-d0ed-42ac-b551-b3851118015b"));

            migrationBuilder.DeleteData(
                table: "SystemSettings",
                keyColumn: "Id",
                keyValue: new Guid("e26ab4e9-5c07-4ad6-96d8-0732494eb625"));
        }
    }
}
