using System;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace SupermarketSystem.CashierApp.Local.Migrations
{
    /// <summary>
    /// الساعة الموثوقة (بند 22، 8/10/2026) - جدول جديد بصف واحد. إضافي بالكامل: ما بيلمس أي جدول موجود ولا بيمسح بيانات.
    /// </summary>
    [DbContext(typeof(LocalDbContext))]
    [Migration("20261008170000_AddTrustedTimeAnchor")]
    public partial class AddTrustedTimeAnchor : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "TrustedTimeAnchors",
                columns: table => new
                {
                    Id = table.Column<int>(type: "INTEGER", nullable: false)
                        .Annotation("Sqlite:Autoincrement", true),
                    ServerUtc = table.Column<DateTime>(type: "TEXT", nullable: false),
                    DeviceUtcAtSync = table.Column<DateTime>(type: "TEXT", nullable: false),
                    HighWaterUtc = table.Column<DateTime>(type: "TEXT", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_TrustedTimeAnchors", x => x.Id);
                });
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "TrustedTimeAnchors");
        }
    }
}
