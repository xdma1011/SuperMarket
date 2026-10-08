using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace SupermarketSystem.Infrastructure.Migrations
{
    /// <inheritdoc />
    public partial class AddPartnerStatementStaleFlag : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<string>(
                name: "StaleReason",
                table: "PartnerMonthlyStatements",
                type: "nvarchar(300)",
                maxLength: 300,
                nullable: true);

            migrationBuilder.AddColumn<DateTime>(
                name: "StaleSinceUtc",
                table: "PartnerMonthlyStatements",
                type: "datetime2",
                nullable: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "StaleReason",
                table: "PartnerMonthlyStatements");

            migrationBuilder.DropColumn(
                name: "StaleSinceUtc",
                table: "PartnerMonthlyStatements");
        }
    }
}
