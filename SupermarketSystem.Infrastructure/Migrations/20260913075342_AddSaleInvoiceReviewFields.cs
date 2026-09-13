using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace SupermarketSystem.Infrastructure.Migrations
{
    /// <inheritdoc />
    public partial class AddSaleInvoiceReviewFields : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<DateTime>(
                name: "ReviewedAtUtc",
                table: "SaleInvoices",
                type: "datetime2",
                nullable: true);

            migrationBuilder.AddColumn<Guid>(
                name: "ReviewedByUserId",
                table: "SaleInvoices",
                type: "uniqueidentifier",
                nullable: true);

            migrationBuilder.CreateIndex(
                name: "IX_SaleInvoices_ReviewedByUserId",
                table: "SaleInvoices",
                column: "ReviewedByUserId");

            migrationBuilder.AddForeignKey(
                name: "FK_SaleInvoices_Users_ReviewedByUserId",
                table: "SaleInvoices",
                column: "ReviewedByUserId",
                principalTable: "Users",
                principalColumn: "Id",
                onDelete: ReferentialAction.Restrict);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropForeignKey(
                name: "FK_SaleInvoices_Users_ReviewedByUserId",
                table: "SaleInvoices");

            migrationBuilder.DropIndex(
                name: "IX_SaleInvoices_ReviewedByUserId",
                table: "SaleInvoices");

            migrationBuilder.DropColumn(
                name: "ReviewedAtUtc",
                table: "SaleInvoices");

            migrationBuilder.DropColumn(
                name: "ReviewedByUserId",
                table: "SaleInvoices");
        }
    }
}
