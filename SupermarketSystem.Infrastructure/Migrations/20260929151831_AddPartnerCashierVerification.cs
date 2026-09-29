using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

#pragma warning disable CA1814 // Prefer jagged arrays over multidimensional

namespace SupermarketSystem.Infrastructure.Migrations
{
    /// <inheritdoc />
    public partial class AddPartnerCashierVerification : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<string>(
                name: "CashierBarcodeHash",
                table: "Partners",
                type: "nvarchar(100)",
                maxLength: 100,
                nullable: true);

            migrationBuilder.AddColumn<DateTime>(
                name: "CashierBarcodeIssuedAtUtc",
                table: "Partners",
                type: "datetime2",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "TelegramPhone",
                table: "Partners",
                type: "nvarchar(20)",
                maxLength: 20,
                nullable: true);

            migrationBuilder.CreateTable(
                name: "PartnerOtpChallenges",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    BranchId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    PartnerId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    CodeHash = table.Column<string>(type: "nvarchar(100)", maxLength: 100, nullable: false),
                    Amount = table.Column<decimal>(type: "decimal(18,4)", nullable: false),
                    CreatedAtUtc = table.Column<DateTime>(type: "datetime2", nullable: false),
                    ExpiresAtUtc = table.Column<DateTime>(type: "datetime2", nullable: false),
                    FailedAttempts = table.Column<int>(type: "int", nullable: false),
                    ConsumedAtUtc = table.Column<DateTime>(type: "datetime2", nullable: true),
                    ConsumedByClientRequestId = table.Column<Guid>(type: "uniqueidentifier", nullable: true),
                    RequestedByUserId = table.Column<Guid>(type: "uniqueidentifier", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_PartnerOtpChallenges", x => x.Id);
                    table.ForeignKey(
                        name: "FK_PartnerOtpChallenges_Branches_BranchId",
                        column: x => x.BranchId,
                        principalTable: "Branches",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_PartnerOtpChallenges_Partners_PartnerId",
                        column: x => x.PartnerId,
                        principalTable: "Partners",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_PartnerOtpChallenges_Users_RequestedByUserId",
                        column: x => x.RequestedByUserId,
                        principalTable: "Users",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.InsertData(
                table: "SystemSettings",
                columns: new[] { "Id", "CreatedAtUtc", "CreatedByUserId", "Description", "Key", "UpdatedAtUtc", "UpdatedByUserId", "Value" },
                values: new object[,]
                {
                    { new Guid("0f6c2b8e-7d41-4c35-9a2e-5b8d1f3c6a90"), new DateTime(2026, 1, 1, 0, 0, 0, 0, DateTimeKind.Utc), null, "Allow a partner to verify a cashier withdrawal with a one-time code sent to their Telegram.", "Partners.CashierVerify.TelegramOtpEnabled", null, null, "false" },
                    { new Guid("1a7d3c9f-8e52-4d46-8b3f-6c9e2a4d7b01"), new DateTime(2026, 1, 1, 0, 0, 0, 0, DateTimeKind.Utc), null, "Allow a partner to verify a cashier withdrawal by scanning their personal barcode card.", "Partners.CashierVerify.BarcodeEnabled", null, null, "false" }
                });

            migrationBuilder.CreateIndex(
                name: "IX_PartnerOtpChallenges_BranchId",
                table: "PartnerOtpChallenges",
                column: "BranchId");

            migrationBuilder.CreateIndex(
                name: "IX_PartnerOtpChallenges_PartnerId_CreatedAtUtc",
                table: "PartnerOtpChallenges",
                columns: new[] { "PartnerId", "CreatedAtUtc" });

            migrationBuilder.CreateIndex(
                name: "IX_PartnerOtpChallenges_RequestedByUserId",
                table: "PartnerOtpChallenges",
                column: "RequestedByUserId");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "PartnerOtpChallenges");

            migrationBuilder.DeleteData(
                table: "SystemSettings",
                keyColumn: "Id",
                keyValue: new Guid("0f6c2b8e-7d41-4c35-9a2e-5b8d1f3c6a90"));

            migrationBuilder.DeleteData(
                table: "SystemSettings",
                keyColumn: "Id",
                keyValue: new Guid("1a7d3c9f-8e52-4d46-8b3f-6c9e2a4d7b01"));

            migrationBuilder.DropColumn(
                name: "CashierBarcodeHash",
                table: "Partners");

            migrationBuilder.DropColumn(
                name: "CashierBarcodeIssuedAtUtc",
                table: "Partners");

            migrationBuilder.DropColumn(
                name: "TelegramPhone",
                table: "Partners");
        }
    }
}
