using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace SupermarketSystem.Infrastructure.Migrations
{
    /// <inheritdoc />
    public partial class AddCashClosingVarianceNotesAndPendingSales : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<decimal>(
                name: "PendingSalesAmount",
                table: "CashClosings",
                type: "decimal(18,4)",
                nullable: false,
                defaultValue: 0m);

            migrationBuilder.AddColumn<int>(
                name: "PendingSalesCount",
                table: "CashClosings",
                type: "int",
                nullable: false,
                defaultValue: 0);

            migrationBuilder.CreateTable(
                name: "CashClosingVarianceNotes",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    CashClosingId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    BranchId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    Reason = table.Column<int>(type: "int", nullable: false),
                    ExplainedAmount = table.Column<decimal>(type: "decimal(18,4)", nullable: false),
                    Note = table.Column<string>(type: "nvarchar(500)", maxLength: 500, nullable: true),
                    RelatedExpenseId = table.Column<Guid>(type: "uniqueidentifier", nullable: true),
                    RecordedByUserId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    RecordedAtUtc = table.Column<DateTime>(type: "datetime2", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_CashClosingVarianceNotes", x => x.Id);
                    table.ForeignKey(
                        name: "FK_CashClosingVarianceNotes_Branches_BranchId",
                        column: x => x.BranchId,
                        principalTable: "Branches",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_CashClosingVarianceNotes_CashClosings_CashClosingId",
                        column: x => x.CashClosingId,
                        principalTable: "CashClosings",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_CashClosingVarianceNotes_Users_RecordedByUserId",
                        column: x => x.RecordedByUserId,
                        principalTable: "Users",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateIndex(
                name: "IX_CashClosingVarianceNotes_BranchId",
                table: "CashClosingVarianceNotes",
                column: "BranchId");

            migrationBuilder.CreateIndex(
                name: "IX_CashClosingVarianceNotes_CashClosingId",
                table: "CashClosingVarianceNotes",
                column: "CashClosingId");

            migrationBuilder.CreateIndex(
                name: "IX_CashClosingVarianceNotes_RecordedByUserId",
                table: "CashClosingVarianceNotes",
                column: "RecordedByUserId");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "CashClosingVarianceNotes");

            migrationBuilder.DropColumn(
                name: "PendingSalesAmount",
                table: "CashClosings");

            migrationBuilder.DropColumn(
                name: "PendingSalesCount",
                table: "CashClosings");
        }
    }
}
