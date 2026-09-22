using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace SupermarketSystem.Infrastructure.Migrations
{
    /// <inheritdoc />
    public partial class AddPurchaseInvoiceDueDate : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<DateOnly>(
                name: "DueDate",
                table: "PurchaseInvoices",
                type: "date",
                nullable: true);

            migrationBuilder.CreateIndex(
                name: "IX_PurchaseInvoices_DueDate",
                table: "PurchaseInvoices",
                column: "DueDate");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropIndex(
                name: "IX_PurchaseInvoices_DueDate",
                table: "PurchaseInvoices");

            migrationBuilder.DropColumn(
                name: "DueDate",
                table: "PurchaseInvoices");
        }
    }
}
