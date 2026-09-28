using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

#pragma warning disable CA1814 // Prefer jagged arrays over multidimensional

namespace SupermarketSystem.Infrastructure.Migrations
{
    /// <inheritdoc />
    public partial class AddNotificationsManagePermission : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.InsertData(
                table: "Permissions",
                columns: new[] { "Id", "Code", "CreatedAtUtc", "CreatedByUserId", "Description", "Name", "UpdatedAtUtc", "UpdatedByUserId" },
                values: new object[] { new Guid("b2e5d3f6-9c7a-4e4b-8d1f-6a9c2e5b7d91"), "Notifications.Manage", new DateTime(2026, 9, 28, 0, 0, 0, 0, DateTimeKind.Utc), null, "Mark alerts as read (read state is shared by all admins) - separate from Notifications.View so a cashier can't hide an alert about their own drawer.", "Mark alerts as read", null, null });

            migrationBuilder.InsertData(
                table: "RolePermissions",
                columns: new[] { "Id", "PermissionId", "RoleId" },
                values: new object[,]
                {
                    { new Guid("c3f6e4a7-0d8b-4f5c-9e2a-7b0d3f6c8ea2"), new Guid("b2e5d3f6-9c7a-4e4b-8d1f-6a9c2e5b7d91"), new Guid("50e6125a-cac0-4d82-a0b8-9f3c6fff59d7") },
                    { new Guid("d4a7f5b8-1e9c-4a6d-8f3b-8c1e4a7d9fb3"), new Guid("b2e5d3f6-9c7a-4e4b-8d1f-6a9c2e5b7d91"), new Guid("5d0b3578-417e-4706-ab9b-fc9a208b6642") }
                });
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DeleteData(
                table: "RolePermissions",
                keyColumn: "Id",
                keyValue: new Guid("c3f6e4a7-0d8b-4f5c-9e2a-7b0d3f6c8ea2"));

            migrationBuilder.DeleteData(
                table: "RolePermissions",
                keyColumn: "Id",
                keyValue: new Guid("d4a7f5b8-1e9c-4a6d-8f3b-8c1e4a7d9fb3"));

            migrationBuilder.DeleteData(
                table: "Permissions",
                keyColumn: "Id",
                keyValue: new Guid("b2e5d3f6-9c7a-4e4b-8d1f-6a9c2e5b7d91"));
        }
    }
}
