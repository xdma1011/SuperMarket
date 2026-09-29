using System;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace SupermarketSystem.CashierApp.Local.Migrations
{
    /// <summary>
    /// عروض الكمية محليًا (29/9/2026) - جدول جديد، وعمود وقت آخر تحديث لطرق الدفع. وبعدين رقم نسخة الكتالوج المحلي بيصير -1، عشان أول مزامنة
    /// بعد التحديث تسحب الكتالوج كامل ومعه العروض (بلا هيك الجدول بيضل فاضي لحد أول تغيير بالكتالوج).
    /// </summary>
    [DbContext(typeof(LocalDbContext))]
    [Migration("20260929120000_AddLocalPromotions")]
    public partial class AddLocalPromotions : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "Promotions",
                columns: table => new
                {
                    PromotionId = table.Column<Guid>(type: "TEXT", nullable: false),
                    ProductId = table.Column<Guid>(type: "TEXT", nullable: false),
                    Title = table.Column<string>(type: "TEXT", nullable: false),
                    BundleQuantity = table.Column<int>(type: "INTEGER", nullable: false),
                    BundlePrice = table.Column<decimal>(type: "TEXT", nullable: false),
                    MaxQuantityPerInvoice = table.Column<decimal>(type: "TEXT", nullable: true),
                    StartAtUtc = table.Column<DateTime>(type: "TEXT", nullable: false),
                    EndAtUtc = table.Column<DateTime>(type: "TEXT", nullable: false),
                    CreatedAtUtc = table.Column<DateTime>(type: "TEXT", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_Promotions", x => x.PromotionId);
                });

            migrationBuilder.CreateIndex(
                name: "IX_Promotions_ProductId",
                table: "Promotions",
                column: "ProductId");

            // طرق الدفع بتتحدّث مرة كل 24 ساعة (PaymentMethodStore) - null = أول دورة بتحدّثها.
            migrationBuilder.AddColumn<DateTime>(
                name: "LastPaymentMethodsRefreshAtUtc",
                table: "SyncStates",
                type: "TEXT",
                nullable: true);

            // -1 مش 0: سيرفر جديد رقم نسخته 0، فـ0 محليًا كان رح يبين "محدَّث" وما يسحب إشي.
            migrationBuilder.Sql("UPDATE SyncStates SET LastSyncedCatalogVersion = -1;");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "Promotions");

            migrationBuilder.DropColumn(
                name: "LastPaymentMethodsRefreshAtUtc",
                table: "SyncStates");
        }
    }
}
