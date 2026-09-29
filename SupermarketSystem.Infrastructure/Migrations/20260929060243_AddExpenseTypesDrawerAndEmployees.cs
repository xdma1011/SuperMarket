using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

#pragma warning disable CA1814 // Prefer jagged arrays over multidimensional

namespace SupermarketSystem.Infrastructure.Migrations
{
    /// <inheritdoc />
    public partial class AddExpenseTypesDrawerAndEmployees : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<Guid>(
                name: "EmployeePaymentId",
                table: "Expenses",
                type: "uniqueidentifier",
                nullable: true);

            migrationBuilder.AddColumn<Guid>(
                name: "ExpenseTypeId",
                table: "Expenses",
                type: "uniqueidentifier",
                nullable: true);

            migrationBuilder.AddColumn<bool>(
                name: "PaidFromDrawer",
                table: "Expenses",
                type: "bit",
                nullable: false,
                defaultValue: false);

            migrationBuilder.CreateTable(
                name: "Employees",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    BranchId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    FullName = table.Column<string>(type: "nvarchar(150)", maxLength: 150, nullable: false),
                    Phone = table.Column<string>(type: "nvarchar(30)", maxLength: 30, nullable: true),
                    MonthlySalary = table.Column<decimal>(type: "decimal(18,4)", nullable: false),
                    IsActive = table.Column<bool>(type: "bit", nullable: false),
                    Notes = table.Column<string>(type: "nvarchar(500)", maxLength: 500, nullable: true),
                    CreatedAtUtc = table.Column<DateTime>(type: "datetime2", nullable: false),
                    CreatedByUserId = table.Column<Guid>(type: "uniqueidentifier", nullable: true),
                    UpdatedAtUtc = table.Column<DateTime>(type: "datetime2", nullable: true),
                    UpdatedByUserId = table.Column<Guid>(type: "uniqueidentifier", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_Employees", x => x.Id);
                    table.ForeignKey(
                        name: "FK_Employees_Branches_BranchId",
                        column: x => x.BranchId,
                        principalTable: "Branches",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "ExpenseTypes",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    Name = table.Column<string>(type: "nvarchar(100)", maxLength: 100, nullable: false),
                    IsActive = table.Column<bool>(type: "bit", nullable: false),
                    SortOrder = table.Column<int>(type: "int", nullable: false),
                    LegacyCategory = table.Column<int>(type: "int", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_ExpenseTypes", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "EmployeePayments",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    EmployeeId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    BranchId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    Type = table.Column<int>(type: "int", nullable: false),
                    GrossAmount = table.Column<decimal>(type: "decimal(18,4)", nullable: false),
                    AdvanceDeducted = table.Column<decimal>(type: "decimal(18,4)", nullable: false),
                    NetPaid = table.Column<decimal>(type: "decimal(18,4)", nullable: false),
                    PeriodYear = table.Column<int>(type: "int", nullable: true),
                    PeriodMonth = table.Column<int>(type: "int", nullable: true),
                    PaidFromDrawer = table.Column<bool>(type: "bit", nullable: false),
                    OccurredAtUtc = table.Column<DateTime>(type: "datetime2", nullable: false),
                    Notes = table.Column<string>(type: "nvarchar(500)", maxLength: 500, nullable: true),
                    RecordedByUserId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    ClientRequestId = table.Column<Guid>(type: "uniqueidentifier", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_EmployeePayments", x => x.Id);
                    table.ForeignKey(
                        name: "FK_EmployeePayments_Branches_BranchId",
                        column: x => x.BranchId,
                        principalTable: "Branches",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_EmployeePayments_Employees_EmployeeId",
                        column: x => x.EmployeeId,
                        principalTable: "Employees",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_EmployeePayments_Users_RecordedByUserId",
                        column: x => x.RecordedByUserId,
                        principalTable: "Users",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.InsertData(
                table: "ExpenseTypes",
                columns: new[] { "Id", "IsActive", "LegacyCategory", "Name", "SortOrder" },
                values: new object[,]
                {
                    { new Guid("e1a0b1c2-0001-4a5b-9c6d-7e8f90a1b201"), true, 1, "إيجار", 1 },
                    { new Guid("e1a0b1c2-0002-4a5b-9c6d-7e8f90a1b202"), true, 2, "كهرباء", 2 },
                    { new Guid("e1a0b1c2-0003-4a5b-9c6d-7e8f90a1b203"), true, 3, "ماء", 3 },
                    { new Guid("e1a0b1c2-0004-4a5b-9c6d-7e8f90a1b204"), true, 4, "رواتب", 4 },
                    { new Guid("e1a0b1c2-0005-4a5b-9c6d-7e8f90a1b205"), true, 5, "أخرى", 99 },
                    { new Guid("e1a0b1c2-0006-4a5b-9c6d-7e8f90a1b206"), true, null, "تنظيف", 5 }
                });

            migrationBuilder.CreateIndex(
                name: "IX_Expenses_EmployeePaymentId",
                table: "Expenses",
                column: "EmployeePaymentId",
                unique: true,
                filter: "[EmployeePaymentId] IS NOT NULL");

            migrationBuilder.CreateIndex(
                name: "IX_Expenses_ExpenseTypeId",
                table: "Expenses",
                column: "ExpenseTypeId");

            migrationBuilder.CreateIndex(
                name: "IX_EmployeePayments_BranchId_OccurredAtUtc",
                table: "EmployeePayments",
                columns: new[] { "BranchId", "OccurredAtUtc" });

            migrationBuilder.CreateIndex(
                name: "IX_EmployeePayments_ClientRequestId",
                table: "EmployeePayments",
                column: "ClientRequestId",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_EmployeePayments_EmployeeId_OccurredAtUtc",
                table: "EmployeePayments",
                columns: new[] { "EmployeeId", "OccurredAtUtc" });

            migrationBuilder.CreateIndex(
                name: "IX_EmployeePayments_RecordedByUserId",
                table: "EmployeePayments",
                column: "RecordedByUserId");

            migrationBuilder.CreateIndex(
                name: "IX_Employees_BranchId_IsActive",
                table: "Employees",
                columns: new[] { "BranchId", "IsActive" });

            migrationBuilder.CreateIndex(
                name: "IX_ExpenseTypes_Name",
                table: "ExpenseTypes",
                column: "Name",
                unique: true);

            migrationBuilder.AddForeignKey(
                name: "FK_Expenses_EmployeePayments_EmployeePaymentId",
                table: "Expenses",
                column: "EmployeePaymentId",
                principalTable: "EmployeePayments",
                principalColumn: "Id",
                onDelete: ReferentialAction.Restrict);

            migrationBuilder.AddForeignKey(
                name: "FK_Expenses_ExpenseTypes_ExpenseTypeId",
                table: "Expenses",
                column: "ExpenseTypeId",
                principalTable: "ExpenseTypes",
                principalColumn: "Id",
                onDelete: ReferentialAction.Restrict);

            // المصاريف القديمة: نوعها من التصنيف القديم (Category 1-5) - بيانات بس، بلا أي مصروف بيتغيّر مبلغه.
            migrationBuilder.Sql(@"
UPDATE [Expenses] SET [ExpenseTypeId] = CASE [Category]
    WHEN 1 THEN 'e1a0b1c2-0001-4a5b-9c6d-7e8f90a1b201'
    WHEN 2 THEN 'e1a0b1c2-0002-4a5b-9c6d-7e8f90a1b202'
    WHEN 3 THEN 'e1a0b1c2-0003-4a5b-9c6d-7e8f90a1b203'
    WHEN 4 THEN 'e1a0b1c2-0004-4a5b-9c6d-7e8f90a1b204'
    ELSE 'e1a0b1c2-0005-4a5b-9c6d-7e8f90a1b205' END
WHERE [ExpenseTypeId] IS NULL;");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropForeignKey(
                name: "FK_Expenses_EmployeePayments_EmployeePaymentId",
                table: "Expenses");

            migrationBuilder.DropForeignKey(
                name: "FK_Expenses_ExpenseTypes_ExpenseTypeId",
                table: "Expenses");

            migrationBuilder.DropTable(
                name: "EmployeePayments");

            migrationBuilder.DropTable(
                name: "ExpenseTypes");

            migrationBuilder.DropTable(
                name: "Employees");

            migrationBuilder.DropIndex(
                name: "IX_Expenses_EmployeePaymentId",
                table: "Expenses");

            migrationBuilder.DropIndex(
                name: "IX_Expenses_ExpenseTypeId",
                table: "Expenses");

            migrationBuilder.DropColumn(
                name: "EmployeePaymentId",
                table: "Expenses");

            migrationBuilder.DropColumn(
                name: "ExpenseTypeId",
                table: "Expenses");

            migrationBuilder.DropColumn(
                name: "PaidFromDrawer",
                table: "Expenses");
        }
    }
}
