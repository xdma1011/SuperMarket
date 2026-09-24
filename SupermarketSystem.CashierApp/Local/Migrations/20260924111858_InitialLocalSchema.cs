using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace SupermarketSystem.CashierApp.Local.Migrations
{
    /// <inheritdoc />
    public partial class InitialLocalSchema : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "PaymentMethods",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "TEXT", nullable: false),
                    Name = table.Column<string>(type: "TEXT", nullable: false),
                    RequiresExternalReference = table.Column<bool>(type: "INTEGER", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_PaymentMethods", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "PendingSales",
                columns: table => new
                {
                    Id = table.Column<int>(type: "INTEGER", nullable: false)
                        .Annotation("Sqlite:Autoincrement", true),
                    ClientRequestId = table.Column<Guid>(type: "TEXT", nullable: false),
                    BranchId = table.Column<Guid>(type: "TEXT", nullable: false),
                    RequestPayloadJson = table.Column<string>(type: "TEXT", nullable: false),
                    CreatedAtLocal = table.Column<DateTime>(type: "TEXT", nullable: false),
                    AttemptCount = table.Column<int>(type: "INTEGER", nullable: false),
                    LastAttemptAtLocal = table.Column<DateTime>(type: "TEXT", nullable: true),
                    LastErrorMessage = table.Column<string>(type: "TEXT", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_PendingSales", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "ProductBarcodes",
                columns: table => new
                {
                    Id = table.Column<int>(type: "INTEGER", nullable: false)
                        .Annotation("Sqlite:Autoincrement", true),
                    BarcodeValue = table.Column<string>(type: "TEXT", nullable: false),
                    ProductUnitId = table.Column<Guid>(type: "TEXT", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_ProductBarcodes", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "ProductBatches",
                columns: table => new
                {
                    BatchId = table.Column<Guid>(type: "TEXT", nullable: false),
                    ProductId = table.Column<Guid>(type: "TEXT", nullable: false),
                    BatchNumber = table.Column<string>(type: "TEXT", nullable: false),
                    ExpiryDate = table.Column<DateOnly>(type: "TEXT", nullable: true),
                    QuantityAvailable = table.Column<decimal>(type: "TEXT", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_ProductBatches", x => x.BatchId);
                });

            migrationBuilder.CreateTable(
                name: "Products",
                columns: table => new
                {
                    ProductId = table.Column<Guid>(type: "TEXT", nullable: false),
                    Name = table.Column<string>(type: "TEXT", nullable: false),
                    CategoryId = table.Column<Guid>(type: "TEXT", nullable: false),
                    CategoryName = table.Column<string>(type: "TEXT", nullable: false),
                    SellingPrice = table.Column<decimal>(type: "TEXT", nullable: false),
                    IsAvailableForSale = table.Column<bool>(type: "INTEGER", nullable: false),
                    IsBatchTracked = table.Column<bool>(type: "INTEGER", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_Products", x => x.ProductId);
                });

            migrationBuilder.CreateTable(
                name: "ProductUnits",
                columns: table => new
                {
                    UnitId = table.Column<Guid>(type: "TEXT", nullable: false),
                    ProductId = table.Column<Guid>(type: "TEXT", nullable: false),
                    UnitName = table.Column<string>(type: "TEXT", nullable: false),
                    ConversionFactorToBase = table.Column<decimal>(type: "TEXT", nullable: false),
                    IsBaseUnit = table.Column<bool>(type: "INTEGER", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_ProductUnits", x => x.UnitId);
                });

            migrationBuilder.CreateTable(
                name: "SyncStates",
                columns: table => new
                {
                    Id = table.Column<int>(type: "INTEGER", nullable: false)
                        .Annotation("Sqlite:Autoincrement", true),
                    LastSyncedCatalogVersion = table.Column<long>(type: "INTEGER", nullable: false),
                    LastSuccessfulSyncAtLocal = table.Column<DateTime>(type: "TEXT", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_SyncStates", x => x.Id);
                });

            migrationBuilder.CreateIndex(
                name: "IX_PendingSales_ClientRequestId",
                table: "PendingSales",
                column: "ClientRequestId",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_ProductBarcodes_BarcodeValue",
                table: "ProductBarcodes",
                column: "BarcodeValue",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_ProductBatches_ProductId",
                table: "ProductBatches",
                column: "ProductId");

            migrationBuilder.CreateIndex(
                name: "IX_ProductUnits_ProductId",
                table: "ProductUnits",
                column: "ProductId");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "PaymentMethods");

            migrationBuilder.DropTable(
                name: "PendingSales");

            migrationBuilder.DropTable(
                name: "ProductBarcodes");

            migrationBuilder.DropTable(
                name: "ProductBatches");

            migrationBuilder.DropTable(
                name: "Products");

            migrationBuilder.DropTable(
                name: "ProductUnits");

            migrationBuilder.DropTable(
                name: "SyncStates");
        }
    }
}
