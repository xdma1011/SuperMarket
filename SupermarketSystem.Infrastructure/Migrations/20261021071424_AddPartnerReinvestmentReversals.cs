using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace SupermarketSystem.Infrastructure.Migrations
{
    /// <inheritdoc />
    public partial class AddPartnerReinvestmentReversals : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "PartnerReinvestmentReversals",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    BranchId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    PartnerId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    ReinvestmentId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    Amount = table.Column<decimal>(type: "decimal(18,4)", nullable: false),
                    OccurredAtUtc = table.Column<DateTime>(type: "datetime2", nullable: false),
                    Reason = table.Column<string>(type: "nvarchar(500)", maxLength: 500, nullable: true),
                    RecordedByUserId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    CapitalTransactionId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    ClientRequestId = table.Column<Guid>(type: "uniqueidentifier", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_PartnerReinvestmentReversals", x => x.Id);
                    table.ForeignKey(
                        name: "FK_PartnerReinvestmentReversals_Branches_BranchId",
                        column: x => x.BranchId,
                        principalTable: "Branches",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_PartnerReinvestmentReversals_CapitalTransactions_CapitalTransactionId",
                        column: x => x.CapitalTransactionId,
                        principalTable: "CapitalTransactions",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_PartnerReinvestmentReversals_PartnerReinvestments_ReinvestmentId",
                        column: x => x.ReinvestmentId,
                        principalTable: "PartnerReinvestments",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_PartnerReinvestmentReversals_Partners_PartnerId",
                        column: x => x.PartnerId,
                        principalTable: "Partners",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_PartnerReinvestmentReversals_Users_RecordedByUserId",
                        column: x => x.RecordedByUserId,
                        principalTable: "Users",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateIndex(
                name: "IX_PartnerReinvestmentReversals_BranchId",
                table: "PartnerReinvestmentReversals",
                column: "BranchId");

            migrationBuilder.CreateIndex(
                name: "IX_PartnerReinvestmentReversals_CapitalTransactionId",
                table: "PartnerReinvestmentReversals",
                column: "CapitalTransactionId",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_PartnerReinvestmentReversals_ClientRequestId",
                table: "PartnerReinvestmentReversals",
                column: "ClientRequestId",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_PartnerReinvestmentReversals_PartnerId_OccurredAtUtc",
                table: "PartnerReinvestmentReversals",
                columns: new[] { "PartnerId", "OccurredAtUtc" });

            migrationBuilder.CreateIndex(
                name: "IX_PartnerReinvestmentReversals_RecordedByUserId",
                table: "PartnerReinvestmentReversals",
                column: "RecordedByUserId");

            migrationBuilder.CreateIndex(
                name: "IX_PartnerReinvestmentReversals_ReinvestmentId",
                table: "PartnerReinvestmentReversals",
                column: "ReinvestmentId",
                unique: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "PartnerReinvestmentReversals");
        }
    }
}
