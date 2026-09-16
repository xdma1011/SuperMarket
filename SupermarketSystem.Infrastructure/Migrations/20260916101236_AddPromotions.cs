using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace SupermarketSystem.Infrastructure.Migrations
{
    /// <inheritdoc />
    public partial class AddPromotions : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<decimal>(
                name: "PromotionAmount",
                table: "SaleInvoiceItems",
                type: "decimal(18,4)",
                nullable: false,
                defaultValue: 0m);

            migrationBuilder.AddColumn<Guid>(
                name: "PromotionId",
                table: "SaleInvoiceItems",
                type: "uniqueidentifier",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "PromotionTitleSnapshot",
                table: "SaleInvoiceItems",
                type: "nvarchar(200)",
                maxLength: 200,
                nullable: true);

            migrationBuilder.CreateTable(
                name: "Promotions",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    ProductId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    Title = table.Column<string>(type: "nvarchar(200)", maxLength: 200, nullable: false),
                    BundleQuantity = table.Column<int>(type: "int", nullable: false),
                    BundlePrice = table.Column<decimal>(type: "decimal(18,4)", nullable: false),
                    MaxQuantityPerInvoice = table.Column<decimal>(type: "decimal(18,4)", nullable: true),
                    StartAtUtc = table.Column<DateTime>(type: "datetime2", nullable: false),
                    EndAtUtc = table.Column<DateTime>(type: "datetime2", nullable: false),
                    RowVersion = table.Column<byte[]>(type: "rowversion", rowVersion: true, nullable: true),
                    CreatedAtUtc = table.Column<DateTime>(type: "datetime2", nullable: false),
                    CreatedByUserId = table.Column<Guid>(type: "uniqueidentifier", nullable: true),
                    UpdatedAtUtc = table.Column<DateTime>(type: "datetime2", nullable: true),
                    UpdatedByUserId = table.Column<Guid>(type: "uniqueidentifier", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_Promotions", x => x.Id);
                    table.ForeignKey(
                        name: "FK_Promotions_Products_ProductId",
                        column: x => x.ProductId,
                        principalTable: "Products",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "PromotionBranches",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    PromotionId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    BranchId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    IsActive = table.Column<bool>(type: "bit", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_PromotionBranches", x => x.Id);
                    table.ForeignKey(
                        name: "FK_PromotionBranches_Branches_BranchId",
                        column: x => x.BranchId,
                        principalTable: "Branches",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_PromotionBranches_Promotions_PromotionId",
                        column: x => x.PromotionId,
                        principalTable: "Promotions",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateIndex(
                name: "IX_SaleInvoiceItems_PromotionId",
                table: "SaleInvoiceItems",
                column: "PromotionId");

            migrationBuilder.CreateIndex(
                name: "IX_PromotionBranches_BranchId",
                table: "PromotionBranches",
                column: "BranchId");

            migrationBuilder.CreateIndex(
                name: "IX_PromotionBranches_PromotionId_BranchId",
                table: "PromotionBranches",
                columns: new[] { "PromotionId", "BranchId" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_Promotions_ProductId_StartAtUtc_EndAtUtc",
                table: "Promotions",
                columns: new[] { "ProductId", "StartAtUtc", "EndAtUtc" });

            migrationBuilder.AddForeignKey(
                name: "FK_SaleInvoiceItems_Promotions_PromotionId",
                table: "SaleInvoiceItems",
                column: "PromotionId",
                principalTable: "Promotions",
                principalColumn: "Id",
                onDelete: ReferentialAction.SetNull);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropForeignKey(
                name: "FK_SaleInvoiceItems_Promotions_PromotionId",
                table: "SaleInvoiceItems");

            migrationBuilder.DropTable(
                name: "PromotionBranches");

            migrationBuilder.DropTable(
                name: "Promotions");

            migrationBuilder.DropIndex(
                name: "IX_SaleInvoiceItems_PromotionId",
                table: "SaleInvoiceItems");

            migrationBuilder.DropColumn(
                name: "PromotionAmount",
                table: "SaleInvoiceItems");

            migrationBuilder.DropColumn(
                name: "PromotionId",
                table: "SaleInvoiceItems");

            migrationBuilder.DropColumn(
                name: "PromotionTitleSnapshot",
                table: "SaleInvoiceItems");
        }
    }
}
