using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace SupermarketSystem.Infrastructure.Migrations
{
    /// <inheritdoc />
    public partial class AddDrawerOpenOfflineFields : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<Guid>(
                name: "ClientRequestId",
                table: "DrawerOpenEvents",
                type: "uniqueidentifier",
                nullable: true);

            migrationBuilder.AddColumn<DateTime>(
                name: "RecordedAtUtc",
                table: "DrawerOpenEvents",
                type: "datetime2",
                nullable: true);

            migrationBuilder.CreateIndex(
                name: "IX_DrawerOpenEvents_ClientRequestId",
                table: "DrawerOpenEvents",
                column: "ClientRequestId",
                unique: true,
                filter: "[ClientRequestId] IS NOT NULL");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropIndex(
                name: "IX_DrawerOpenEvents_ClientRequestId",
                table: "DrawerOpenEvents");

            migrationBuilder.DropColumn(
                name: "ClientRequestId",
                table: "DrawerOpenEvents");

            migrationBuilder.DropColumn(
                name: "RecordedAtUtc",
                table: "DrawerOpenEvents");
        }
    }
}
