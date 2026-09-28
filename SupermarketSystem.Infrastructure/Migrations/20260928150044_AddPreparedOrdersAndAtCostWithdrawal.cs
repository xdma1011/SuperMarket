using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace SupermarketSystem.Infrastructure.Migrations
{
    /// <inheritdoc />
    public partial class AddPreparedOrdersAndAtCostWithdrawal : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<DateTime>(
                name: "ClosedAtUtc",
                table: "SuspendedSales",
                type: "datetime2",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "Note",
                table: "SuspendedSales",
                type: "nvarchar(200)",
                maxLength: 200,
                nullable: true);

            migrationBuilder.AddColumn<Guid>(
                name: "SaleInvoiceId",
                table: "SuspendedSales",
                type: "uniqueidentifier",
                nullable: true);

            migrationBuilder.AddColumn<int>(
                name: "Status",
                table: "SuspendedSales",
                type: "int",
                nullable: false,
                defaultValue: 0);

            migrationBuilder.AddColumn<DateOnly>(
                name: "TicketDateUtc",
                table: "SuspendedSales",
                type: "date",
                nullable: false,
                defaultValue: new DateOnly(1, 1, 1));

            migrationBuilder.AddColumn<int>(
                name: "TicketNumber",
                table: "SuspendedSales",
                type: "int",
                nullable: false,
                defaultValue: 0);

            migrationBuilder.AddColumn<bool>(
                name: "IsAtCostWithdrawal",
                table: "SaleInvoices",
                type: "bit",
                nullable: false,
                defaultValue: false);

            migrationBuilder.AddColumn<bool>(
                name: "IsDeductedFromShare",
                table: "SaleInvoices",
                type: "bit",
                nullable: false,
                defaultValue: false);

            migrationBuilder.InsertData(
                table: "Permissions",
                columns: new[] { "Id", "Code", "CreatedAtUtc", "CreatedByUserId", "Description", "Name", "UpdatedAtUtc", "UpdatedByUserId" },
                values: new object[] { new Guid("7c1d9e2a-5b3f-4e8a-9d6c-2f4a8b1e3c57"), "Sales.AtCostWithdrawal", new DateTime(2026, 9, 28, 0, 0, 0, 0, DateTimeKind.Utc), null, "Take goods from the store at cost price - paid into the drawer, or deducted from the partner's share (recorded as an open balance until the partners module exists).", "Withdraw goods at cost (owner/partner)", null, null });

            migrationBuilder.InsertData(
                table: "RolePermissions",
                columns: new[] { "Id", "PermissionId", "RoleId" },
                values: new object[] { new Guid("8e2f0a3b-6c4d-4f9b-8e7d-3a5b9c2f4d68"), new Guid("7c1d9e2a-5b3f-4e8a-9d6c-2f4a8b1e3c57"), new Guid("50e6125a-cac0-4d82-a0b8-9f3c6fff59d7") });

            // SuspendedSales كان جدول بلا استخدام بأي كود، بس لو فيه صفوف قديمة (من SSMS مثلًا) كلها رح تاخد
            // نفس (يوم 0001-01-01، رقم 0) وتفشّل الفهرس الفريد تحت. بنعتبرها ملغاة، وبنعطيها يومها الحقيقي
            // ورقم متسلسل لكل (فرع، يوم) قبل إنشاء الفهرس.
            migrationBuilder.Sql(@"
WITH Numbered AS (
    SELECT Id, CAST(CreatedAtUtc AS date) AS TicketDay,
           ROW_NUMBER() OVER (PARTITION BY BranchId, CAST(CreatedAtUtc AS date) ORDER BY CreatedAtUtc, Id) AS Seq
    FROM SuspendedSales
    WHERE TicketNumber = 0
)
UPDATE s SET s.Status = 3, s.TicketDateUtc = n.TicketDay, s.TicketNumber = n.Seq, s.ClosedAtUtc = SYSUTCDATETIME()
FROM SuspendedSales s JOIN Numbered n ON n.Id = s.Id;");

            migrationBuilder.CreateIndex(
                name: "IX_SuspendedSales_BranchId_Status",
                table: "SuspendedSales",
                columns: new[] { "BranchId", "Status" });

            migrationBuilder.CreateIndex(
                name: "IX_SuspendedSales_BranchId_TicketDateUtc_TicketNumber",
                table: "SuspendedSales",
                columns: new[] { "BranchId", "TicketDateUtc", "TicketNumber" },
                unique: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropIndex(
                name: "IX_SuspendedSales_BranchId_Status",
                table: "SuspendedSales");

            migrationBuilder.DropIndex(
                name: "IX_SuspendedSales_BranchId_TicketDateUtc_TicketNumber",
                table: "SuspendedSales");

            migrationBuilder.DeleteData(
                table: "RolePermissions",
                keyColumn: "Id",
                keyValue: new Guid("8e2f0a3b-6c4d-4f9b-8e7d-3a5b9c2f4d68"));

            migrationBuilder.DeleteData(
                table: "Permissions",
                keyColumn: "Id",
                keyValue: new Guid("7c1d9e2a-5b3f-4e8a-9d6c-2f4a8b1e3c57"));

            migrationBuilder.DropColumn(
                name: "ClosedAtUtc",
                table: "SuspendedSales");

            migrationBuilder.DropColumn(
                name: "Note",
                table: "SuspendedSales");

            migrationBuilder.DropColumn(
                name: "SaleInvoiceId",
                table: "SuspendedSales");

            migrationBuilder.DropColumn(
                name: "Status",
                table: "SuspendedSales");

            migrationBuilder.DropColumn(
                name: "TicketDateUtc",
                table: "SuspendedSales");

            migrationBuilder.DropColumn(
                name: "TicketNumber",
                table: "SuspendedSales");

            migrationBuilder.DropColumn(
                name: "IsAtCostWithdrawal",
                table: "SaleInvoices");

            migrationBuilder.DropColumn(
                name: "IsDeductedFromShare",
                table: "SaleInvoices");
        }
    }
}
