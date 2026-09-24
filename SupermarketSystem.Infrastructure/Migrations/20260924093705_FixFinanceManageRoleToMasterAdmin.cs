using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace SupermarketSystem.Infrastructure.Migrations
{
    /// <inheritdoc />
    public partial class FixFinanceManageRoleToMasterAdmin : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            // لو حدا ربط Finance.Manage بـMaster Admin يدويًا (SSMS) كحل مؤقت،
            // الفهرس الفريد (RoleId, PermissionId) كان رح يفشّل UpdateData تحت.
            // بنشيل الصف اليدوي المكرَّر بس (مش صف البذر نفسه).
            migrationBuilder.Sql(
                "DELETE FROM [RolePermissions] " +
                "WHERE [RoleId] = '50e6125a-cac0-4d82-a0b8-9f3c6fff59d7' " +
                "AND [PermissionId] = 'c4d5e6f7-a8b9-4c0d-9e1f-2a3b4c5d6e7f' " +
                "AND [Id] <> 'd3e4f5a6-b7c8-4d9e-8f0a-1b2c3d4e5f6a';");

            migrationBuilder.UpdateData(
                table: "RolePermissions",
                keyColumn: "Id",
                keyValue: new Guid("d3e4f5a6-b7c8-4d9e-8f0a-1b2c3d4e5f6a"),
                column: "RoleId",
                value: new Guid("50e6125a-cac0-4d82-a0b8-9f3c6fff59d7"));
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.UpdateData(
                table: "RolePermissions",
                keyColumn: "Id",
                keyValue: new Guid("d3e4f5a6-b7c8-4d9e-8f0a-1b2c3d4e5f6a"),
                column: "RoleId",
                value: new Guid("5d0b3578-417e-4706-ab9b-fc9a208b6642"));
        }
    }
}
