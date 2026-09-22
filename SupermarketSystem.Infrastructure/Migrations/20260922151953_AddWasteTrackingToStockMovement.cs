using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace SupermarketSystem.Infrastructure.Migrations
{
    /// <inheritdoc />
    public partial class AddWasteTrackingToStockMovement : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<int>(
                name: "WasteReason",
                table: "StockMovements",
                type: "int",
                nullable: true);

            migrationBuilder.InsertData(
                table: "Permissions",
                columns: new[] { "Id", "Code", "CreatedAtUtc", "CreatedByUserId", "Description", "Name", "UpdatedAtUtc", "UpdatedByUserId" },
                values: new object[] { new Guid("1a2b3c4d-5e6f-4a7b-8c9d-0e1f2a3b4c5d"), "Inventory.WasteIssue", new DateTime(2026, 9, 22, 0, 0, 0, 0, DateTimeKind.Utc), null, "Issue stock as waste/damage (expired, broken, storage damage...), with an explicit reason - separate from complimentary issues.", "Record waste/damage issues", null, null });

            migrationBuilder.InsertData(
                table: "RolePermissions",
                columns: new[] { "Id", "PermissionId", "RoleId" },
                values: new object[] { new Guid("2b3c4d5e-6f7a-4b8c-9d0e-1f2a3b4c5d6e"), new Guid("1a2b3c4d-5e6f-4a7b-8c9d-0e1f2a3b4c5d"), new Guid("50e6125a-cac0-4d82-a0b8-9f3c6fff59d7") });
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DeleteData(
                table: "RolePermissions",
                keyColumn: "Id",
                keyValue: new Guid("2b3c4d5e-6f7a-4b8c-9d0e-1f2a3b4c5d6e"));

            migrationBuilder.DeleteData(
                table: "Permissions",
                keyColumn: "Id",
                keyValue: new Guid("1a2b3c4d-5e6f-4a7b-8c9d-0e1f2a3b4c5d"));

            migrationBuilder.DropColumn(
                name: "WasteReason",
                table: "StockMovements");
        }
    }
}
