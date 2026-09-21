using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace SupermarketSystem.Infrastructure.Migrations
{
    /// <inheritdoc />
    public partial class AddCashClosingShiftNumber : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropIndex(
                name: "IX_CashClosings_BranchId_BusinessDate",
                table: "CashClosings");

            migrationBuilder.AddColumn<int>(
                name: "ShiftNumber",
                table: "CashClosings",
                type: "int",
                nullable: false,
                defaultValue: 1);

            migrationBuilder.CreateIndex(
                name: "IX_CashClosings_BranchId_BusinessDate_ShiftNumber",
                table: "CashClosings",
                columns: new[] { "BranchId", "BusinessDate", "ShiftNumber" },
                unique: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropIndex(
                name: "IX_CashClosings_BranchId_BusinessDate_ShiftNumber",
                table: "CashClosings");

            migrationBuilder.DropColumn(
                name: "ShiftNumber",
                table: "CashClosings");

            migrationBuilder.CreateIndex(
                name: "IX_CashClosings_BranchId_BusinessDate",
                table: "CashClosings",
                columns: new[] { "BranchId", "BusinessDate" },
                unique: true);
        }
    }
}
