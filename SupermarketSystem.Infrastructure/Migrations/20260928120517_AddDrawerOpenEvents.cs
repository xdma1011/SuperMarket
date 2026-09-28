using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace SupermarketSystem.Infrastructure.Migrations
{
    /// <inheritdoc />
    public partial class AddDrawerOpenEvents : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "DrawerOpenEvents",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    BranchId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    UserId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    OccurredAtUtc = table.Column<DateTime>(type: "datetime2", nullable: false),
                    Reason = table.Column<string>(type: "nvarchar(200)", maxLength: 200, nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_DrawerOpenEvents", x => x.Id);
                    table.ForeignKey(
                        name: "FK_DrawerOpenEvents_Branches_BranchId",
                        column: x => x.BranchId,
                        principalTable: "Branches",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_DrawerOpenEvents_Users_UserId",
                        column: x => x.UserId,
                        principalTable: "Users",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateIndex(
                name: "IX_DrawerOpenEvents_BranchId_OccurredAtUtc",
                table: "DrawerOpenEvents",
                columns: new[] { "BranchId", "OccurredAtUtc" });

            migrationBuilder.CreateIndex(
                name: "IX_DrawerOpenEvents_UserId",
                table: "DrawerOpenEvents",
                column: "UserId");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "DrawerOpenEvents");
        }
    }
}
