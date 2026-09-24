using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace SupermarketSystem.Infrastructure.Migrations
{
    /// <inheritdoc />
    public partial class AddPriceChangeCatalogVersionAndCashierClosing : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<long>(
                name: "AppliedAtCatalogVersion",
                table: "PriceChangeRequests",
                type: "bigint",
                nullable: true);

            // لو حدا ربط CashClosing.Manage بدور الكاشير يدويًا (SSMS)، الفهرس الفريد
            // (RoleId, PermissionId) كان رح يفشّل InsertData تحت.
            migrationBuilder.Sql(
                "DELETE FROM [RolePermissions] " +
                "WHERE [RoleId] = 'f3b401c7-84f6-4a0f-9f17-b689979c5d8c' " +
                "AND [PermissionId] = 'a7fc0954-e9d6-4c47-af8a-4620d9faf6f0';");

            migrationBuilder.InsertData(
                table: "RolePermissions",
                columns: new[] { "Id", "PermissionId", "RoleId" },
                values: new object[] { new Guid("7e1c4b2a-9d3f-4e6a-b8c5-2f0d1a3e5b79"), new Guid("a7fc0954-e9d6-4c47-af8a-4620d9faf6f0"), new Guid("f3b401c7-84f6-4a0f-9f17-b689979c5d8c") });

            migrationBuilder.CreateIndex(
                name: "IX_PriceChangeRequests_ProductBranchId_AppliedAtCatalogVersion",
                table: "PriceChangeRequests",
                columns: new[] { "ProductBranchId", "AppliedAtCatalogVersion" });
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropIndex(
                name: "IX_PriceChangeRequests_ProductBranchId_AppliedAtCatalogVersion",
                table: "PriceChangeRequests");

            migrationBuilder.DeleteData(
                table: "RolePermissions",
                keyColumn: "Id",
                keyValue: new Guid("7e1c4b2a-9d3f-4e6a-b8c5-2f0d1a3e5b79"));

            migrationBuilder.DropColumn(
                name: "AppliedAtCatalogVersion",
                table: "PriceChangeRequests");
        }
    }
}
