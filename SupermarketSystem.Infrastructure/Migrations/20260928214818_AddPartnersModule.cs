using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace SupermarketSystem.Infrastructure.Migrations
{
    /// <inheritdoc />
    public partial class AddPartnersModule : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<Guid>(
                name: "PartnerId",
                table: "CapitalTransactions",
                type: "uniqueidentifier",
                nullable: true);

            migrationBuilder.CreateTable(
                name: "PartnerMonthlyStatements",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    BranchId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    Year = table.Column<int>(type: "int", nullable: false),
                    Month = table.Column<int>(type: "int", nullable: false),
                    NetProfit = table.Column<decimal>(type: "decimal(18,4)", nullable: false),
                    UnallocatedAmount = table.Column<decimal>(type: "decimal(18,4)", nullable: false),
                    GeneratedAtUtc = table.Column<DateTime>(type: "datetime2", nullable: false),
                    IsAutomatic = table.Column<bool>(type: "bit", nullable: false),
                    CreatedAtUtc = table.Column<DateTime>(type: "datetime2", nullable: false),
                    CreatedByUserId = table.Column<Guid>(type: "uniqueidentifier", nullable: true),
                    UpdatedAtUtc = table.Column<DateTime>(type: "datetime2", nullable: true),
                    UpdatedByUserId = table.Column<Guid>(type: "uniqueidentifier", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_PartnerMonthlyStatements", x => x.Id);
                    table.ForeignKey(
                        name: "FK_PartnerMonthlyStatements_Branches_BranchId",
                        column: x => x.BranchId,
                        principalTable: "Branches",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "Partners",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    BranchId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    FullName = table.Column<string>(type: "nvarchar(200)", maxLength: 200, nullable: false),
                    Type = table.Column<int>(type: "int", nullable: false),
                    UserId = table.Column<Guid>(type: "uniqueidentifier", nullable: true),
                    SpeculativeProfitPercent = table.Column<decimal>(type: "decimal(9,4)", nullable: true),
                    IsActive = table.Column<bool>(type: "bit", nullable: false),
                    Notes = table.Column<string>(type: "nvarchar(500)", maxLength: 500, nullable: true),
                    CreatedAtUtc = table.Column<DateTime>(type: "datetime2", nullable: false),
                    CreatedByUserId = table.Column<Guid>(type: "uniqueidentifier", nullable: true),
                    UpdatedAtUtc = table.Column<DateTime>(type: "datetime2", nullable: true),
                    UpdatedByUserId = table.Column<Guid>(type: "uniqueidentifier", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_Partners", x => x.Id);
                    table.ForeignKey(
                        name: "FK_Partners_Branches_BranchId",
                        column: x => x.BranchId,
                        principalTable: "Branches",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_Partners_Users_UserId",
                        column: x => x.UserId,
                        principalTable: "Users",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "PartnerStatementLines",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    StatementId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    PartnerId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    PartnerType = table.Column<int>(type: "int", nullable: false),
                    CapitalBalance = table.Column<decimal>(type: "decimal(18,4)", nullable: true),
                    SharePercent = table.Column<decimal>(type: "decimal(9,4)", nullable: false),
                    ShareAmount = table.Column<decimal>(type: "decimal(18,4)", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_PartnerStatementLines", x => x.Id);
                    table.ForeignKey(
                        name: "FK_PartnerStatementLines_PartnerMonthlyStatements_StatementId",
                        column: x => x.StatementId,
                        principalTable: "PartnerMonthlyStatements",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                    table.ForeignKey(
                        name: "FK_PartnerStatementLines_Partners_PartnerId",
                        column: x => x.PartnerId,
                        principalTable: "Partners",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "PartnerWithdrawals",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    BranchId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    PartnerId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    Amount = table.Column<decimal>(type: "decimal(18,4)", nullable: false),
                    Source = table.Column<int>(type: "int", nullable: false),
                    OccurredAtUtc = table.Column<DateTime>(type: "datetime2", nullable: false),
                    Notes = table.Column<string>(type: "nvarchar(500)", maxLength: 500, nullable: true),
                    RecordedByUserId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    PaidByUserId = table.Column<Guid>(type: "uniqueidentifier", nullable: true),
                    RecordedAtCashier = table.Column<bool>(type: "bit", nullable: false),
                    ClientRequestId = table.Column<Guid>(type: "uniqueidentifier", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_PartnerWithdrawals", x => x.Id);
                    table.ForeignKey(
                        name: "FK_PartnerWithdrawals_Branches_BranchId",
                        column: x => x.BranchId,
                        principalTable: "Branches",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_PartnerWithdrawals_Partners_PartnerId",
                        column: x => x.PartnerId,
                        principalTable: "Partners",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_PartnerWithdrawals_Users_PaidByUserId",
                        column: x => x.PaidByUserId,
                        principalTable: "Users",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_PartnerWithdrawals_Users_RecordedByUserId",
                        column: x => x.RecordedByUserId,
                        principalTable: "Users",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "OwnerReceivableEntries",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    BranchId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    OwnerUserId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    Type = table.Column<int>(type: "int", nullable: false),
                    Amount = table.Column<decimal>(type: "decimal(18,4)", nullable: false),
                    OccurredAtUtc = table.Column<DateTime>(type: "datetime2", nullable: false),
                    PartnerWithdrawalId = table.Column<Guid>(type: "uniqueidentifier", nullable: true),
                    RepaymentSource = table.Column<int>(type: "int", nullable: true),
                    Notes = table.Column<string>(type: "nvarchar(500)", maxLength: 500, nullable: true),
                    RecordedByUserId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    ClientRequestId = table.Column<Guid>(type: "uniqueidentifier", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_OwnerReceivableEntries", x => x.Id);
                    table.ForeignKey(
                        name: "FK_OwnerReceivableEntries_Branches_BranchId",
                        column: x => x.BranchId,
                        principalTable: "Branches",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_OwnerReceivableEntries_PartnerWithdrawals_PartnerWithdrawalId",
                        column: x => x.PartnerWithdrawalId,
                        principalTable: "PartnerWithdrawals",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_OwnerReceivableEntries_Users_OwnerUserId",
                        column: x => x.OwnerUserId,
                        principalTable: "Users",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_OwnerReceivableEntries_Users_RecordedByUserId",
                        column: x => x.RecordedByUserId,
                        principalTable: "Users",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.InsertData(
                table: "Permissions",
                columns: new[] { "Id", "Code", "CreatedAtUtc", "CreatedByUserId", "Description", "Name", "UpdatedAtUtc", "UpdatedByUserId" },
                values: new object[] { new Guid("9d3e1b4c-7a5f-4c2d-8b6e-4f7a0c3d5e79"), "Partners.Manage", new DateTime(2026, 9, 28, 0, 0, 0, 0, DateTimeKind.Utc), null, "Partners per branch, monthly profit statements, partner withdrawals (drawer or owner's pocket) and the owner receivable.", "Manage partners and profit distribution", null, null });

            migrationBuilder.InsertData(
                table: "RolePermissions",
                columns: new[] { "Id", "PermissionId", "RoleId" },
                values: new object[] { new Guid("a1f4c2d5-8b6e-4d3f-9c7a-5e8b1d4f6a80"), new Guid("9d3e1b4c-7a5f-4c2d-8b6e-4f7a0c3d5e79"), new Guid("50e6125a-cac0-4d82-a0b8-9f3c6fff59d7") });

            migrationBuilder.CreateIndex(
                name: "IX_CapitalTransactions_PartnerId",
                table: "CapitalTransactions",
                column: "PartnerId");

            migrationBuilder.CreateIndex(
                name: "IX_OwnerReceivableEntries_BranchId_OwnerUserId",
                table: "OwnerReceivableEntries",
                columns: new[] { "BranchId", "OwnerUserId" });

            migrationBuilder.CreateIndex(
                name: "IX_OwnerReceivableEntries_ClientRequestId",
                table: "OwnerReceivableEntries",
                column: "ClientRequestId",
                unique: true,
                filter: "[ClientRequestId] IS NOT NULL");

            migrationBuilder.CreateIndex(
                name: "IX_OwnerReceivableEntries_OwnerUserId",
                table: "OwnerReceivableEntries",
                column: "OwnerUserId");

            migrationBuilder.CreateIndex(
                name: "IX_OwnerReceivableEntries_PartnerWithdrawalId",
                table: "OwnerReceivableEntries",
                column: "PartnerWithdrawalId");

            migrationBuilder.CreateIndex(
                name: "IX_OwnerReceivableEntries_RecordedByUserId",
                table: "OwnerReceivableEntries",
                column: "RecordedByUserId");

            migrationBuilder.CreateIndex(
                name: "IX_PartnerMonthlyStatements_BranchId_Year_Month",
                table: "PartnerMonthlyStatements",
                columns: new[] { "BranchId", "Year", "Month" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_Partners_BranchId_IsActive",
                table: "Partners",
                columns: new[] { "BranchId", "IsActive" });

            migrationBuilder.CreateIndex(
                name: "IX_Partners_BranchId_UserId",
                table: "Partners",
                columns: new[] { "BranchId", "UserId" });

            migrationBuilder.CreateIndex(
                name: "IX_Partners_UserId",
                table: "Partners",
                column: "UserId");

            migrationBuilder.CreateIndex(
                name: "IX_PartnerStatementLines_PartnerId",
                table: "PartnerStatementLines",
                column: "PartnerId");

            migrationBuilder.CreateIndex(
                name: "IX_PartnerStatementLines_StatementId",
                table: "PartnerStatementLines",
                column: "StatementId");

            migrationBuilder.CreateIndex(
                name: "IX_PartnerWithdrawals_BranchId_OccurredAtUtc",
                table: "PartnerWithdrawals",
                columns: new[] { "BranchId", "OccurredAtUtc" });

            migrationBuilder.CreateIndex(
                name: "IX_PartnerWithdrawals_ClientRequestId",
                table: "PartnerWithdrawals",
                column: "ClientRequestId",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_PartnerWithdrawals_PaidByUserId",
                table: "PartnerWithdrawals",
                column: "PaidByUserId");

            migrationBuilder.CreateIndex(
                name: "IX_PartnerWithdrawals_PartnerId_OccurredAtUtc",
                table: "PartnerWithdrawals",
                columns: new[] { "PartnerId", "OccurredAtUtc" });

            migrationBuilder.CreateIndex(
                name: "IX_PartnerWithdrawals_RecordedByUserId",
                table: "PartnerWithdrawals",
                column: "RecordedByUserId");

            migrationBuilder.AddForeignKey(
                name: "FK_CapitalTransactions_Partners_PartnerId",
                table: "CapitalTransactions",
                column: "PartnerId",
                principalTable: "Partners",
                principalColumn: "Id",
                onDelete: ReferentialAction.Restrict);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropForeignKey(
                name: "FK_CapitalTransactions_Partners_PartnerId",
                table: "CapitalTransactions");

            migrationBuilder.DropTable(
                name: "OwnerReceivableEntries");

            migrationBuilder.DropTable(
                name: "PartnerStatementLines");

            migrationBuilder.DropTable(
                name: "PartnerWithdrawals");

            migrationBuilder.DropTable(
                name: "PartnerMonthlyStatements");

            migrationBuilder.DropTable(
                name: "Partners");

            migrationBuilder.DropIndex(
                name: "IX_CapitalTransactions_PartnerId",
                table: "CapitalTransactions");

            migrationBuilder.DeleteData(
                table: "RolePermissions",
                keyColumn: "Id",
                keyValue: new Guid("a1f4c2d5-8b6e-4d3f-9c7a-5e8b1d4f6a80"));

            migrationBuilder.DeleteData(
                table: "Permissions",
                keyColumn: "Id",
                keyValue: new Guid("9d3e1b4c-7a5f-4c2d-8b6e-4f7a0c3d5e79"));

            migrationBuilder.DropColumn(
                name: "PartnerId",
                table: "CapitalTransactions");
        }
    }
}
