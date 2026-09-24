using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace SupermarketSystem.Infrastructure.Migrations
{
    /// <inheritdoc />
    public partial class AddNotificationSeverity : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<int>(
                name: "Severity",
                table: "Notifications",
                type: "int",
                nullable: false,
                defaultValue: 2);

            migrationBuilder.UpdateData(
                table: "SystemSettings",
                keyColumn: "Id",
                keyValue: new Guid("5fddc559-554c-4f20-8923-bfb5c1bb7c6e"),
                column: "Description",
                value: "Cash-closing variance (absolute value) above which a notification is sent. 0 = any variance (deficit or surplus) alerts.");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "Severity",
                table: "Notifications");

            migrationBuilder.UpdateData(
                table: "SystemSettings",
                keyColumn: "Id",
                keyValue: new Guid("5fddc559-554c-4f20-8923-bfb5c1bb7c6e"),
                column: "Description",
                value: "Cash-closing variance (absolute value) above which a notification is sent. 0 disables the alert.");
        }
    }
}
